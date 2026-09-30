using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MC.Shared;
using UnityEngine;
using ItemType = ItemDrop.ItemData.ItemType;
using Object = UnityEngine.Object;
#endif

namespace MC.Combat.ShieldsTowerWallMod;

// Debug build only (calls vanish in Release). In-world self tests, run by world probe (tools/Test-InWorld.ps1,
// -Mod Shields.TowerWall) in throwaway world: god mode, world modifier StaminaRate 0. Design 7.4:
//   tower.data    default towers carry the 2.0.1 values over their vanilla snapshot; creature copies, ShieldBanded,
//                 ShieldIronSquare untouched; ItemKinds say Shield; craft-every-weapon list has no tower; tooltips
//                 (bash cooldown line too); no NG+ bonus on a tower copy; bash Attack fields (stamina 20, ShieldPunch);
//                 item stands and armor stands still take a two-handed tower; every animator parameter and prefab me
//                 use exist. NOTE: effect lists, stand lists, ShieldIronSquare recipe, armor slow values, NG+ game
//                 numbers, training dummy prefab.
//   tower.rules   wire round trip (layout 2: speed, cooldown, stagger lock), clamp, unknown layout, list parsing,
//                 rules key, join verdicts, animation names (any case; old ShieldUp read as ShieldPunch), fallback chain, Custom
//                 trigger set (held attacks refused), lowest BashStagger still a bash, player hits made blockable only
//                 as PvP attacks, tooltip lines, generation rules, 0.5 s apply delay, Towers validation
//   tower.hands   two hands on every equip path, back shield slot, no auto-equip on pickup, FixHands drop a hidden
//                 sword, skeleton that pick up a player tower keep a vanilla shield and its weapon
//   tower.slow    carried slow, sprint floor, braced slow, tower on the back = no slow
//   tower.block   braced: Braced effect on HUD, block formula, unblockable frontal hits blocked (not from behind, not
//                 falls, not from a creature that is not hostile, not with the setting off), stagger resist while
//                 stamina last (sum with another effect never below -1), exhausted HUD, guard break, no push from the
//                 front, effect gone when block released or tower put away
//   tower.bash    every BashAnimation on a Troll: start, hit, fallback, timings, clips, stagger and damage per hit;
//                 swing speed (clip's own speed x BashAnimationSpeed, same value in the ZDO other games read, back to 1
//                 after; hit later than at speed 1); Custom refusals (held attack too); one hit per bash with two Hit
//                 events; a bash that never end stopped and fallen back (watchdog C); bashes a Draugr need; cooldown
//                 through vanilla input (held attack button: starts BashCooldown apart; a press too soon refused, at 0
//                 accepted; one press after the swing kept till the time is up); stamina of one bash with the world
//                 stamina rate back
//   tower.lock    lock limit (cooldown off, speed 1): first bash stagger, bashes within 6 s add only landed x 1, bash
//                 after the lock stagger; BashStaggerLock 0 = no limit (second bash from an empty bar); one heavy
//                 stagger per bash (two Draugr: the middle one, with the other right then left); bash on a creature
//                 already staggering start no lock
//   tower.ng      world level 1: no gear bonus on a tower, bash stagger x NG+ health factor, bash alert the creature
//   tower.lock, tower.ng and tower.toggle pin BashAnimation OtherPunch on purpose: last option of the fallback chain,
//   so no watchdog verdict can swap the clip under a stagger measure (stagger and lock math same for every clip).
//   Default ShieldPunch and every other option: tower.bash.
// Bash lane (Press, group bash): before me put a creature in front, me turn the player to a lane where nothing solid
// or alive stand between them (capsule over the bash rays' height band). Vanilla Attack.DoMeleeAttack stop each ray at
// the first collider (m_hitThroughWalls off): a rock between would take the bash. Each Punchstep clip step the player
// forward (root motion), so after many bashes the player may stand right in front of a rock.
//   tower.toggle  what OnDeactivated / OnActivated do: items everywhere vanilla and back, sword put away, bash
//                 cancelled before its hit, slowed swing back to the clip's own speed when cancelled mid-clip
// Me force rules only through ServerRules.TestRules and the block key through SelfTestHooks.HoldBlock, never the
// config. Me put back god mode, StaminaRate and WorldLevel keys, stamina, health, hands, the player's place and facing
// (bash steps and lane turns: next test start where this one did); take back every item me give; destroy what me
// spawn. Creatures me spawn have their AI off (stand still, never attack).
internal static class SelfTests
{
    private const string DataName = "tower.data";
    private const string RulesName = "tower.rules";
    private const string HandsName = "tower.hands";
    private const string SlowName = "tower.slow";
    private const string BlockName = "tower.block";
    private const string BashName = "tower.bash";
    private const string LockName = "tower.lock";
    private const string NgName = "tower.ng";
    private const string ToggleName = "tower.toggle";

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        _registered = true;
        SelfTest.Register(DataName, RunData);
        SelfTest.Register(RulesName, RunRules);
        SelfTest.Register(HandsName, RunHands);
        SelfTest.Register(SlowName, RunSlow);
        SelfTest.Register(BlockName, RunBlock);
        SelfTest.Register(BashName, RunBash);
        SelfTest.Register(LockName, RunLock);
        SelfTest.Register(NgName, RunNg);
        SelfTest.Register(ToggleName, RunToggle);
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        _registered = false;
        SelfTest.Unregister(DataName);
        SelfTest.Unregister(RulesName);
        SelfTest.Unregister(HandsName);
        SelfTest.Unregister(SlowName);
        SelfTest.Unregister(BlockName);
        SelfTest.Unregister(BashName);
        SelfTest.Unregister(LockName);
        SelfTest.Unregister(NgName);
        SelfTest.Unregister(ToggleName);
        SelfTestHooks.Clear();
        ServerRules.TestRules = null;
#endif
    }

#if DEBUG
    private const string IronTower = "ShieldIronTower";

    private static readonly WaitForFixedUpdate Fixed = new WaitForFixedUpdate();

    // True while mod is active (Register in OnActivated, Unregister in OnDeactivated): tower.toggle turn TowerSync
    // back on in its finally only then.
    private static bool _registered;

    private sealed class Checks
    {
        private readonly string _name;
        private readonly List<string> _failures = new List<string>();
        private int _count;

        internal Checks(string name) => _name = name;

        internal void Check(bool ok, string what)
        {
            _count++;
            if (!ok)
            {
                _failures.Add(what);
            }
        }

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

    private sealed class Box<T>
    {
        internal T Value;
    }

    // Me hold what a test change on the player and the world, put it back in Done (finally, no yield).
    private sealed class Rig
    {
        internal readonly Player P;
        internal readonly Inventory Inv;
        private readonly string _test;
        private readonly List<ItemDrop.ItemData> _items = new List<ItemDrop.ItemData>();
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly ItemDrop.ItemData _right;
        private readonly ItemDrop.ItemData _left;
        private readonly bool _god;
        private bool _staminaTaken;
        private bool _hadStamina;
        private float _stamina;
        private bool _levelSet;
        private bool _hadLevel;
        private float _level;
        private readonly Vector3 _pos;
        private readonly Quaternion _rot;
        private readonly Quaternion _yaw;

        internal Rig(string test)
        {
            _test = test;
            P = Player.m_localPlayer;
            Inv = P.GetInventory();
            _god = P.InGodMode();
            _right = P.m_rightItem;
            _left = P.m_leftItem;
            _pos = P.transform.position;
            _rot = P.transform.rotation;
            _yaw = P.m_lookYaw;
            EmptyHands();
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
            if (P.m_hiddenRightItem != null)
            {
                P.UnequipItem(P.m_hiddenRightItem, false);
            }
            if (P.m_hiddenLeftItem != null)
            {
                P.UnequipItem(P.m_hiddenLeftItem, false);
            }
        }

        // New item in the inventory (own SharedData copy made from the prefab, like crafting).
        internal ItemDrop.ItemData Give(string prefab, int quality = 1)
        {
            var item = Inv.AddItem(prefab, 1, quality, 0, 0L, "", false);
            Track(item);
            return item;
        }

        internal void Track(ItemDrop.ItemData item)
        {
            if (item != null && !_items.Contains(item))
            {
                _items.Add(item);
            }
        }

        internal void Track(GameObject go)
        {
            if (go != null)
            {
                _spawned.Add(go);
            }
        }

        // Creature 5 m ahead, AI off at once (OnDisable take it out of BaseAI.Instances): no walk, no target, no
        // attack. Its OnDamaged stay hooked (Awake): hurt still alert it and set its target.
        internal Character Creature(string prefab)
        {
            var go = ZNetScene.instance.GetPrefab(prefab);
            if (go == null)
            {
                return null;
            }
            var fwd = Flat(P.transform.forward);
            var obj = Object.Instantiate(go, P.transform.position + fwd * 5f, Quaternion.LookRotation(-fwd));
            _spawned.Add(obj);
            var ai = obj.GetComponent<BaseAI>();
            if (ai != null)
            {
                ai.enabled = false;
            }
            return obj.GetComponent<Character>();
        }

        // Chest 3 m to the right (support off: never break and spill).
        internal Container Chest(out string name)
        {
            var prefab = ZNetScene.instance.GetPrefab("piece_chest_wood");
            if (prefab == null || prefab.GetComponent<Container>() == null)
            {
                prefab = null;
                foreach (var go in ZNetScene.instance.m_prefabs)
                {
                    if (go != null && go.name.StartsWith("piece_chest", StringComparison.Ordinal) && go.GetComponent<Container>() != null)
                    {
                        prefab = go;
                        break;
                    }
                }
            }
            name = prefab != null ? prefab.name : "none";
            if (prefab == null)
            {
                return null;
            }
            var pos = P.transform.position + Flat(P.transform.right) * 3f;
            pos.y = ZoneSystem.instance.GetGroundHeight(pos);
            var obj = Object.Instantiate(prefab, pos, Quaternion.identity);
            _spawned.Add(obj);
            var wnt = obj.GetComponent<WearNTear>();
            if (wnt != null)
            {
                wnt.enabled = false;
            }
            return obj.GetComponent<Container>();
        }

        // Item on the ground, out of auto-pickup reach (own SharedData copy from the prefab).
        internal ItemDrop Ground(string prefab)
        {
            var go = ObjectDB.instance.GetItemPrefab(prefab);
            if (go == null)
            {
                return null;
            }
            var pos = P.transform.position - Flat(P.transform.right) * 4f + Vector3.up;
            var obj = Object.Instantiate(go, pos, Quaternion.identity);
            _spawned.Add(obj);
            return obj.GetComponent<ItemDrop>();
        }

        internal void TakeStaminaRate()
        {
            var zone = ZoneSystem.instance;
            _hadStamina = zone.GetGlobalKey(GlobalKeys.StaminaRate, out _stamina);
            _staminaTaken = true;
            zone.RemoveGlobalKey(GlobalKeys.StaminaRate);
        }

        internal void SetWorldLevel(int level)
        {
            var zone = ZoneSystem.instance;
            _hadLevel = zone.GetGlobalKey(GlobalKeys.WorldLevel, out _level);
            _levelSet = true;
            zone.SetGlobalKey(GlobalKeys.WorldLevel, level);
        }

        internal void Done()
        {
            Try("hooks", () =>
            {
                SelfTestHooks.Clear();
                ServerRules.TestRules = null;
            });
            Try("items", () =>
            {
                foreach (var item in _items)
                {
                    if (item == null || P == null)
                    {
                        continue;
                    }
                    if (ReferenceEquals(P.m_hiddenLeftItem, item) || ReferenceEquals(P.m_hiddenRightItem, item) || P.IsItemEquiped(item))
                    {
                        P.UnequipItem(item, false);
                    }
                    if (Inv.ContainsItem(item))
                    {
                        Inv.RemoveItem(item);
                    }
                }
                _items.Clear();
            });
            Try("spawned", () =>
            {
                foreach (var go in _spawned)
                {
                    if (go != null)
                    {
                        ZNetScene.instance.Destroy(go);
                    }
                }
                _spawned.Clear();
            });
            Try("keys", () =>
            {
                var zone = ZoneSystem.instance;
                if (_staminaTaken && _hadStamina)
                {
                    zone.SetGlobalKey(GlobalKeys.StaminaRate, _stamina);
                }
                if (_levelSet)
                {
                    if (_hadLevel)
                    {
                        zone.SetGlobalKey(GlobalKeys.WorldLevel, _level);
                    }
                    else
                    {
                        zone.RemoveGlobalKey(GlobalKeys.WorldLevel);
                    }
                }
            });
            Try("player", () =>
            {
                if (P == null)
                {
                    return;
                }
                P.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectPoison, true);
                P.m_stamina = P.GetMaxStamina();
                P.m_staminaRegenTimer = 0f;
                P.m_staggerDamage = 0f;
                P.m_pushForce = Vector3.zero;
                P.SetHealth(P.GetMaxHealth());
                P.SetGodMode(_god);
                if (_right != null && Inv.ContainsItem(_right) && !P.IsItemEquiped(_right))
                {
                    P.EquipItem(_right, false);
                }
                if (_left != null && Inv.ContainsItem(_left) && !P.IsItemEquiped(_left))
                {
                    P.EquipItem(_left, false);
                }
            });
            Try("place", () =>
            {
                if (P == null)
                {
                    return;
                }
                P.transform.SetPositionAndRotation(_pos, _rot);
                var body = P.m_body;
                if (body != null)
                {
                    body.position = _pos;
                    body.rotation = _rot;
                    if (!body.isKinematic)
                    {
                        body.linearVelocity = Vector3.zero;
                    }
                }
                P.m_lookYaw = _yaw;
                P.SetLookDir(_yaw * Vector3.forward);
            });
        }

        private void Try(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                SelfTest.Note(_test, $"clean-up of the {what} failed: {e.GetType().Name}: {e.Message}");
            }
        }
    }

    // One press of the attack button, what came of it.
    private sealed class Swing
    {
        internal bool Started;
        internal string Trigger = "";
        internal BashAnimationKind Mode;
        internal BashAnimationKind ModeAfter;
        internal float Skill;                 // Blocking skill factor at the press
        internal float Drain;                 // target's stagger bar drain per second (reading tolerance)
        internal float InAttackAt = -1f;      // s after the press, -1 = never
        internal float HitAt = -1f;
        internal float EndAt = -1f;
        internal int Events;                  // Hit events of our bash clones in this swing
        internal int Skipped;                 // of which skipped (one hit per bash)
        internal string Clips = "";
        internal float Gained = float.NaN;    // target's stagger bar gained by the hit (drain since put back)
        internal float Lost;                  // target's health lost by the hit
        internal bool Staggered;              // target's bar reached its threshold at the hit
        internal float Factor = 1f;           // BashAnimationSpeed applied at the press
        internal float RawSpeed = float.NaN;  // 0.2 s after the press: clip's own speed (BashSpeed), me unscaled
        internal float AnimSpeed = float.NaN; // same time: animator speed on this game
        internal float ZdoSpeed = float.NaN;  // same time: speed in the ZDO, what other games set on their copy
        internal bool StartScaled;            // start speed scaled by me (no Speed event before the attack state)
        internal float SpeedAfter = float.NaN; // animator speed two fixed steps after the swing
        internal float StaminaUsed;           // stamina before the press minus the lowest seen during the swing
        internal string Blocker = "";         // Hit event but target took nothing: what stand in the lane after the swing

        // Bash Hit event came (animation side).
        internal bool Hit => Events > 0 && !float.IsNaN(Gained);

        // And target took it (health or stagger moved).
        internal bool Landed => Hit && (Lost > 0.001f || Gained > 0.5f);
    }

    // ---------- helpers ----------

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string T(float seconds) => seconds < 0f ? "never" : F(seconds) + " s";

    private static bool Near(float a, float b, float tolerance = 0.01f) => Mathf.Abs(a - b) <= tolerance;

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
    }

    private static string Name(ItemDrop.ItemData item) => item == null ? "empty" : item.m_shared.m_name;

    private static string Hands(Player p) => $"right {Name(p.m_rightItem)}, left {Name(p.m_leftItem)}";

    // Only this item in the hands, in that hand.
    private static bool Only(Player p, ItemDrop.ItemData item, bool left) =>
        left
            ? ReferenceEquals(p.m_leftItem, item) && p.m_rightItem == null && item.m_equipped
            : ReferenceEquals(p.m_rightItem, item) && p.m_leftItem == null && item.m_equipped;

    private static ItemDrop.ItemData.SharedData SharedOf(string prefab)
    {
        var go = ObjectDB.instance.GetItemPrefab(prefab);
        if (go == null)
        {
            return null;
        }
        var drop = go.GetComponent<ItemDrop>();
        return drop != null && drop.m_itemData != null ? drop.m_itemData.m_shared : null;
    }

    private static TowerSnapshot SnapshotOf(string prefab) => TowerCatalog.SnapshotOfPrefab(ObjectDB.instance.GetItemPrefab(prefab));

    // Vanilla block formula (HitData.DamageTypes.ApplyArmor): what of this blunt get through this block armor.
    private static float Through(float blunt, float armor)
    {
        var d = new HitData.DamageTypes { m_blunt = blunt };
        d.ApplyArmor(armor);
        return d.m_blunt;
    }

    // What 1 blunt become on this character after its resistances.
    private static float BluntTaken(Character c)
    {
        var probe = new HitData();
        probe.m_damage.m_blunt = 1f;
        probe.ApplyResistance(c.GetDamageModifiers(), out _);
        return probe.m_damage.m_blunt;
    }

    private static float SpeedMods(Player p)
    {
        var speed = 1f;
        p.GetSEMan().ApplyStatusEffectSpeedMods(ref speed, p.transform.forward);
        return speed;
    }

    private static float Drain(Character c) => c.m_staggerDamageFactor > 0f ? c.GetStaggerTreshold() / 5f : 0f;

    private static bool IsVanilla(ItemDrop.ItemData.SharedData s, TowerSnapshot v) =>
        s != null && v != null && s.m_itemType == v.ItemType && s.m_attachOverride == v.AttachOverride
        && Near(s.m_blockPower, v.BlockPower) && Near(s.m_movementModifier, v.MovementModifier)
        && Near(s.m_timedBlockBonus, v.TimedBlockBonus) && s.m_blockable == v.Blockable
        && ReferenceEquals(s.m_attack, v.Attack);

    private static bool IsTower(ItemDrop.ItemData.SharedData s, TowerSnapshot v, TowerRules r) =>
        s != null && v != null && s.m_itemType == ItemType.TwoHandedWeaponLeft && s.m_attachOverride == ItemType.Shield
        && Near(s.m_blockPower, v.BlockPower * r.BlockArmorMultiplier)
        && Near(s.m_movementModifier, -r.CarrySlowPercent / 100f);

    private static int Count(EffectList e) => e != null && e.m_effectPrefabs != null ? e.m_effectPrefabs.Length : 0;

    private static string Fx(EffectList hit, EffectList block, EffectList start, EffectList trigger) =>
        $"hit {Count(hit)}, block {Count(block)}, start {Count(start)}, trigger {Count(trigger)}";

    private static IEnumerator Frames(int n)
    {
        for (var i = 0; i < n; i++)
        {
            yield return null;
        }
    }

    private static IEnumerator Until(Func<bool> done, float seconds)
    {
        var until = Time.time + seconds;
        while (!done() && Time.time < until)
        {
            yield return null;
        }
    }

    private static IEnumerator UntilTime(float time)
    {
        while (Time.time < time)
        {
            yield return null;
        }
    }

    // Me force rules in memory (never config), wait till TowerSync applied them (0.5 s after change).
    private static IEnumerator UseRules(TowerRules rules)
    {
        ServerRules.TestRules = rules;
        var until = Time.unscaledTime + 3f;
        while (Time.unscaledTime < until && (TowerSync.HasWork || TowerSync.AppliedKey != rules.Key))
        {
            yield return null;
        }
    }

    private static TowerRules WithAnimation(BashAnimationKind kind, string custom = "") =>
        TowerRules.Defaults().With(r =>
        {
            r.BashAnimation = kind;
            r.BashCustomTrigger = custom;
        });

    private static IEnumerator WaitIdle(Player p)
    {
        var until = Time.time + 4f;
        while (Time.time < until && (p.InAttack() || p.IsStaggering() || p.IsKnockedBack() || p.InMinorAction()))
        {
            yield return null;
        }
        yield return Fixed;
    }

    // Capsule radius in metres (vanilla GetRadius scale creature by scale vector length: x1.73 at scale 1).
    private static float Radius(Character c)
    {
        var col = c.m_collider;
        if (col == null)
        {
            return 0.5f;
        }
        var scale = c.transform.localScale;
        return col.radius * Mathf.Max(scale.x, scale.z);
    }

    // Look where the body face, put the creature just in front (surfaces 0.3 m apart), facing the player, still.
    private static float PlaceInFront(Player p, Character c, float gap = 0.3f)
    {
        var fwd = Flat(p.transform.forward);
        p.SetLookDir(fwd);
        var distance = Radius(p) + Radius(c) + gap;
        var pos = p.transform.position + fwd * distance;
        var rot = Quaternion.LookRotation(-fwd);
        c.transform.SetPositionAndRotation(pos, rot);
        var body = c.m_body;
        if (body != null)
        {
            body.position = pos;
            body.rotation = rot;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
            }
        }
        c.m_pushForce = Vector3.zero;
        return distance;
    }

    // Creature beside the one in front: same distance from the player, turned by sign x the smallest angle that keeps
    // the capsules 0.1 m apart, facing the player, still. Return that angle in degrees.
    private static float PlaceBeside(Player p, Character c, Character middle, float sign)
    {
        var fwd = Flat(p.transform.forward);
        var distance = Flat(middle.transform.position - p.transform.position).magnitude;
        var gap = Radius(middle) + Radius(c) + 0.1f;
        var angle = 2f * Mathf.Asin(Mathf.Clamp01(gap / (2f * Mathf.Max(0.1f, distance)))) * Mathf.Rad2Deg;
        var dir = Quaternion.Euler(0f, sign * angle, 0f) * fwd;
        var pos = p.transform.position + dir * distance;
        var rot = Quaternion.LookRotation(-dir);
        c.transform.SetPositionAndRotation(pos, rot);
        var body = c.m_body;
        if (body != null)
        {
            body.position = pos;
            body.rotation = rot;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
            }
        }
        c.m_pushForce = Vector3.zero;
        return angle;
    }

    // ---------- bash lane ----------

    // Vanilla melee ray layers (Attack.m_attackMask: solid things and characters) plus terrain (ground rising in the
    // lane would lift the creature out of the rays' band).
    private static readonly string[] LaneLayers =
    {
        "Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "character", "character_net",
        "character_ghost", "hitbox", "character_noenv", "vehicle",
    };

    private const float LaneRadius = 0.4f;  // BashAttack.RayWidth
    private const float LaneLow = 0.5f;     // capsule 0.5 m to 1.8 m above the feet: bash rays start 1 m up
    private const float LaneHigh = 1.8f;    // (unarmed attack height), at -0.3 / 0 / +0.3, 0.4 wide
    private const float LaneFan = 10f;      // half width in degrees of the lane checked for one creature ahead
    private const float LaneStep = 10f;     // degrees between the casts of one fan
    private const float TurnStep = 20f;     // degrees between the facings tried, both ways, up to 180
    private const float LaneDrop = 0.8f;    // ground at the lane's end at most this far below the feet

    // Ground a creature can stand on (no characters).
    private static readonly string[] GroundLayers = { "Default", "static_solid", "Default_small", "piece", "terrain", "vehicle" };

    private static readonly RaycastHit[] LaneHits = new RaycastHit[64];
    private static int _laneMask;
    private static int _groundMask;

    private static int LaneMask()
    {
        if (_laneMask == 0)
        {
            _laneMask = LayerMask.GetMask(LaneLayers);
        }
        return _laneMask;
    }

    private static int GroundMask()
    {
        if (_groundMask == 0)
        {
            _groundMask = LayerMask.GetMask(GroundLayers);
        }
        return _groundMask;
    }

    // Centre to far side of a creature put in front with PlaceInFront: how far the lane must be free.
    private static float Reach(Player p, Character c) => Radius(p) + 2f * Radius(c) + 0.3f;

    // Nearest thing in the lane from the player's feet along dir (length m), the player and the given creatures left
    // out (a creature is its IDestructible root, like vanilla's Projectile.FindHitObject). Null = free. A collider the
    // capsule already overlap at the start count too (distance 0, like vanilla's own sphere casts).
    private static string LaneBlocker(Player p, Vector3 dir, float length, Character a, Character b)
    {
        var feet = p.transform.position;
        var low = feet + Vector3.up * (LaneLow + LaneRadius);
        var high = feet + Vector3.up * (LaneHigh - LaneRadius);
        var n = Physics.CapsuleCastNonAlloc(low, high, LaneRadius, dir, LaneHits, length, LaneMask(), QueryTriggerInteraction.Ignore);
        string found = null;
        var nearest = float.MaxValue;
        for (var i = 0; i < n; i++)
        {
            var col = LaneHits[i].collider;
            if (col == null)
            {
                continue;
            }
            var go = Projectile.FindHitObject(col);
            if (go == null || go == p.gameObject || (a != null && go == a.gameObject) || (b != null && go == b.gameObject))
            {
                continue;
            }
            if (LaneHits[i].distance < nearest)
            {
                nearest = LaneHits[i].distance;
                found = $"{go.name} ({LayerMask.LayerToName(col.gameObject.layer)}) {F(nearest)} m ahead";
            }
        }
        return found;
    }

    // Every cast of a fan (dir +- halfFan, LaneStep apart): first blocker, null = all free. Ground under the middle
    // lane's end first: a creature put over a drop fall below the rays' band.
    private static string FanBlocker(Player p, Vector3 dir, float length, float halfFan, Character a, Character b)
    {
        var far = p.transform.position + dir * length + Vector3.up * LaneLow;
        if (!Physics.Raycast(far, Vector3.down, LaneLow + LaneDrop, GroundMask(), QueryTriggerInteraction.Ignore))
        {
            return $"ground more than {F(LaneDrop)} m lower {F(length)} m ahead";
        }
        var steps = Mathf.Max(0, Mathf.CeilToInt(halfFan / LaneStep - 0.001f));
        for (var i = -steps; i <= steps; i++)
        {
            var angle = steps == 0 ? 0f : halfFan * i / steps;
            var blocker = LaneBlocker(p, Quaternion.Euler(0f, angle, 0f) * dir, length, a, b);
            if (blocker != null)
            {
                return blocker;
            }
        }
        return null;
    }

    // Body, look and camera yaw all to dir (vanilla turn the body toward m_lookYaw every grounded tick).
    private static void Face(Player p, Vector3 dir)
    {
        var rot = Quaternion.LookRotation(dir);
        p.m_lookYaw = rot;
        p.transform.rotation = rot;
        var body = p.m_body;
        if (body != null)
        {
            body.rotation = rot;
        }
        p.SetLookDir(dir);
    }

    // Keep the facing when its lane is free, else turn to the nearest free one (TurnStep apart, both ways). A turn and
    // a spot with no free lane are noted (the bash then may hit what stands there: the check after tell).
    private static bool FaceClearLane(Player p, float length, float halfFan, string test, Character a, Character b = null)
    {
        var fwd = Flat(p.transform.forward);
        string first = null;
        for (var k = 0; k * TurnStep <= 360f; k++)
        {
            var offset = k == 0 ? 0f : (k + 1) / 2 * TurnStep * (k % 2 == 1 ? 1f : -1f);
            if (Mathf.Abs(offset) > 180f || (k > 0 && Mathf.Approximately(offset, -180f)))
            {
                continue;
            }
            var dir = Quaternion.Euler(0f, offset, 0f) * fwd;
            var blocker = FanBlocker(p, dir, length, halfFan, a, b);
            if (blocker == null)
            {
                if (k > 0)
                {
                    Face(p, dir);
                    SelfTest.Note(test, $"bash lane: turned the player {F(offset)} degrees ({first} in the way)");
                }
                return true;
            }
            if (first == null)
            {
                first = blocker;
            }
        }
        SelfTest.Note(test, $"bash lane: no free lane of {F(length)} m around the player ({first} ahead); the bash may hit it");
        return false;
    }

    private static string ClipNames(Player p)
    {
        var anim = p.m_animator;
        if (anim == null)
        {
            return "no animator";
        }
        var parts = new List<string>();
        for (var layer = 0; layer < anim.layerCount; layer++)
        {
            var next = anim.GetNextAnimatorClipInfo(layer);
            parts.Add($"{anim.GetLayerName(layer)}: {Clips(anim.GetCurrentAnimatorClipInfo(layer))}"
                      + (next.Length > 0 ? " -> " + Clips(next) : ""));
        }
        return string.Join("; ", parts.ToArray());
    }

    private static string Clips(AnimatorClipInfo[] infos) =>
        infos.Length == 0 ? "-" : string.Join("+", infos.Select(i => i.clip != null ? i.clip.name : "?").ToArray());

    // Bash cooldown of the applied rules over (Press wait for it: a press too soon is refused, tested on its own).
    private static IEnumerator WaitCooldown(Player p)
    {
        var until = Time.time + TowerRules.MaxBashCooldown + 1f;
        while (Time.time < until && BashWatch.InCooldown(p))
        {
            yield return null;
        }
        yield return Fixed;
    }

    private static float ZdoSpeed(Player p)
    {
        var zdo = p.m_nview != null ? p.m_nview.GetZDO() : null;
        return zdo != null ? zdo.GetFloat(ZDOVars.s_animationSpeed, 1f) : float.NaN;
    }

    // One press of the attack button (vanilla StartAttack) with the target just in front, once idle and out of the bash
    // cooldown. Me watch frame by frame until the swing ends: attack state, first Hit event, clip names and speeds at
    // 0.2 s, one screenshot at 0.25 s, target's stagger gained at the hit (drain since then put back), health lost,
    // stamina spent, speed after the swing.
    private static IEnumerator Press(Player p, Character target, Swing s, string test, string shot, bool resetBar = true)
    {
        yield return WaitIdle(p);
        yield return WaitCooldown(p);
        FaceClearLane(p, Reach(p, target), LaneFan, test, target);
        PlaceInFront(p, target);
        if (resetBar)
        {
            target.m_staggerDamage = 0f;
        }
        target.SetHealth(target.GetMaxHealth());
        yield return Fixed;
        var drain = Drain(target);
        s.Drain = drain;
        var bar0 = target.m_staggerDamage;
        var hp0 = target.GetHealth();
        var events0 = BashWatch.DebugHitEvents;
        var skipped0 = BashWatch.DebugSkippedEvents;
        s.Skill = p.GetSkillFactor(Skills.SkillType.Blocking);
        s.Mode = BashAttack.Mode;
        var applied = TowerSync.Applied;
        s.Factor = applied != null ? applied.BashAnimationSpeed : 1f;
        var stamina0 = p.GetStamina();
        var staminaLow = stamina0;
        var t0 = Time.time;
        s.Started = p.StartAttack(null, false);
        var attack = s.Started ? p.m_currentAttack : null;
        if (attack == null)
        {
            s.ModeAfter = BashAttack.Mode;
            yield break;
        }
        s.Trigger = attack.m_attackAnimation;
        var clipsTaken = false;
        var shotTaken = shot == null;
        // Slow swing: kick at 0.3 is about 5 s.
        var watch = 3.5f / Mathf.Min(1f, s.Factor);
        while (Time.time - t0 < watch)
        {
            yield return null;
            var t = Time.time - t0;
            staminaLow = Mathf.Min(staminaLow, p.GetStamina());
            if (s.InAttackAt < 0f && p.InAttack())
            {
                s.InAttackAt = t;
            }
            if (!clipsTaken && t >= 0.2f)
            {
                clipsTaken = true;
                s.Clips = ClipNames(p);
                s.RawSpeed = BashSpeed.DebugRaw;
                s.AnimSpeed = p.m_animator != null ? p.m_animator.speed : float.NaN;
                s.ZdoSpeed = ZdoSpeed(p);
                s.StartScaled = BashSpeed.DebugStartScaled;
            }
            if (!shotTaken && t >= 0.25f)
            {
                shotTaken = true;
                SelfTest.Screenshot(test, shot);
            }
            if (s.HitAt < 0f && BashWatch.DebugHitEvents > events0)
            {
                var hitTime = BashWatch.DebugFirstHitTime;
                s.HitAt = hitTime - t0;
                var now = target.m_staggerDamage + drain * (Time.time - hitTime);
                var before = Mathf.Max(0f, bar0 - drain * (hitTime - t0));
                s.Gained = now - before;
                s.Staggered = now >= target.GetStaggerTreshold() - 0.05f;
                s.Lost = hp0 - target.GetHealth();
            }
            if (!ReferenceEquals(p.m_currentAttack, attack) || attack.IsDone())
            {
                s.EndAt = t;
                break;
            }
        }
        // Watchdog verdict run in FixedUpdate, after swing end; vanilla set speed 1 out of the attack state there too.
        yield return Fixed;
        yield return Fixed;
        s.SpeedAfter = p.m_animator != null ? p.m_animator.speed : float.NaN;
        s.StaminaUsed = stamina0 - Mathf.Min(staminaLow, p.GetStamina());
        s.Events = BashWatch.DebugHitEvents - events0;
        s.Skipped = BashWatch.DebugSkippedEvents - skipped0;
        s.ModeAfter = BashAttack.Mode;
        if (s.Clips.Length == 0)
        {
            s.Clips = "(swing over before 0.2 s)";
        }
        if (s.Hit && !s.Landed)
        {
            var to = target.transform.position - p.transform.position;
            to.y = 0f;
            s.Blocker = LaneBlocker(p, Flat(to), to.magnitude + Radius(target), target, null) ?? "nothing";
        }
    }

    private static string Describe(string label, Swing s) =>
        $"{label}: option {s.Mode} ('{s.Trigger}'), started {s.Started}, attack state at {T(s.InAttackAt)}, "
        + $"hit at {T(s.HitAt)}, swing over at {T(s.EndAt)}, Hit events {s.Events} (skipped {s.Skipped}), "
        + $"clips 0.2 s after the press [{s.Clips}], {SpeedText(s)}; stamina -{F(s.StaminaUsed)}; option after {s.ModeAfter}"
        + (!s.Hit ? ", no hit"
            : s.Landed ? $", stagger +{F(s.Gained)}{(s.Staggered ? " (staggered)" : "")}, health -{F(s.Lost)}"
            : $", Hit event but the target took nothing (between the player and it after the swing: {s.Blocker})");

    private static string SpeedText(Swing s) =>
        Mathf.Approximately(s.Factor, 1f)
            ? $"speed x1 (not scaled): animator {F(s.AnimSpeed)}, ZDO {F(s.ZdoSpeed)}, after the swing {F(s.SpeedAfter)}"
            : $"speed x{F(s.Factor)}: animator {F(s.AnimSpeed)} = clip's own {F(s.RawSpeed)} scaled "
              + $"{(s.StartScaled ? "from the start (no Speed event yet)" : "by its Speed event")}, ZDO {F(s.ZdoSpeed)}, "
              + $"after the swing {F(s.SpeedAfter)}";

    // Design 2.7 swing speed: 0.2 s after the press the animator play at the clip's own speed x BashAnimationSpeed,
    // the ZDO (what other games apply to their copy) say the same, and after the swing vanilla speed 1 is back.
    private static void CheckSpeed(Checks c, string what, Swing s)
    {
        if (!s.Started || float.IsNaN(s.AnimSpeed))
        {
            return;
        }
        if (!Mathf.Approximately(s.Factor, 1f)) // at 1 me track nothing: the clip's own speed is the animator's
        {
            c.Check(Near(s.AnimSpeed, s.RawSpeed * s.Factor, 0.02f),
                $"{what}: animator speed {F(s.AnimSpeed)} = the clip's own {F(s.RawSpeed)} x {F(s.Factor)}");
        }
        c.Check(Near(s.ZdoSpeed, s.AnimSpeed, 0.02f),
            $"{what}: other players' games get the same speed (ZDO {F(s.ZdoSpeed)}, animator {F(s.AnimSpeed)})");
        c.Check(Near(s.SpeedAfter, 1f, 0.01f), $"{what}: after the swing the animator speed is 1 again ({F(s.SpeedAfter)})");
    }

    // Design 2.6: stagger = bash blunt x BashStagger x NG+ factor x random skill factor (lerp(0.4, 1, skill) +-0.15),
    // before the target's armor (decoupled); damage = bash blunt x skill factor x target's blunt modifier.
    private static void CheckHit(Checks c, string what, Swing s, float blunt, float stagger, float ng, float bluntTaken)
    {
        var mean = Mathf.Lerp(0.4f, 1f, s.Skill);
        var lo = Mathf.Clamp01(mean - 0.15f);
        var hi = Mathf.Clamp01(mean + 0.15f);
        var min = blunt * stagger * ng * lo;
        var max = blunt * stagger * ng * hi;
        var slack = 1f + s.Drain * 0.05f; // bar read a frame after the hit, drain put back from frame times
        c.Check(s.Gained >= min - slack && s.Gained <= max + slack, $"{what}: stagger +{F(s.Gained)}, expected {F(min)} to {F(max)}");
        if (bluntTaken >= 0f)
        {
            var most = blunt * hi * bluntTaken;
            c.Check(s.Lost <= most + 0.2f, $"{what}: health -{F(s.Lost)}, at most {F(most)} expected");
        }
    }

    // Me take item out of inventory, drop it 1 m ahead, pick it up with auto-equip on (like walk over it).
    private static IEnumerator DropAndPickup(Rig rig, ItemDrop.ItemData item, Box<ItemDrop.ItemData> result)
    {
        result.Value = null;
        var p = rig.P;
        if (p.IsItemEquiped(item))
        {
            p.UnequipItem(item, false);
        }
        rig.Inv.RemoveItem(item);
        var drop = ItemDrop.DropItem(item, 1, p.transform.position + Flat(p.transform.forward) + Vector3.up * 0.5f, Quaternion.identity);
        rig.Track(drop.gameObject);
        var data = drop.m_itemData;
        rig.Track(data);
        yield return null;
        if (p.Pickup(drop.gameObject, true, false))
        {
            result.Value = data;
        }
    }

    // ---------- tower.data ----------

    private static IEnumerator RunData()
    {
        var c = new Checks(DataName);
        var rig = new Rig(DataName);
        try
        {
            var p = rig.P;
            var rules = TowerRules.Defaults();
            yield return UseRules(rules);
            c.Check(TowerSync.AppliedKey == rules.Key, "default rules applied");
            var db = ObjectDB.instance;

            // Names mod and tests lean on.
            foreach (var name in new[] { "Troll", "Draugr", "Greydwarf", "Greydwarf_Shaman", "Skeleton" })
            {
                c.Check(ZNetScene.instance.GetPrefab(name) != null, $"creature prefab {name} exists");
            }
            foreach (var name in new[] { "SwordIron", "Torch", "ShieldBanded", "ShieldIronSquare", "FW_ShieldBlackmetalTower", "SP_ShieldBlackmetalTower", "skeleton_sword" })
            {
                c.Check(db.GetItemPrefab(name) != null, $"item prefab {name} exists");
            }
            var zanim = p.m_zanim;
            foreach (var trigger in new[] { BashAttack.OtherArmTrigger, BashAttack.ShieldArmTrigger, BashAttack.KickTrigger })
            {
                c.Check(zanim.HasParameter(trigger, AnimatorControllerParameterType.Trigger), $"player animator has the trigger {trigger}");
            }
            var examples = new[] { "throw_bomb", "spear_poke", "mace_secondary", "emote_wave", "dualaxes3", "stagger" };
            SelfTest.Note(DataName, "animator triggers used by tests and README examples: "
                                    + string.Join(", ", examples.Select(t => $"{t} {(zanim.HasParameter(t, AnimatorControllerParameterType.Trigger) ? "yes" : "NO")}").ToArray()));
            var unarmed = p.m_unarmedWeapon;
            var unarmedShared = unarmed != null ? unarmed.m_itemData.m_shared : null;
            c.Check(unarmed != null && unarmed.name == "PlayerUnarmed" && unarmedShared.m_attack.m_attackAnimation == "unarmed_attack"
                    && unarmedShared.m_attack.m_attackChainLevels == 2 && unarmedShared.m_secondaryAttack.m_attackAnimation == "unarmed_kick",
                "bash template: the player's unarmed weapon is PlayerUnarmed, primary unarmed_attack with 2 chain levels, secondary unarmed_kick");

            // Every default tower: 2.0.1 values over its vanilla snapshot.
            var armor = new List<string>();
            var effects = new List<string>();
            c.Check(TowerCatalog.Count == rules.Entries.Count, $"{TowerCatalog.Count} towers in the catalog, {rules.Entries.Count} in the default list");
            foreach (var e in rules.Entries)
            {
                var go = db.GetItemPrefab(e.Prefab);
                var s = SharedOf(e.Prefab);
                var v = TowerCatalog.SnapshotOfPrefab(go);
                if (s == null || v == null || TowerCatalog.TowerOfPrefab(go) == null)
                {
                    c.Check(false, $"{e.Prefab} is a tower of the default list (prefab {(s != null ? "found" : "MISSING")}, snapshot {(v != null ? "taken" : "none")})");
                    continue;
                }
                c.Check(v.ItemType == ItemType.Shield && v.AnimationState == ItemDrop.ItemData.AnimationState.Shield
                        && v.SkillType == Skills.SkillType.Blocking && v.TimedBlockBonus <= 1f,
                    $"{e.Prefab}: snapshot is the vanilla shield (type {v.ItemType}, parry bonus {F(v.TimedBlockBonus)})");
                c.Check(s.m_itemType == ItemType.TwoHandedWeaponLeft && s.m_attachOverride == ItemType.Shield,
                    $"{e.Prefab}: two-handed (left), shield look (type {s.m_itemType}, attach {s.m_attachOverride})");
                c.Check(s.m_timedBlockBonus <= 1f && s.m_perfectBlockAdrenaline == 0f,
                    $"{e.Prefab}: no parry (bonus {F(s.m_timedBlockBonus)}, parry adrenaline {F(s.m_perfectBlockAdrenaline)})");
                c.Check(Near(s.m_blockPower, v.BlockPower * 2.5f) && Near(s.m_blockPowerPerLevel, v.BlockPowerPerLevel * 2.5f),
                    $"{e.Prefab}: block armor x2.5 ({F(v.BlockPower)} -> {F(s.m_blockPower)}, per level {F(v.BlockPowerPerLevel)} -> {F(s.m_blockPowerPerLevel)})");
                c.Check(Near(s.m_deflectionForce, v.DeflectionForce) && Near(s.m_deflectionForcePerLevel, v.DeflectionForcePerLevel),
                    $"{e.Prefab}: block force 100% ({F(s.m_deflectionForce)})");
                c.Check(Near(s.m_movementModifier, -0.30f), $"{e.Prefab}: movement -30% (is {F(s.m_movementModifier)})");
                c.Check(Near(s.m_damages.m_blunt, e.BashDamage) && Near(s.m_damages.GetTotalDamage(), e.BashDamage)
                        && Near(s.m_damagesPerLevel.GetTotalDamage(), 0f),
                    $"{e.Prefab}: bash damage {F(e.BashDamage)} blunt only, no growth per level (blunt {F(s.m_damages.m_blunt)}, total {F(s.m_damages.GetTotalDamage())})");
                c.Check(Near(s.m_attackForce, 40f) && s.m_blockable && s.m_dodgeable && Near(s.m_backstabBonus, 1f) && !s.m_buildBlockCharges,
                    $"{e.Prefab}: knockback 40, blockable, dodgeable, backstab x1, no block charges");
                c.Check(ItemKinds.Classify(s) == ItemKind.Shield, $"{e.Prefab}: ItemKinds still say Shield");
                armor.Add($"{e.Prefab} {F(v.BlockPower)}");
                effects.Add($"{e.Prefab} ({Fx(s.m_hitEffect, s.m_blockEffect, s.m_startEffect, s.m_triggerEffect)})");
            }
            SelfTest.Note(DataName, "vanilla block armor at quality 1 (snapshots): " + string.Join(", ", armor.ToArray()));
            SelfTest.Note(DataName, "effect lists of the towers: " + string.Join("; ", effects.ToArray()));
            if (unarmedShared != null)
            {
                var ua = unarmedShared.m_attack;
                SelfTest.Note(DataName, $"effect lists of PlayerUnarmed: item ({Fx(unarmedShared.m_hitEffect, unarmedShared.m_blockEffect, unarmedShared.m_startEffect, unarmedShared.m_triggerEffect)}), "
                                        + $"its attack (hit {Count(ua.m_hitEffect)}, start {Count(ua.m_startEffect)}, trigger {Count(ua.m_triggerEffect)})");
            }

            // Left alone: creature copies, a round shield, the square shield.
            foreach (var name in new[] { "FW_ShieldBlackmetalTower", "SP_ShieldBlackmetalTower", "ShieldBanded", "ShieldIronSquare" })
            {
                var s = SharedOf(name);
                c.Check(s != null && s.m_itemType == ItemType.Shield && TowerCatalog.TowerOfPrefab(db.GetItemPrefab(name)) == null,
                    $"{name} stays a vanilla shield (type {(s != null ? s.m_itemType.ToString() : "missing")})");
            }
            foreach (var name in new[] { "FW_ShieldBlackmetalTower", "SP_ShieldBlackmetalTower" })
            {
                c.Check(TowerCatalog.SnapshotOfPrefab(db.GetItemPrefab(name)) == null, $"{name} was never written (creature copy)");
            }

            // Craft-every-weapon list rebuilt now (towers are weapons now): no tower in it. The rebuild read the
            // achievements object; if it throw, me put the old cached list back and check that one.
            List<ItemDrop> weapons = null;
            var cached = db.m_craftableWeapons;
            try
            {
                db.m_craftableWeapons = null;
                weapons = db.GetAllCraftableWeapons();
            }
            catch (Exception ex)
            {
                db.m_craftableWeapons = cached;
                SelfTest.Note(DataName, $"rebuilding the craftable weapons list threw {ex.GetType().Name}: {ex.Message}; the cached list is checked");
                weapons = cached != null && cached.Count > 0 ? db.GetAllCraftableWeapons() : null;
            }
            c.Check(weapons != null && weapons.Count > 0 && weapons.All(w => w == null || TowerCatalog.SnapshotOfPrefab(w.gameObject) == null),
                $"craft-every-weapon list has no tower ({(weapons != null ? weapons.Count : -1)} weapons)");

            // Copies: tooltip, damage without the NG+ bonus, bash attack, stands.
            var gold = rig.Give("ShieldGoldTower");
            var tip = gold != null ? gold.GetTooltip() : "";
            c.Check(tip.Contains("$item_twohanded") && tip.Contains("Cannot parry") && tip.Contains("Bash stagger:") && tip.Contains("Bash cooldown:")
                    && tip.Contains("Braced:") && !tip.Contains("$item_parryadrenaline") && !tip.Contains("$item_parrybonus"),
                $"Nord Greatshield tooltip: Two-handed, Cannot parry, bash stagger, bash cooldown and Braced lines, no parry lines: '{tip.Replace("\n", " | ")}'");
            c.Check(tip.Contains("$item_blockarmor: <color=orange>395</color>") && tip.Contains("$inventory_blunt: <color=orange>32</color>"),
                "Nord Greatshield tooltip: block armor 395, bash blunt 32");
            var iron = rig.Give(IronTower);
            var sword = rig.Give("SwordIron");
            if (iron == null || sword == null)
            {
                c.Check(false, "could not add ShieldIronTower and SwordIron");
                c.Report();
                yield break;
            }
            var ng = iron.GetDamage(1, 1f);
            c.Check(Near(ng.m_blunt, 12f) && Near(ng.GetTotalDamage(), 12f), $"tower copy GetDamage(1, 1) = its 12 blunt, no world-level bonus (total {F(ng.GetTotalDamage())})");
            var sword0 = sword.GetDamage(1, 0f).GetTotalDamage();
            var sword1 = sword.GetDamage(1, 1f).GetTotalDamage();
            c.Check(sword1 > sword0 + 1f, $"control: a sword gets the world-level bonus ({F(sword0)} -> {F(sword1)})");
            SelfTest.Note(DataName, $"Game NG+ numbers: gear base damage {Game.instance.m_worldLevelGearBaseDamage}, enemy HP multiplier {F(Game.instance.m_worldLevelEnemyHPMultiplier)}, enemy base armor {Game.instance.m_worldLevelEnemyBaseAC}");

            p.EquipItem(iron);
            yield return Fixed;
            var bash = BashAttack.Current;
            c.Check(bash != null && ReferenceEquals(iron.m_shared.m_attack, bash), "equipped tower copy carries the shared bash attack");
            c.Check(ReferenceEquals(p.GetCurrentWeapon(), iron) && ReferenceEquals(p.GetCurrentBlocker(), iron) && iron.HavePrimaryAttack(),
                "the tower is both the current weapon and the blocker, with a primary attack");
            c.Check(TowerCatalog.Held(p) != null, "held-tower verdict says tower");
            if (bash != null)
            {
                c.Check(bash.m_attackAnimation == BashAttack.TriggerOf(BashAttack.Mode, rules) && bash.m_attackChainLevels == 0
                        && bash.m_attackRandomAnimations == 0 && bash.m_attackType == Attack.AttackType.Horizontal,
                    $"bash plays '{bash.m_attackAnimation}' ({BashAttack.Mode}) as is: no chain, no random");
                c.Check(BashAttack.Mode == BashAnimationKind.ShieldPunch && bash.m_attackAnimation == BashAttack.ShieldArmTrigger,
                    $"default animation ShieldPunch ({BashAttack.Mode}, '{bash.m_attackAnimation}')");
                c.Check(Near(bash.m_attackStamina, 20f) && Near(bash.m_staggerMultiplier, 25f) && Near(bash.m_forceMultiplier, 1f),
                    $"bash stamina 20, stagger x25, force x1 ({F(bash.m_attackStamina)}, {F(bash.m_staggerMultiplier)}, {F(bash.m_forceMultiplier)})");
                c.Check(Near(bash.m_attackRange, 1.8f) && Near(bash.m_attackAngle, 60f) && Near(bash.m_attackRayWidth, BashAttack.RayWidth)
                        && bash.m_multiHit && !bash.m_lowerDamagePerHit && !bash.m_hitTerrain,
                    "bash range 1.8 m, arc 60, ray 0.4, full bash on every enemy in the arc, never stop on terrain");
                c.Check(!ReferenceEquals(bash, unarmedShared != null ? unarmedShared.m_attack : null), "bash is a clone, the unarmed attack is untouched");
                SelfTest.Note(DataName, $"bash attack effect lists (from the unarmed attack): hit {Count(bash.m_hitEffect)}, start {Count(bash.m_startEffect)}, trigger {Count(bash.m_triggerEffect)}");
            }
            var ironTip = iron.GetTooltip();
            c.Check(ironTip.Contains("$item_staminause: <color=orange>20</color>") && ironTip.Contains("$item_knockback: <color=orange>40</color>")
                    && ironTip.Contains("$item_blockarmor: <color=orange>130</color>") && ironTip.Contains("Bash stagger: <color=orange>×25</color>")
                    && ironTip.Contains("Bash cooldown: <color=orange>2 s</color>"),
                $"Iron tower tooltip: stamina use 20, knockback 40, block armor 130, bash stagger x25, bash cooldown 2 s: '{ironTip.Replace("\n", " | ")}'");

            // Item stands: vanilla take any item when supported list empty; two-handed tower must be taken
            // wherever Shield-typed one is (no patch, design 2.1).
            var ironSnap = TowerCatalog.SnapshotOf(iron);
            var refused = new List<string>();
            var stands = new List<string>();
            foreach (var go in ZNetScene.instance.m_prefabs)
            {
                var stand = go != null ? go.GetComponentInChildren<ItemStand>(true) : null;
                if (stand == null)
                {
                    continue;
                }
                var keep = iron.m_shared.m_itemType;
                bool asShield;
                bool asTower;
                try
                {
                    iron.m_shared.m_itemType = ironSnap != null ? ironSnap.ItemType : ItemType.Shield;
                    asShield = stand.CanAttach(iron);
                    iron.m_shared.m_itemType = keep;
                    asTower = stand.CanAttach(iron);
                }
                finally
                {
                    iron.m_shared.m_itemType = keep;
                }
                stands.Add($"{go.name} (supported items {stand.m_supportedItems.Count}, types [{string.Join(",", stand.m_supportedTypes.Select(t => t.ToString()).ToArray())}], unsupported {stand.m_unsupportedItems.Count}, tower {(asTower ? "taken" : "refused")})");
                if (asShield && !asTower)
                {
                    refused.Add(go.name);
                }
            }
            SelfTest.Note(DataName, $"item stands ({stands.Count}): " + string.Join("; ", stands.ToArray()));
            c.Check(refused.Count == 0, $"no item stand refuses a two-handed tower it took as a shield ({string.Join(", ", refused.ToArray())})");
            var armorStands = new List<string>();
            var slotMismatch = new List<string>();
            foreach (var go in ZNetScene.instance.m_prefabs)
            {
                var armorStand = go != null ? go.GetComponentInChildren<ArmorStand>(true) : null;
                if (armorStand == null)
                {
                    continue;
                }
                var taken = 0;
                for (var i = 0; i < armorStand.m_slots.Count; i++)
                {
                    var slot = armorStand.m_slots[i];
                    var vanilla = slot.m_supportedTypes.Count == 0 || slot.m_supportedTypes.Contains(ItemType.Shield);
                    var tower = armorStand.CanAttach(slot, iron);
                    if (tower)
                    {
                        taken++;
                    }
                    if (tower != vanilla)
                    {
                        slotMismatch.Add($"{go.name} slot {i}");
                    }
                }
                armorStands.Add($"{go.name} ({armorStand.m_slots.Count} slots, {taken} take the tower)");
            }
            SelfTest.Note(DataName, $"armor stands: {string.Join("; ", armorStands.ToArray())}");
            c.Check(slotMismatch.Count == 0, $"armor stand slots take the tower exactly where they take a shield ({string.Join(", ", slotMismatch.ToArray())})");

            // Unverified values of design 9.
            var square = db.m_recipes.Where(r => r != null && r.m_item != null && r.m_item.name == "ShieldIronSquare").ToList();
            SelfTest.Note(DataName, square.Count == 0
                ? "ShieldIronSquare has no recipe (players cannot craft it)"
                : $"ShieldIronSquare has {square.Count} recipe(s), enabled {string.Join(",", square.Select(r => r.m_enabled.ToString()).ToArray())}");
            var slow = new List<string>();
            foreach (var go in db.m_items)
            {
                var drop = go != null ? go.GetComponent<ItemDrop>() : null;
                var s = drop != null ? drop.m_itemData.m_shared : null;
                if (s == null || s.m_movementModifier == 0f || s.m_icons == null || s.m_icons.Length == 0)
                {
                    continue;
                }
                if (s.m_itemType == ItemType.Chest || s.m_itemType == ItemType.Legs || s.m_itemType == ItemType.Helmet
                    || s.m_itemType == ItemType.Shoulder)
                {
                    slow.Add($"{go.name} {F(s.m_movementModifier)}");
                }
            }
            slow.Sort(StringComparer.Ordinal);
            SelfTest.Note(DataName, $"armor movement modifiers ({slow.Count}): {string.Join(", ", slow.ToArray())}");
            SelfTest.Note(DataName, $"training dummy prefab piece_TrainingDummy {(ZNetScene.instance.GetPrefab("piece_TrainingDummy") != null ? "exists" : "NOT found")}");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- tower.rules ----------

    private static IEnumerator RunRules()
    {
        var c = new Checks(RulesName);
        var rig = new Rig(RulesName);
        try
        {
            // Wire: every field survive, out-of-range value clamped, unknown layout and short package refused.
            var odd = TowerRules.Defaults().With(r =>
            {
                r.Towers = "ShieldIronTower:12, ShieldBanded";
                r.BlockArmorMultiplier = 3.5f;
                r.BlockForcePercent = 55;
                r.CarrySlowPercent = 12;
                r.BraceSlowPercent = 45;
                r.BraceStaggerResistPercent = 60;
                r.BraceKnockbackResistPercent = 30;
                r.BlockUnblockableAttacks = false;
                r.BashAnimation = BashAnimationKind.Custom;
                r.BashCustomTrigger = "throw_bomb";
                r.BashAnimationSpeed = 0.85f;
                r.BashStamina = 7.5f;
                r.BashCooldown = 3.5f;
                r.BashStagger = 33f;
                r.BashStaggerLock = 2f;
                r.BashKnockback = 90f;
                r.BashRange = 2.5f;
                r.BashAngle = 120f;
            });
            var pkg = new ZPackage();
            odd.Write(pkg);
            pkg.SetPos(0);
            c.Check(TowerRules.TryRead(pkg, out var back, out var clamped) && !clamped && back.Key == odd.Key && back.Describe() == odd.Describe(),
                "rules survive the wire unchanged (every field)");

            var wild = TowerRules.Defaults().With(null);
            wild.BlockArmorMultiplier = 50f;       // written raw after the seal: what a broken server could send
            wild.BlockForcePercent = -5;
            wild.CarrySlowPercent = 90;
            wild.BraceSlowPercent = 200;
            wild.BraceStaggerResistPercent = 150;
            wild.BraceKnockbackResistPercent = -1;
            wild.BashAnimation = (BashAnimationKind)42;
            wild.BashAnimationSpeed = 0f;
            wild.BashStamina = 0f;
            wild.BashCooldown = 60f;
            wild.BashStagger = 150f;
            wild.BashStaggerLock = float.NaN;
            wild.BashKnockback = 1000f;
            wild.BashRange = 0.1f;
            wild.BashAngle = 720f;
            pkg = new ZPackage();
            wild.Write(pkg);
            pkg.SetPos(0);
            c.Check(TowerRules.TryRead(pkg, out back, out clamped) && clamped
                    && back.BlockArmorMultiplier == TowerRules.MaxBlockArmorMultiplier && back.BlockForcePercent == 0
                    && back.CarrySlowPercent == TowerRules.MaxCarrySlowPercent && back.BraceSlowPercent == TowerRules.MaxBraceSlowPercent
                    && back.BraceStaggerResistPercent == 100 && back.BraceKnockbackResistPercent == 0
                    && back.BashAnimation == TowerRules.DefaultBashAnimation && back.BashAnimationSpeed == TowerRules.MinBashAnimationSpeed
                    && back.BashStamina == TowerRules.MinBashStamina && back.BashCooldown == TowerRules.MaxBashCooldown
                    && back.BashStagger == TowerRules.MaxBashStagger && back.BashStaggerLock == TowerRules.DefaultBashStaggerLock
                    && back.BashKnockback == TowerRules.MaxBashKnockback && back.BashRange == TowerRules.MinBashRange
                    && back.BashAngle == TowerRules.MaxBashAngle,
                "out-of-range rules from the wire are clamped (stagger never 100 or more, unknown animation = ShieldPunch, NaN = default)");
            c.Check(TowerRules.MaxBashStagger < 100f, "the bash stagger multiplier can never reach 100 (would stagger before the block)");
            pkg = new ZPackage();
            pkg.Write(TowerRules.Layout + 1);
            pkg.SetPos(0);
            c.Check(!TowerRules.TryRead(pkg, out _, out _), "unknown rules layout refused");
            pkg = new ZPackage();
            pkg.Write(1);
            pkg.Write("ShieldIronTower:12");
            pkg.SetPos(0);
            c.Check(TowerRules.Layout == 2 && !TowerRules.TryRead(pkg, out _, out _), "rules of layout 1 (0.1.0 test builds before the bash speed and cooldown) refused");
            pkg = new ZPackage();
            pkg.Write(TowerRules.Layout);
            pkg.Write("ShieldIronTower");
            pkg.SetPos(0);
            c.Check(!TowerRules.TryRead(pkg, out _, out _), "a cut rules package is refused");
            pkg = new ZPackage();
            odd.Write(pkg);
            pkg.SetPos(0);
            c.Check(ServerRules.Receive(pkg) == false, "single player never takes rules from a peer");

            // Towers list parsing and the rules key.
            var problems = new List<string>();
            var entries = TowerRules.ParseTowers("ShieldWoodTower:6, ShieldBoneTower;X:abc\n:5, ShieldWoodTower:9, Y:500, Z:-3", problems);
            var parsed = string.Join(", ", entries.Select(e => $"{e.Prefab}:{F(e.BashDamage)}").ToArray());
            c.Check(parsed == "ShieldWoodTower:6, ShieldBoneTower:10, X:10, Y:200, Z:0" && problems.Count == 5,
                $"Towers parsing: {parsed}; {problems.Count} problems ({string.Join(" / ", problems.ToArray())})");
            var spaced = TowerRules.Defaults().With(r => r.Towers = "ShieldWoodTower:6,ShieldBoneTower:8, ShieldIronTower:12;ShieldSerpentscale:15\nShieldBlackmetalTower:20 ,ShieldFlametalTower:28,  ShieldGoldTower:32");
            c.Check(spaced.Key == TowerRules.Defaults().Key && spaced.Describe().StartsWith("default tower list", StringComparison.Ordinal),
                "spaces and separators do not change the rules (same key: nothing to apply again)");
            c.Check(TowerRules.Defaults().With(r => r.BashStagger = 26f).Key != TowerRules.Defaults().Key
                    && TowerRules.Defaults().With(r => r.BashCooldown = 1f).Key != TowerRules.Defaults().Key
                    && TowerRules.Defaults().With(r => r.BashAnimationSpeed = 0.7f).Key != TowerRules.Defaults().Key
                    && TowerRules.Defaults().With(r => r.BashStaggerLock = 5f).Key != TowerRules.Defaults().Key,
                "a changed value changes the key (bash stagger, cooldown, animation speed, stagger lock)");

            // Join check (framework's PeerCompatible is the compatible input; its own tests N07, N08).
            c.Check(PlayerCheck.Decide(true, true, true, false, true, false) == JoinVerdict.Compatible, "compatible player allowed");
            c.Check(PlayerCheck.Decide(true, true, true, false, false, false) == JoinVerdict.Refuse,
                "not compatible (no mod, other network version or turned off) refused with the setting off");
            c.Check(PlayerCheck.Decide(true, true, true, false, false, true) == JoinVerdict.Allowed, "not compatible allowed with AllowPlayersWithoutMod on");
            c.Check(PlayerCheck.Decide(false, true, true, false, false, false) == JoinVerdict.Skip
                    && PlayerCheck.Decide(true, false, true, false, false, false) == JoinVerdict.Skip
                    && PlayerCheck.Decide(true, true, false, false, false, false) == JoinVerdict.Skip
                    && PlayerCheck.Decide(true, true, true, true, false, false) == JoinVerdict.Skip,
                "not the server, gone, not ready or already being kicked: skipped");

            // Bash animation: triggers, fallback chain, Custom trigger set.
            c.Check(BashAttack.TriggerOf(BashAnimationKind.ShieldPunch, odd) == "unarmed_attack1"
                    && BashAttack.TriggerOf(BashAnimationKind.OtherPunch, odd) == "unarmed_attack0" && BashAttack.TriggerOf(BashAnimationKind.Kick, odd) == "unarmed_kick"
                    && BashAttack.TriggerOf(BashAnimationKind.Custom, odd) == "throw_bomb",
                "bash triggers: ShieldPunch unarmed_attack1, OtherPunch unarmed_attack0, Kick unarmed_kick, Custom its setting");
            c.Check(BashAttack.Next(BashAnimationKind.Custom) == BashAnimationKind.ShieldPunch
                    && BashAttack.Next(BashAnimationKind.ShieldPunch) == BashAnimationKind.OtherPunch && BashAttack.Next(BashAnimationKind.Kick) == BashAnimationKind.OtherPunch
                    && BashAttack.Next(BashAnimationKind.OtherPunch) == null,
                "fallback chain: Custom -> ShieldPunch -> OtherPunch; Kick -> OtherPunch; OtherPunch last");

            // Animation setting: the config offer exactly the four names (default first); the setting's own clamp (what
            // a file value go through) read a name in any case as the list spell it, and a file that still say ShieldUp
            // (0.1.0 test builds) as ShieldPunch, without a warning (clamp called here, config never written). Unknown
            // names: checked on the pure lookup (the clamp would log its Warning).
            var names = Plugin.BashAnimation.Description.AcceptableValues as BepInEx.Configuration.AcceptableValueList<string>;
            c.Check(names != null && string.Join(",", names.AcceptableValues) == "ShieldPunch,OtherPunch,Kick,Custom"
                    && (string)Plugin.BashAnimation.DefaultValue == "ShieldPunch",
                $"BashAnimation offers ShieldPunch, OtherPunch, Kick, Custom, default ShieldPunch ({(names != null ? string.Join(", ", names.AcceptableValues) : "no list")})");
            c.Check(names != null && !names.IsValid("ShieldUp") && (string)names.Clamp("ShieldUp") == "ShieldPunch"
                    && TowerRules.ParseAnimation("ShieldUp") == BashAnimationKind.ShieldPunch,
                "an old config value ShieldUp is read as ShieldPunch");
            var kick = names != null ? names.Clamp("kick") as string : null;
            var custom = names != null ? names.Clamp(" CUSTOM ") as string : null;
            c.Check(kick == "Kick" && custom == "Custom" && names.IsValid("otherpunch")
                    && TowerRules.ParseAnimation("kick") == BashAnimationKind.Kick && TowerRules.ParseAnimation("Custom") == BashAnimationKind.Custom
                    && TowerRules.AnimationName("mc_no_such_animation") == null && TowerRules.AnimationName("") == null,
                $"the BashAnimation setting reads names in any case ('kick' -> '{kick}', ' CUSTOM ' -> '{custom}'); unknown names are not names");
            var set = BashAttack.AttackTriggers();
            c.Check(set.Contains("unarmed_attack0") && set.Contains("unarmed_attack1") && set.Contains("unarmed_kick") && set.Contains("throw_bomb")
                    && set.Contains("spear_poke") && set.Contains("mace_secondary") && set.Contains("dualaxes3"),
                "player attack triggers include the unarmed ones and the README examples");
            c.Check(!set.Contains("emote_wave") && !set.Contains("stagger") && !set.Contains("dodge") && !set.Contains("unarmed_attack"),
                "emotes, stagger, dodge and chain base names are not attack triggers");
            var held = BashAttack.HeldTriggers();
            c.Check(!set.Contains("staff_rapidfire") && !set.Contains("bow_fire") && !set.Contains("crossbow_fire")
                    && held.Contains("staff_rapidfire") && held.Contains("bow_fire") && held.Contains("crossbow_fire"),
                "held, aimed and reloaded attacks (staff_rapidfire, bow_fire, crossbow_fire) are refused for Custom (a looping clip would never end)");
            SelfTest.Note(RulesName, $"{set.Count} player attack triggers allowed for Custom; refused as held, aimed, reloaded or attached: {string.Join(", ", held.OrderBy(n => n, StringComparer.Ordinal).ToArray())}");

            // Bash hits are told apart by a stagger multiplier above 1: every allowed BashStagger must be one.
            var lowest = new HitData { m_hitType = HitData.HitType.PlayerHit, m_skill = Skills.SkillType.Blocking, m_staggerMultiplier = TowerRules.MinBashStagger };
            c.Check(TowerRules.MinBashStagger > 1f && BashStagger.IsBashHit(lowest),
                $"the lowest BashStagger ({F(TowerRules.MinBashStagger)}) is still recognised as a bash on the creature's owner");

            // Made blockable only for hostile attackers. Player attacker: PvP attack yes, spell for every player (Aoe
            // m_ignorePVP, e.g. the Staff of Protection's bubble) no. Creature branch: tower.block.
            var pvp = new HitData { m_ignorePVP = false };
            var spell = new HitData { m_ignorePVP = true };
            c.Check(Brace.IsHostile(rig.P, rig.P, pvp) && !Brace.IsHostile(rig.P, rig.P, spell) && !Brace.IsHostile(null, rig.P, pvp),
                "a player's hit is made blockable only as a PvP attack, never a spell that reaches every player (Staff of Protection); no attacker: never");

            // Pure bits.
            c.Check(TowerSnapshot.IsShieldData(ItemType.Shield, ItemDrop.ItemData.AnimationState.Shield, Skills.SkillType.Blocking)
                    && !TowerSnapshot.IsShieldData(ItemType.TwoHandedWeaponLeft, ItemDrop.ItemData.AnimationState.Shield, Skills.SkillType.Blocking)
                    && !TowerSnapshot.IsShieldData(ItemType.OneHandedWeapon, ItemDrop.ItemData.AnimationState.OneHanded, Skills.SkillType.Swords),
                "only shield data (type Shield, shield pose, Blocking) can be a tower");
            var line = TowerTooltip.BracedLine(TowerRules.Defaults());
            c.Check(line == "Braced: <color=orange>movement -30%, blocks attacks from the front that normally cannot be blocked</color>; "
                    + "while you have stamina for another block: <color=orange>stagger -80%, no knockback from the front</color>",
                $"Braced tooltip line: '{line}'");
            var heldOnly = TowerTooltip.BracedLine(TowerRules.Defaults().With(r =>
            {
                r.BraceSlowPercent = 0;
                r.BlockUnblockableAttacks = false;
                r.BraceKnockbackResistPercent = 50;
            }));
            c.Check(heldOnly == "Braced, while you have stamina for another block: <color=orange>stagger -80%, knockback from the front -50%</color>",
                $"Braced line with only the stamina parts: '{heldOnly}'");
            c.Check(TowerTooltip.BracedLine(TowerRules.Defaults().With(r =>
                    {
                        r.BraceSlowPercent = 0;
                        r.BraceStaggerResistPercent = 0;
                        r.BraceKnockbackResistPercent = 0;
                        r.BlockUnblockableAttacks = false;
                    })) == ""
                    && TowerTooltip.BracedLine(TowerRules.Defaults().With(r =>
                    {
                        r.BraceStaggerResistPercent = 0;
                        r.BraceKnockbackResistPercent = 0;
                        r.BlockUnblockableAttacks = false;
                    })) == "Braced: <color=orange>movement -30%</color>",
                "Braced line leaves out parts at 0, and the stamina condition when only the slow is left");

            // Generation: go up on different server rules, not on same ones; on own change when not client.
            var net = ZNet.instance;
            var g0 = ServerRules.Generation;
            ServerRules.Accept(TowerRules.Defaults().With(r => r.BashStagger = 20f), false, net);
            var g1 = ServerRules.Generation;
            ServerRules.Accept(TowerRules.Defaults().With(r => r.BashStagger = 20f), false, net);
            var g2 = ServerRules.Generation;
            ServerRules.Accept(TowerRules.Defaults().With(r => r.BashStagger = 30f), false, net);
            var g3 = ServerRules.Generation;
            c.Check(g1 != g0 && g2 == g1 && g3 != g2, $"generation: new rules raise it, the same rules again do not ({g0} -> {g1} -> {g2} -> {g3})");
            c.Check(!ServerRules.UsingServer && ReferenceEquals(TowerRules.InForce, ServerRules.Own), "single player keeps its own rules");
            ServerRules.Forget();
            var g4 = ServerRules.Generation;
            ServerRules.OwnChanged();
            c.Check(ServerRules.Generation != g4, "own settings changed in single player (not a client): generation raised");
            SelfTest.Note(RulesName, "a client ignoring its own setting changes while it uses the server's rules is an in-game test (M10, M11)");

            // Rules change reach items about 0.5 s later.
            var basic = TowerRules.Defaults();
            yield return UseRules(basic);
            var tower = rig.Give(IronTower);
            var snap = SnapshotOf(IronTower);
            if (tower == null || snap == null)
            {
                c.Check(false, "could not add a ShieldIronTower (or no snapshot)");
                c.Report();
                yield break;
            }
            var x4 = TowerRules.Defaults().With(r => r.BlockArmorMultiplier = 4f);
            var t0 = Time.unscaledTime;
            ServerRules.TestRules = x4;
            yield return null;
            var early = tower.m_shared.m_blockPower;
            var took = -1f;
            while (Time.unscaledTime - t0 < 3f)
            {
                if (Near(tower.m_shared.m_blockPower, snap.BlockPower * 4f))
                {
                    took = Time.unscaledTime - t0;
                    break;
                }
                yield return null;
            }
            c.Check(Near(early, snap.BlockPower * 2.5f), $"just after the change the item keeps its block armor ({F(early)})");
            c.Check(took >= 0.45f && took <= 1.5f, $"the new block armor reached the inventory copy {T(took)} after the change (about 0.5 s expected)");
            c.Check(Near(SharedOf(IronTower).m_blockPower, snap.BlockPower * 4f), "and the prefab");

            // Towers validation: a sword refused, a missing name skipped, a parrying shield accepted without parry.
            var mixed = TowerRules.Defaults().With(r => r.Towers = "ShieldIronTower:12, SwordIron:5, ShieldBanded:7, MC_NoSuchItem");
            yield return UseRules(mixed);
            var db = ObjectDB.instance;
            var banded = SharedOf("ShieldBanded");
            var bandedSnap = SnapshotOf("ShieldBanded");
            c.Check(TowerSync.AppliedKey == mixed.Key && TowerCatalog.Count == 2 && TowerCatalog.TowerOfPrefab(db.GetItemPrefab("SwordIron")) == null
                    && TowerCatalog.TowerOfPrefab(db.GetItemPrefab("ShieldBanded")) != null,
                $"Towers: SwordIron refused (not a shield), MC_NoSuchItem skipped (MISSING), ShieldBanded accepted ({TowerCatalog.Count} towers; the Warnings are in the log)");
            c.Check(banded != null && bandedSnap != null && banded.m_itemType == ItemType.TwoHandedWeaponLeft && banded.m_timedBlockBonus <= 1f
                    && bandedSnap.TimedBlockBonus > 1f,
                "ShieldBanded as a tower loses its parry");
            c.Check(SharedOf("SwordIron").m_itemType == ItemType.OneHandedWeapon && SharedOf("ShieldWoodTower").m_itemType == ItemType.Shield,
                "SwordIron untouched; towers left out of the list are vanilla again");
            var again = TowerRules.Defaults();
            yield return UseRules(again);
            c.Check(banded != null && bandedSnap != null && banded.m_itemType == ItemType.Shield && Near(banded.m_timedBlockBonus, bandedSnap.TimedBlockBonus),
                "ShieldBanded is a parrying round shield again once it leaves the list");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- tower.hands ----------

    private static IEnumerator RunHands()
    {
        var c = new Checks(HandsName);
        var rig = new Rig(HandsName);
        try
        {
            var p = rig.P;
            var rules = TowerRules.Defaults();
            yield return UseRules(rules);
            c.Check(TowerSync.AppliedKey == rules.Key, "default rules applied");
            var tower = rig.Give(IronTower);
            var sword = rig.Give("SwordIron");
            var torch = rig.Give("Torch");
            var banded = rig.Give("ShieldBanded");
            if (tower == null || sword == null || torch == null || banded == null)
            {
                c.Check(false, "could not add ShieldIronTower, SwordIron, Torch and ShieldBanded");
                c.Report();
                yield break;
            }

            // Equip paths (vanilla EquipItem, tower = TwoHandedWeaponLeft).
            p.EquipItem(sword);
            p.EquipItem(banded);
            c.Check(ReferenceEquals(p.m_rightItem, sword) && ReferenceEquals(p.m_leftItem, banded), $"vanilla: sword and round shield together ({Hands(p)})");
            p.EquipItem(tower);
            c.Check(Only(p, tower, left: true) && !sword.m_equipped && !banded.m_equipped,
                $"tower after sword + round shield: only the tower, in the left hand ({Hands(p)})");
            c.Check(tower.m_shared.m_itemType == ItemType.TwoHandedWeaponLeft && tower.IsTwoHanded() && tower.IsWeapon(),
                "the tower copy is a two-handed (left) weapon");
            p.EquipItem(sword);
            c.Check(Only(p, sword, left: false) && !tower.m_equipped, $"sword with the tower held: only the sword ({Hands(p)})");
            p.EquipItem(tower);
            c.Check(Only(p, tower, left: true) && !sword.m_equipped, $"tower again: only the tower ({Hands(p)})");
            p.EquipItem(torch);
            c.Check(Only(p, torch, left: false) && !tower.m_equipped, $"torch with the tower held: only the torch ({Hands(p)})");
            p.EquipItem(tower);
            p.EquipItem(banded);
            c.Check(Only(p, banded, left: true) && !tower.m_equipped, $"round shield with the tower held: only the round shield ({Hands(p)})");

            // Put away: tower hang in back shield slot (prefab attach override Shield).
            p.EquipItem(tower);
            p.HideHandItems();
            c.Check(ReferenceEquals(p.m_hiddenLeftItem, tower) && p.m_leftItem == null, "hide: the tower goes to the hidden left slot");
            yield return Frames(4);
            var vis = p.m_visEquipment;
            var backItem = vis != null ? vis.m_leftBackItemInstance : null;
            var where = backItem == null ? "no back item" : backItem.transform.parent != null ? backItem.transform.parent.name : "no parent";
            c.Check(backItem != null && vis.m_backShield != null && backItem.transform.IsChildOf(vis.m_backShield),
                $"the hidden tower hangs in the back shield slot, not the two-handed one ({where})");
            SelfTest.Screenshot(HandsName, "tower-on-back");
            yield return null;
            yield return null;
            p.ShowHandItems(false, false);
            c.Check(Only(p, tower, left: true), $"show: the tower back in the left hand ({Hands(p)})");

            // Pickup: vanilla auto-equip weapons into an empty right hand; a tower never auto-equip.
            var picked = new Box<ItemDrop.ItemData>();
            p.EquipItem(banded);
            yield return DropAndPickup(rig, tower, picked);
            c.Check(picked.Value != null && !picked.Value.m_equipped && ReferenceEquals(p.m_leftItem, banded) && banded.m_equipped && p.m_rightItem == null,
                $"tower picked up with a round shield held and the right hand empty: not equipped, the round shield stays ({Hands(p)})");
            if (picked.Value == null)
            {
                c.Report();
                yield break;
            }
            tower = picked.Value;
            p.UnequipItem(banded);
            yield return DropAndPickup(rig, tower, picked);
            c.Check(picked.Value != null && !picked.Value.m_equipped && p.m_leftItem == null && p.m_rightItem == null,
                $"tower picked up with both hands empty: in the inventory, not in the hands ({Hands(p)})");
            if (picked.Value == null)
            {
                c.Report();
                yield break;
            }
            tower = picked.Value;
            p.EquipItem(tower);
            yield return DropAndPickup(rig, sword, picked);
            c.Check(picked.Value != null && !picked.Value.m_equipped && Only(p, tower, left: true),
                $"sword picked up with the tower held: not equipped (vanilla: the left item is two-handed) ({Hands(p)})");
            if (picked.Value == null)
            {
                c.Report();
                yield break;
            }
            sword = picked.Value;

            // Sword hidden next to a tower that is a vanilla shield, then the rules make it a tower again: FixHands
            // drop the hidden sword, so showing hands never drop the tower (design 2.1, decision 15).
            var none = TowerRules.Defaults().With(r => r.Towers = "");
            yield return UseRules(none);
            c.Check(tower.m_shared.m_itemType == ItemType.Shield, "empty tower list: the held copy is a vanilla shield again");
            p.EquipItem(sword);
            c.Check(ReferenceEquals(p.m_rightItem, sword) && ReferenceEquals(p.m_leftItem, tower), $"vanilla: sword and one-handed tower together ({Hands(p)})");
            p.HideHandItems();
            yield return Frames(4);
            c.Check(ReferenceEquals(p.m_hiddenRightItem, sword) && ReferenceEquals(p.m_hiddenLeftItem, tower) && vis != null && vis.m_rightBackItemInstance != null,
                "both put away, the sword on the back");
            yield return UseRules(rules);
            yield return Frames(4);
            c.Check(tower.m_shared.m_itemType == ItemType.TwoHandedWeaponLeft && ReferenceEquals(p.m_hiddenLeftItem, tower)
                    && p.m_hiddenRightItem == null && !sword.m_equipped,
                "rules apply: the tower is two-handed again and the hidden sword left the hidden slot (FixHands)");
            c.Check(vis != null && vis.m_rightBackItemInstance == null, "no sword left drawn on the back");
            p.ShowHandItems(false, false);
            c.Check(Only(p, tower, left: true), $"show: only the tower comes back ({Hands(p)})");

            // Creature that pick up player tower: its own copy stay vanilla shield (decision 25).
            var skeleton = rig.Creature("Skeleton") as Humanoid;
            yield return Frames(3); // Humanoid.Start give its default items
            if (skeleton == null)
            {
                c.Check(false, "could not spawn a Skeleton");
                c.Report();
                yield break;
            }
            var bone = ObjectDB.instance.GetItemPrefab("skeleton_sword");
            var weapon = bone != null ? skeleton.PickupPrefab(bone, 0, false) : null;
            if (weapon != null)
            {
                skeleton.EquipItem(weapon, false);
            }
            var copy = skeleton.PickupPrefab(ObjectDB.instance.GetItemPrefab(IronTower), 0, false);
            if (copy != null && !copy.IsWeapon())
            {
                skeleton.EquipItem(copy, false); // what Humanoid.GiveDefaultItem do
            }
            var copySnap = copy != null ? TowerCatalog.SnapshotOf(copy) : null;
            c.Check(copy != null && copySnap != null && copy.m_shared.m_itemType == ItemType.Shield && !copy.IsWeapon()
                    && Near(copy.m_shared.m_blockPower, copySnap.BlockPower),
                $"Skeleton's tower copy is a vanilla shield (type {(copy != null ? copy.m_shared.m_itemType.ToString() : "none")})");
            c.Check(copy != null && ReferenceEquals(skeleton.m_leftItem, copy), "the Skeleton holds it as a shield");
            c.Check(weapon != null && ReferenceEquals(skeleton.m_rightItem, weapon), $"the Skeleton keeps its weapon ({Name(skeleton.m_rightItem)})");
            c.Check(SharedOf(IronTower).m_itemType == ItemType.TwoHandedWeaponLeft, "the prefab (and players' towers) stay towers");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- tower.slow ----------

    private static IEnumerator RunSlow()
    {
        var c = new Checks(SlowName);
        var rig = new Rig(SlowName);
        ItemDrop.ItemData tower = null;
        var savedModifier = 0f;
        var modifierChanged = false;
        try
        {
            var p = rig.P;
            var rules = TowerRules.Defaults();
            yield return UseRules(rules);
            tower = rig.Give(IronTower);
            if (tower == null)
            {
                c.Check(false, "could not add a ShieldIronTower");
                c.Report();
                yield break;
            }
            var runSkill = p.GetSkillFactor(Skills.SkillType.Run);
            p.UpdateModifiers();
            var jog0 = p.GetJogSpeedFactor();
            var run0 = p.GetRunSpeedFactor();
            var speed0 = SpeedMods(p);

            // Carried.
            p.EquipItem(tower);
            p.UpdateModifiers();
            var jog = p.GetJogSpeedFactor();
            var run = p.GetRunSpeedFactor();
            var mod = p.GetEquipmentMovementModifier();
            var vanillaRun = (1f + runSkill * 0.25f) * (1f + mod * 1.5f);
            var floor = p.m_speed * jog / p.m_runSpeed;
            c.Check(Near(jog - jog0, -0.30f, 0.001f), $"tower in hand: jog factor {F(jog0)} -> {F(jog)} (-0.30)");
            c.Check(Near(run, Mathf.Max(vanillaRun, floor), 0.001f), $"sprint factor {F(run)} = vanilla {F(vanillaRun)} (floor {F(floor)})");
            if (Near(jog0, 1f, 0.0001f))
            {
                c.Check(Near(jog, 0.70f, 0.001f) && (runSkill > 0f || Near(run, 0.55f, 0.001f)),
                    $"no armor: jog x0.70, sprint x0.55 at run skill 0 (jog {F(jog)}, sprint {F(run)}, run skill {F(runSkill * 100f)})");
            }
            else
            {
                SelfTest.Note(SlowName, $"the player wears slowing gear (jog factor {F(jog0)} without the tower): the 0.70 / 0.55 case was not checked");
            }
            c.Check(run * p.m_runSpeed >= jog * p.m_speed - 0.001f, $"sprint {F(run * p.m_runSpeed)} m/s is not slower than jog {F(jog * p.m_speed)} m/s");

            // Braced.
            SelfTestHooks.HoldBlock = true;
            yield return Until(() => p.IsBlocking() && Brace.Clone != null && !Brace.Clone.m_hidden, 3f);
            var braced = Brace.Clone;
            c.Check(braced != null && !braced.m_hidden && Near(braced.m_speedModifier, -0.30f),
                $"braced: Braced effect shown with speed modifier -0.30 ({(braced != null ? F(braced.m_speedModifier) : "no effect")})");
            var speedBraced = SpeedMods(p);
            c.Check(Near(speedBraced, speed0 - 0.30f, 0.001f), $"braced: status-effect speed {F(speed0)} -> {F(speedBraced)} (every ground speed and turning)");
            SelfTestHooks.HoldBlock = false;
            yield return Until(() => !p.m_internalBlockingState, 2f);
            yield return Fixed;
            yield return Fixed;
            var released = Brace.Clone;
            c.Check(released != null && released.m_hidden && Near(released.m_speedModifier, 0f) && Near(SpeedMods(p), speed0, 0.001f),
                "released: Braced hidden, speed back to the carried one");

            // Sprint floor with a heavy slow (armor that add up).
            savedModifier = tower.m_shared.m_movementModifier;
            modifierChanged = true;
            tower.m_shared.m_movementModifier = -0.70f;
            p.UpdateModifiers();
            var jogHeavy = p.GetJogSpeedFactor();
            var runHeavy = p.GetRunSpeedFactor();
            var rawHeavy = (1f + runSkill * 0.25f) * (1f + p.GetEquipmentMovementModifier() * 1.5f);
            c.Check(runHeavy * p.m_runSpeed >= jogHeavy * p.m_speed - 0.001f && rawHeavy * p.m_runSpeed < jogHeavy * p.m_speed,
                $"movement -0.70: sprint {F(runHeavy * p.m_runSpeed)} m/s, jog {F(jogHeavy * p.m_speed)} m/s (vanilla sprint would be {F(rawHeavy * p.m_runSpeed)} m/s)");
            tower.m_shared.m_movementModifier = savedModifier;
            modifierChanged = false;

            // Put away: no slow (only items in hand count).
            p.HideHandItems();
            p.UpdateModifiers();
            c.Check(Near(p.GetJogSpeedFactor(), jog0, 0.0001f) && Near(p.GetRunSpeedFactor(), run0, 0.0001f),
                $"tower on the back: jog {F(p.GetJogSpeedFactor())} and sprint {F(p.GetRunSpeedFactor())} factors as without it");
            p.ShowHandItems(false, false);
            c.Report();
        }
        finally
        {
            if (modifierChanged && tower != null)
            {
                tower.m_shared.m_movementModifier = savedModifier;
            }
            rig.Done();
            if (rig.P != null)
            {
                rig.P.UpdateModifiers();
            }
        }
    }

    // ---------- tower.block ----------

    private static HitData Hit(Player p, Vector3 dir, float blunt, Character attacker)
    {
        var hit = new HitData();
        hit.m_damage.m_blunt = blunt;
        hit.m_dir = dir;
        hit.m_point = p.GetCenterPoint() - dir * 0.3f;
        hit.m_blockable = true;
        hit.m_dodgeable = true;
        hit.m_hitType = HitData.HitType.EnemyHit;
        hit.SetAttacker(attacker);
        return hit;
    }

    // Poison spray game mark unblockable (like Greydwarf Shaman's), carry poison effect.
    private static HitData Poison(Player p, Vector3 dir, Character attacker)
    {
        var hit = Hit(p, dir, 0f, attacker);
        hit.m_damage.m_poison = 30f;
        hit.m_blockable = false;
        hit.m_statusEffectHash = SEMan.s_statusEffectPoison;
        return hit;
    }

    // What vanilla make of one blunt hit from a creature on the braced player, from live state right before the hit.
    // Block armor from Blocking skill now: each block train it, so it grow during tower.block itself.
    // RPC_Damage: x difficulty scale x enemy damage rate (attacker not player) -> BlockAttack: block armor, stamina
    // cost, stagger of what get through -> player resistance -> body armor -> ApplyDamage: x damage taken rate,
    // stagger of what land.
    private struct Blow
    {
        internal float Armor;      // block armor now
        internal float Through;    // what get through the block (block step add this much stagger)
        internal float Lands;      // what land on health when the block hold
        internal float Unblocked;  // what land on health when the block fail
        internal float Cost;       // stamina the block use
        internal float BodyArmor;

        // Stagger bar gain before any resist when the block hold: block step + what land (stagger multiplier 1).
        internal float Full => Through + Lands;

        internal string Text => $"block armor {F(Armor)}, {F(Through)} through, {F(Lands)} lands (body armor {F(BodyArmor)}), {F(Cost)} stamina";
    }

    private static Blow Expect(Player p, ItemDrop.ItemData tower, float blunt)
    {
        var b = new Blow();
        var scaled = blunt * Game.instance.GetDifficultyDamageScalePlayer(p.transform.position) * Game.m_enemyDamageRate;
        b.Armor = tower.GetBlockPower(p.GetSkillFactor(Skills.SkillType.Blocking));
        b.Through = Through(scaled, b.Armor);
        b.Cost = p.m_blockStaminaDrain * Mathf.Clamp01((scaled - b.Through) / b.Armor)
                 * (1f + p.GetEquipmentBlockStaminaModifier()) * Game.m_staminaRate;
        b.BodyArmor = p.GetBodyArmor();
        b.Lands = OnBody(p, b.Through, b.BodyArmor);
        b.Unblocked = OnBody(p, scaled, b.BodyArmor);
        return b;
    }

    // What of this blunt land on the player's health after the block step (vanilla RPC_Damage then ApplyDamage).
    private static float OnBody(Player p, float blunt, float bodyArmor) =>
        Through(blunt * BluntTaken(p), bodyArmor) * Game.m_localDamgeTakenRate;

    private static string Worn(Player p)
    {
        var parts = new List<string>();
        foreach (var item in new[] { p.m_helmetItem, p.m_chestItem, p.m_legItem, p.m_shoulderItem })
        {
            if (item != null)
            {
                var name = item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared.m_name;
                parts.Add($"{name} {F(item.GetArmor())}");
            }
        }
        return parts.Count == 0 ? "nothing worn" : string.Join(", ", parts.ToArray());
    }

    // Wait till blocking again after last hit; me clean bar and push, heal full, set stamina and hold its regen.
    private static IEnumerator Ready(Player p, float stamina)
    {
        yield return Until(() => !p.IsStaggering() && p.IsBlocking() && !p.IsKnockedBack(), 4f);
        p.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectPoison, true);
        p.m_staggerDamage = 0f;
        p.m_pushForce = Vector3.zero;
        p.SetHealth(p.GetMaxHealth());
        p.m_stamina = stamina;
        p.m_staminaRegenTimer = 30f;
        yield return Fixed;
        yield return Fixed; // Braced upkeep see the new stamina
        p.m_stamina = stamina;
    }

    private static IEnumerator RunBlock()
    {
        var c = new Checks(BlockName);
        var rig = new Rig(BlockName);
        SE_Stats extra = null;
        try
        {
            var p = rig.P;
            p.SetGodMode(true); // the guard-break hit land 60 on a fresh character (25 health): god mode keep it at 1
            rig.TakeStaminaRate();
            var rules = TowerRules.Defaults();
            yield return UseRules(rules);
            var tower = rig.Give(IronTower);
            var grey = rig.Creature("Greydwarf");
            var shaman = rig.Creature("Greydwarf_Shaman");
            if (tower == null || grey == null || shaman == null)
            {
                c.Check(false, "could not add a ShieldIronTower or spawn the Greydwarf and the Greydwarf Shaman");
                c.Report();
                yield break;
            }
            c.Check(Game.m_staminaRate > 0f, $"stamina use on (StaminaRate key removed, rate {F(Game.m_staminaRate)})");
            var speed0 = SpeedMods(p);
            p.UpdateModifiers();
            var jog0 = p.GetJogSpeedFactor();
            p.EquipItem(tower);
            yield return Fixed;
            yield return Fixed;
            c.Check(p.GetSEMan().HaveStatusEffect(Brace.Hash) && Brace.Clone != null && Brace.Clone.m_hidden,
                "tower in hand: the Braced effect is on the player, hidden while not blocking");

            SelfTestHooks.HoldBlock = true;
            yield return Until(() => p.IsBlocking() && Brace.Clone != null && !Brace.Clone.m_hidden, 3f);
            var se = Brace.Clone;
            var hud = new List<StatusEffect>();
            p.GetSEMan().GetHUDStatusEffects(hud);
            c.Check(se != null && !se.m_hidden && se.m_name == Brace.BracedText && se.m_icon != null && hud.Contains(se),
                "blocking: 'Braced' shows on the HUD with the tower's icon");
            SelfTest.Screenshot(BlockName, "braced");
            yield return null;
            yield return null;
            if (se == null)
            {
                c.Report();
                yield break;
            }

            var fwd = Flat(p.transform.forward);
            var front = -fwd;  // hit travel toward the player's face
            var behind = fwd;
            var skill0 = p.GetSkillLevel(Skills.SkillType.Blocking);
            var blow = Expect(p, tower, 60f);
            SelfTest.Note(BlockName, $"60 blunt at Blocking {F(skill0)}: {blow.Text}; worn: {Worn(p)}; reserve {F(Brace.Reserve(p))}; player max health {F(p.GetMaxHealth())}, stagger threshold {F(p.GetStaggerTreshold())}. Expected values are worked out again before each hit (blocks train Blocking, which raises the block armor)");

            // Block formula (vanilla BlockAttack with x2.5 block armor; body armor after the block, as vanilla).
            yield return Ready(p, p.GetMaxStamina());
            blow = Expect(p, tower, 60f);
            var stamina = p.GetStamina();
            var health = p.GetHealth();
            var hit = Hit(p, front, 60f, grey);
            p.RPC_Damage(0L, hit);
            c.Check(Near(hit.m_damage.m_blunt, blow.Lands, 0.05f) && Near(health - p.GetHealth(), blow.Lands, 0.05f),
                $"60 blunt from the front: {F(hit.m_damage.m_blunt)} lands, health -{F(health - p.GetHealth())} ({F(blow.Lands)} expected: {blow.Text})");
            c.Check(Near(stamina - p.GetStamina(), blow.Cost, 0.05f), $"stamina used {F(stamina - p.GetStamina())} ({F(blow.Cost)} expected)");

            // (a) Unblockable frontal attacks.
            yield return Ready(p, p.GetMaxStamina());
            var poison = Poison(p, front, shaman);
            p.RPC_Damage(0L, poison);
            c.Check(poison.m_blockable && poison.m_statusEffectHash == 0, "unblockable poison from the front while braced: blocked, its poison effect cleared");
            yield return Ready(p, p.GetMaxStamina());
            poison = Poison(p, behind, shaman);
            p.RPC_Damage(0L, poison);
            c.Check(!poison.m_blockable && poison.m_statusEffectHash == SEMan.s_statusEffectPoison, "the same from behind: not blocked");
            yield return Ready(p, p.GetMaxStamina());
            var fall = Poison(p, front, null);
            fall.m_hitType = HitData.HitType.Fall;
            p.RPC_Damage(0L, fall);
            c.Check(!fall.m_blockable && fall.m_statusEffectHash == SEMan.s_statusEffectPoison, "a fall hit without attacker from the front: not blocked");
            // Attacker not hostile (BaseAI.IsEnemy false: same faction as players): its effect is not taken away.
            yield return Ready(p, p.GetMaxStamina());
            var shamanFaction = shaman.m_faction;
            shaman.m_faction = Character.Faction.Players;
            var friendly = BaseAI.IsEnemy(shaman, p);
            poison = Poison(p, front, shaman);
            p.RPC_Damage(0L, poison);
            shaman.m_faction = shamanFaction;
            c.Check(!friendly && !poison.m_blockable && poison.m_statusEffectHash == SEMan.s_statusEffectPoison,
                "the same from the front by a creature that is not hostile (players' faction): not blocked, its effect kept");
            var off = TowerRules.Defaults().With(r => r.BlockUnblockableAttacks = false);
            yield return UseRules(off);
            yield return Ready(p, p.GetMaxStamina());
            poison = Poison(p, front, shaman);
            p.RPC_Damage(0L, poison);
            c.Check(!poison.m_blockable && poison.m_statusEffectHash == SEMan.s_statusEffectPoison, "BlockUnblockableAttacks off: the frontal poison is not blocked");
            yield return UseRules(rules);
            se = Brace.Clone;
            if (se == null)
            {
                c.Check(false, "Braced effect lost after the rules change");
                c.Report();
                yield break;
            }

            // (b) Stagger resistance while the brace hold; guard break vanilla below one block of stamina.
            var threshold = p.GetStaggerTreshold();
            yield return Ready(p, 15f);
            c.Check(se.m_name == Brace.BracedText && !se.m_flashIcon && Near(se.m_staggerModifier, -0.80f),
                $"stamina 15: 'Braced', icon steady, stagger modifier -0.80 ('{se.m_name}', {F(se.m_staggerModifier)})");
            blow = Expect(p, tower, 60f);
            hit = Hit(p, front, 60f, grey);
            p.RPC_Damage(0L, hit);
            var bar = p.m_staggerDamage;
            c.Check(Near(bar, blow.Full * 0.2f, 0.2f), $"stamina 15: stagger bar +{F(bar)} ({F(blow.Full * 0.2f)} expected: 80% less than {F(blow.Full)})");
            c.Check(Near(hit.m_damage.m_blunt, blow.Lands, 0.05f), $"stamina 15: the block holds ({F(hit.m_damage.m_blunt)} lands, {F(blow.Lands)} expected)");
            yield return new WaitForSeconds(0.4f);
            c.Check(!p.IsStaggering(), "stamina 15: no stagger");

            // Another effect's stagger resist (-50%, like Fader's power): the sum stays at -1, never below.
            yield return Ready(p, p.GetMaxStamina());
            extra = ScriptableObject.CreateInstance<SE_Stats>();
            extra.name = "MC_TowerTest_StaggerResist";
            extra.m_name = "MC tower test";
            extra.m_ttl = 0f;
            extra.m_staggerModifier = -0.5f;
            p.GetSEMan().AddStatusEffect(extra);
            yield return Fixed;
            yield return Fixed;
            c.Check(Near(se.m_staggerModifier, -0.5f), $"with another -50% stagger effect: Braced gives {F(se.m_staggerModifier)} (-0.50 expected: the sum stays at -1)");
            p.m_staggerDamage = 0f;
            hit = Hit(p, front, 60f, grey);
            p.RPC_Damage(0L, hit);
            c.Check(p.m_staggerDamage >= 0f && p.m_staggerDamage <= 0.05f, $"with it a blocked hit adds no stagger and takes none away (bar {F(p.m_staggerDamage)})");
            p.GetSEMan().RemoveStatusEffect(extra.NameHash(), true);
            yield return Fixed;
            yield return Fixed;
            c.Check(Near(se.m_staggerModifier, -0.8f), $"the other effect gone: Braced gives {F(se.m_staggerModifier)} again (-0.80)");

            yield return Ready(p, 8f);
            c.Check(se.m_name == Brace.ExhaustedText && se.m_flashIcon && !se.m_hidden && Near(se.m_staggerModifier, 0f),
                $"stamina 8 (one block or less): 'Braced (exhausted)', icon flashing, no resistance ('{se.m_name}')");
            SelfTest.Screenshot(BlockName, "exhausted");
            yield return null;
            yield return null;
            p.m_stamina = 8f;
            blow = Expect(p, tower, 60f);
            hit = Hit(p, front, 60f, grey);
            p.RPC_Damage(0L, hit);
            bar = p.m_staggerDamage;
            var expected = Mathf.Min(blow.Full, threshold);
            c.Check(Near(bar, expected, 0.2f), $"stamina 8: stagger bar {F(bar)} ({F(expected)} expected: full {F(blow.Full)}, threshold {F(threshold)})");
            yield return Until(() => p.IsStaggering(), 0.6f);
            c.Check(p.IsStaggering() == blow.Full >= threshold, $"stamina 8: staggered {p.IsStaggering()} (expected {blow.Full >= threshold})");

            yield return Ready(p, 3f);
            blow = Expect(p, tower, 60f);
            hit = Hit(p, front, 60f, grey);
            p.RPC_Damage(0L, hit);
            c.Check(blow.Cost >= 3f && Near(p.GetStamina(), 0f, 0.001f) && Near(hit.m_damage.m_blunt, blow.Unblocked, 0.1f),
                $"stamina 3: the block empties stamina and the whole hit lands (block cost {F(blow.Cost)}, stamina {F(p.GetStamina())} after, {F(hit.m_damage.m_blunt)} lands, {F(blow.Unblocked)} expected: 60 after body armor {F(blow.BodyArmor)})");
            SelfTest.Note(BlockName, $"Blocking went from {F(skill0)} to {F(p.GetSkillLevel(Skills.SkillType.Blocking))} during the blocks");

            // (c) No push from the front while the brace hold.
            yield return Ready(p, p.GetMaxStamina());
            hit = Hit(p, front, 60f, grey);
            hit.m_pushForce = 100f;
            p.RPC_Damage(0L, hit);
            c.Check(!p.IsKnockedBack(), "a blocked frontal hit with push 100: not pushed");
            yield return Ready(p, p.GetMaxStamina());
            hit = Hit(p, behind, 1f, grey);
            hit.m_pushForce = 100f;
            p.RPC_Damage(0L, hit);
            c.Check(p.IsKnockedBack(), "the same push from behind: pushed");
            yield return Ready(p, p.GetMaxStamina());
            p.ApplyPushback(front, 50f);
            var pushedFront = p.IsKnockedBack();
            p.m_pushForce = Vector3.zero;
            p.ApplyPushback(behind, 50f);
            var pushedBehind = p.IsKnockedBack();
            p.m_pushForce = Vector3.zero;
            c.Check(!pushedFront && pushedBehind, $"area knockback: from the front {(pushedFront ? "pushed" : "not pushed")}, from behind {(pushedBehind ? "pushed" : "not pushed")}");

            // Release; put the tower away while blocking.
            SelfTestHooks.HoldBlock = false;
            yield return Until(() => !p.m_internalBlockingState, 2f);
            yield return Fixed;
            yield return Fixed;
            se = Brace.Clone;
            c.Check(se != null && se.m_hidden && Near(se.m_speedModifier, 0f) && Near(se.m_staggerModifier, 0f),
                "block released: Braced hidden, speed and stagger modifiers 0");
            SelfTestHooks.HoldBlock = true;
            yield return Until(() => p.IsBlocking() && Brace.Clone != null && !Brace.Clone.m_hidden, 3f);
            p.UnequipItem(tower);
            yield return Fixed;
            yield return Fixed;
            p.UpdateModifiers();
            c.Check(!p.GetSEMan().HaveStatusEffect(Brace.Hash) && Brace.Clone == null && Near(SpeedMods(p), speed0, 0.001f)
                    && Near(p.GetJogSpeedFactor(), jog0, 0.0001f),
                "tower put away while blocking: Braced gone, normal speed");
            c.Report();
        }
        finally
        {
            if (extra != null)
            {
                if (rig.P != null && rig.P.GetSEMan() != null)
                {
                    rig.P.GetSEMan().RemoveStatusEffect(extra.NameHash(), true);
                }
                Object.Destroy(extra);
            }
            rig.Done();
        }
    }

    // ---------- tower.bash ----------

    private static IEnumerator RunBash()
    {
        var c = new Checks(BashName);
        var rig = new Rig(BashName);
        try
        {
            var p = rig.P;
            var defaults = TowerRules.Defaults();
            yield return UseRules(defaults);
            // Same rules as before = nothing rebuilt: a fallback an earlier bash made would stay. Me start fresh (what a
            // rules apply do); the new tower copy get the new bash when equipped (heal).
            BashAttack.Reset();
            var tower = rig.Give(IronTower);
            var troll = rig.Creature("Troll");
            if (tower == null || troll == null)
            {
                c.Check(false, "could not add a ShieldIronTower or spawn a Troll");
                c.Report();
                yield break;
            }
            p.EquipItem(tower);
            yield return Fixed;
            var info = TowerCatalog.Held(p);
            c.Check(info != null, "Iron tower held");
            var blunt = info != null ? info.BashDamage : 12f;
            var taken = BluntTaken(troll);
            SelfTest.Note(BashName, $"Troll: health {F(troll.GetMaxHealth())}, stagger threshold {F(troll.GetStaggerTreshold())}, takes blunt x{F(taken)}, "
                                    + $"placed {F(PlaceInFront(p, troll))} m ahead (centre to centre); Blocking {F(p.GetSkillLevel(Skills.SkillType.Blocking))}");

            // Default (ShieldPunch at speed x0.6): bash land within two presses (first may fall back); slowed swing.
            var landed = false;
            Swing slow = null;
            for (var press = 1; press <= 2 && !landed; press++)
            {
                var s = new Swing();
                yield return Press(p, troll, s, BashName, $"default-{press}");
                SelfTest.Note(BashName, Describe($"default press {press}", s));
                if (s.Hit)
                {
                    c.Check(s.Landed, $"default press {press}: the Hit event came and the Troll took the bash");
                }
                if (s.Landed)
                {
                    landed = true;
                    slow = s;
                    CheckHit(c, $"default press {press}", s, blunt, 25f, 1f, taken);
                    CheckSpeed(c, $"default press {press}", s);
                }
            }
            c.Check(landed, "with the default settings a bash lands within two presses");
            if (slow != null)
            {
                c.Check(slow.Mode == BashAnimationKind.ShieldPunch && Near(slow.Factor, TowerRules.DefaultBashAnimationSpeed)
                        && Near(slow.RawSpeed, 2f),
                    $"default: ShieldPunch at x{F(slow.Factor)}, its punch clip's own speed is x2 ({F(slow.RawSpeed)})");
                SelfTest.Note(BashName, slow.StartScaled
                    ? "the punch clip's Speed event at its first frame came after the attack state was seen: the start was scaled first, then the event"
                    : "the punch clip's Speed event at its first frame came with the attack state: only the event was scaled");
            }

            // Same punch at speed 1 (first test build speed): slowed one must hit and end later.
            yield return UseRules(defaults.With(r => r.BashAnimationSpeed = 1f));
            var fast = new Swing();
            yield return Press(p, troll, fast, BashName, null);
            SelfTest.Note(BashName, Describe("ShieldPunch at speed 1", fast));
            CheckSpeed(c, "ShieldPunch at speed 1", fast);
            if (slow != null && slow.HitAt > 0f && fast.HitAt > 0f && slow.EndAt > 0f && fast.EndAt > 0f)
            {
                var hitRatio = slow.HitAt / fast.HitAt;
                var endRatio = slow.EndAt / fast.EndAt;
                SelfTest.Note(BashName, $"swing speed: hit {T(fast.HitAt)} after the press at speed 1, {T(slow.HitAt)} at x{F(slow.Factor)} "
                                        + $"(x{F(hitRatio)}); swing over {T(fast.EndAt)} and {T(slow.EndAt)} (x{F(endRatio)}; the 0.15 s hit-stop is not scaled)");
                c.Check(hitRatio >= 1.4f && hitRatio <= 1.9f, $"at x{F(slow.Factor)} the hit comes about 1 / {F(slow.Factor)} times later than at speed 1 (x{F(hitRatio)})");
                c.Check(endRatio >= 1.25f, $"and the swing ends later (x{F(endRatio)})");
            }
            else
            {
                c.Check(false, $"the slowed and the speed 1 bash both hit and end (to compare them): slow hit {(slow != null ? T(slow.HitAt) : "none")}, fast hit {T(fast.HitAt)}");
            }

            // Each other option on its own (default speed).
            foreach (var kind in new[] { BashAnimationKind.OtherPunch, BashAnimationKind.Kick })
            {
                var r = WithAnimation(kind);
                yield return UseRules(r);
                c.Check(TowerSync.AppliedKey == r.Key && BashAttack.Mode == kind, $"{kind}: rules applied, option resolved as {BashAttack.Mode}");
                var s = new Swing();
                yield return Press(p, troll, s, BashName, kind.ToString().ToLowerInvariant());
                SelfTest.Note(BashName, Describe(kind.ToString(), s));
                if (s.Hit)
                {
                    c.Check(s.Landed, $"{kind}: the Hit event came and the Troll took the bash");
                }
                if (s.Landed)
                {
                    CheckHit(c, kind.ToString(), s, blunt, 25f, 1f, taken);
                }
                CheckSpeed(c, kind.ToString(), s);
                if (kind == BashAnimationKind.OtherPunch)
                {
                    c.Check(s.Started && s.InAttackAt >= 0f && s.InAttackAt <= BashWatch.StartTimeout + 0.05f,
                        $"OtherPunch (last fallback) reaches its attack state within {F(BashWatch.StartTimeout)} s ({T(s.InAttackAt)})");
                    c.Check(s.Landed, "OtherPunch lands its hit");
                }
                if (kind == BashAnimationKind.Kick)
                {
                    c.Check(s.Started && s.StartScaled && Near(s.RawSpeed, 1f),
                        $"Kick: no Speed event before its hit, so its start speed is scaled (clip's own {F(s.RawSpeed)}, start scaled {s.StartScaled})");
                }
            }

            // Custom: player attack trigger play; emote and made-up name refused at resolution.
            var bomb = WithAnimation(BashAnimationKind.Custom, "throw_bomb");
            yield return UseRules(bomb);
            var bash = BashAttack.Current;
            c.Check(BashAttack.Mode == BashAnimationKind.Custom && bash != null && bash.m_attackAnimation == "throw_bomb",
                $"Custom throw_bomb accepted ({BashAttack.Mode}, '{(bash != null ? bash.m_attackAnimation : "")}')");
            var sb = new Swing();
            yield return Press(p, troll, sb, BashName, "custom-throw_bomb");
            SelfTest.Note(BashName, Describe("Custom throw_bomb", sb));
            if (sb.Landed)
            {
                CheckHit(c, "Custom throw_bomb", sb, blunt, 25f, 1f, taken);
            }
            CheckSpeed(c, "Custom throw_bomb", sb);
            foreach (var bad in new[] { "emote_wave", "mc_no_such_trigger", "staff_rapidfire" })
            {
                yield return UseRules(WithAnimation(BashAnimationKind.Custom, bad));
                bash = BashAttack.Current;
                c.Check(BashAttack.Mode == BashAnimationKind.ShieldPunch && bash != null && bash.m_attackAnimation == BashAttack.ShieldArmTrigger,
                    $"Custom '{bad}' refused at resolution: option {BashAttack.Mode}, trigger '{(bash != null ? bash.m_attackAnimation : "")}' (Warning in the log)");
            }

            // Two Hit events in one clip: one hit per bash.
            yield return UseRules(WithAnimation(BashAnimationKind.Custom, "dualaxes3"));
            bash = BashAttack.Current;
            SelfTest.Note(BashName, $"Custom dualaxes3 resolved as {BashAttack.Mode} ('{(bash != null ? bash.m_attackAnimation : "")}')");
            var sd = new Swing();
            yield return Press(p, troll, sd, BashName, "custom-dualaxes3");
            SelfTest.Note(BashName, Describe("Custom dualaxes3", sd));
            c.Check(sd.Events - sd.Skipped <= 1, $"Custom dualaxes3: at most one hit per bash ({sd.Events} Hit events, {sd.Skipped} skipped)");
            if (sd.Landed)
            {
                CheckHit(c, "Custom dualaxes3", sd, blunt, 25f, 1f, taken);
            }

            // A bash that never ends: me put the held Ice Shards trigger (looping clip, left only by attack_abort) on
            // the bash past the Custom check. Watchdog (C) must stop it within MaxAttackSeconds / swing speed and fall
            // back (or (A) if the clip never starts from the shield stance); the player must be free again.
            yield return UseRules(WithAnimation(BashAnimationKind.Custom, "throw_bomb"));
            var shared = BashAttack.Current;
            if (BashAttack.Mode == BashAnimationKind.Custom && shared != null)
            {
                shared.m_attackAnimation = "staff_rapidfire";
                var limit = BashWatch.MaxAttackSeconds / Mathf.Min(1f, TowerSync.Applied.BashAnimationSpeed);
                yield return WaitIdle(p);
                yield return WaitCooldown(p);
                PlaceInFront(p, troll);
                yield return Fixed;
                var t0 = Time.time;
                var started = p.StartAttack(null, false);
                var stateAt = -1f;
                var freeAt = -1f;
                while (started && Time.time - t0 < limit + 2.5f)
                {
                    yield return null;
                    if (stateAt < 0f && p.InAttack())
                    {
                        stateAt = Time.time - t0;
                    }
                    if (BashWatch.Running == null && !p.InAttack() && BashAttack.Mode != BashAnimationKind.Custom)
                    {
                        freeAt = Time.time - t0;
                        break;
                    }
                }
                var stuck = p.InAttack();
                SelfTest.Note(BashName, $"held trigger staff_rapidfire on the bash: started {started}, attack state at {T(stateAt)}, free at {T(freeAt)}, "
                                        + $"limit {F(limit)} s at speed x{F(TowerSync.Applied.BashAnimationSpeed)}, option after {BashAttack.Mode}, "
                                        + $"animator speed {F(p.m_animator.speed)}, clips [{ClipNames(p)}]");
                c.Check(started && !stuck && BashAttack.Mode == BashAnimationKind.ShieldPunch && freeAt >= 0f
                        && (stateAt < 0f || freeAt <= stateAt + limit + 1f),
                    $"a bash whose clip never ends is stopped (attack_abort) within {F(limit)} s and falls back to ShieldPunch "
                    + $"(attack state at {T(stateAt)}, free at {T(freeAt)}, option {BashAttack.Mode})");
                if (stuck)
                {
                    p.Stagger(-Flat(p.transform.forward)); // free the player for the next steps
                    yield return WaitIdle(p);
                    SelfTest.Note(BashName, $"attack_abort did not free the player; a stagger {(p.InAttack() ? "did not either" : "did")}");
                }
            }
            else
            {
                c.Check(false, $"Custom throw_bomb not in use for the no-end check (option {BashAttack.Mode})");
            }

            // How many bashes a Draugr need (threshold 50; lowest Iron bash stagger 75).
            ZNetScene.instance.Destroy(troll.gameObject);
            var draugr = rig.Creature("Draugr");
            yield return UseRules(TowerRules.Defaults());
            if (draugr == null)
            {
                c.Check(false, "could not spawn a Draugr");
                c.Report();
                yield break;
            }
            yield return Fixed;
            draugr.m_staggerDamage = 0f;
            var hits = 0;
            var staggered = false;
            for (var press = 1; press <= 5 && !staggered; press++)
            {
                var s = new Swing();
                yield return Press(p, draugr, s, BashName, null, resetBar: false);
                SelfTest.Note(BashName, Describe($"Draugr press {press}", s));
                if (s.Landed)
                {
                    hits++;
                    staggered = s.Staggered;
                }
            }
            SelfTest.Note(BashName, staggered
                ? $"a Draugr (threshold {F(draugr.GetStaggerTreshold())}) staggered after {hits} bash hit(s)"
                : $"a Draugr was not staggered by {hits} bash hit(s)");
            c.Check(staggered && hits == 1, $"one Iron tower bash staggers a Draugr ({hits} hit(s), staggered {staggered})");

            // Bash cooldown through vanilla entry points (decision 30). (a) Press right after a bash, inside the
            // cooldown: refused, nothing start; cooldown over: accepted.
            var cooldown = TowerSync.Applied.BashCooldown;
            c.Check(Near(cooldown, 2f), $"default bash cooldown 2 s ({F(cooldown)})");
            var sa = new Swing();
            yield return Press(p, draugr, sa, BashName, null);
            var startsBefore = BashWatch.DebugStarts.Count;
            var left = BashWatch.CooldownLeft(p);
            var tooSoon = p.StartAttack(null, false);
            c.Check(sa.Started && left > 0f && !tooSoon && BashWatch.DebugStarts.Count == startsBefore,
                $"a press {F(cooldown - left)} s after the last bash start is refused (started {tooSoon}, {F(left)} s of cooldown left)");
            yield return Until(() => !BashWatch.InCooldown(p), cooldown + 0.5f);
            var afterCooldown = Time.time - BashWatch.DebugStarts[BashWatch.DebugStarts.Count - 1];
            var accepted = p.StartAttack(null, false);
            c.Check(accepted && BashWatch.DebugStarts.Count == startsBefore + 1,
                $"a press {F(afterCooldown)} s after the last bash start is accepted (started {accepted})");
            yield return WaitIdle(p);

            // (a2) One press once the swing is over, more than half a second before the cooldown ends (vanilla keep a
            // press 0.5 s only): kept, the bash start when the time is up. Press = what vanilla PlayerAttackInput do.
            var sk = new Swing();
            yield return Press(p, draugr, sk, BashName, null);
            var keptFrom = BashWatch.DebugStarts.Count;
            var lastStart = keptFrom > 0 ? BashWatch.DebugStarts[keptFrom - 1] : Time.time;
            var leftAtPress = BashWatch.CooldownLeft(p);
            var swingOver = !p.InAttack();
            p.m_queuedAttackTimer = BashWatch.QueuedPress;
            yield return Until(() => BashWatch.DebugStarts.Count > keptFrom, leftAtPress + 1f);
            var keptGap = BashWatch.DebugStarts.Count > keptFrom ? BashWatch.DebugStarts[keptFrom] - lastStart : -1f;
            SelfTest.Note(BashName, $"one press {F(cooldown - leftAtPress)} s after a bash start (swing over {swingOver}, {F(leftAtPress)} s of cooldown left): "
                                    + (keptGap >= 0f ? $"next bash {F(keptGap)} s after the last one" : "no bash"));
            c.Check(sk.Started && swingOver && leftAtPress > BashWatch.QueuedPress + 0.05f,
                $"setup: the press comes after the swing, more than {F(BashWatch.QueuedPress)} s before the cooldown ends ({F(leftAtPress)} s left, swing over {swingOver})");
            c.Check(keptGap >= cooldown - 0.03f && keptGap <= cooldown + 0.1f,
                $"a single press {F(leftAtPress)} s before the cooldown ends is kept: the bash starts {T(keptGap)} after the last one ({F(cooldown)} s expected)");
            yield return WaitIdle(p);

            // (b) Attack button held (vanilla PlayerAttackInput call StartAttack every tick): one bash per cooldown,
            // each one as soon as cooldown is over.
            yield return WaitCooldown(p);
            var n0 = BashWatch.DebugStarts.Count;
            var h0 = Time.time;
            SelfTestHooks.HoldAttack = true;
            yield return UntilTime(h0 + 2f * cooldown + 0.6f);
            SelfTestHooks.HoldAttack = false;
            yield return WaitIdle(p);
            var starts = BashWatch.DebugStarts.Skip(n0).ToList();
            var gaps = new List<float>();
            for (var i = 1; i < starts.Count; i++)
            {
                gaps.Add(starts[i] - starts[i - 1]);
            }
            var gapText = string.Join(", ", gaps.Select(F).ToArray());
            SelfTest.Note(BashName, $"attack held {F(2f * cooldown + 0.6f)} s: {starts.Count} bash starts, first {T(starts.Count > 0 ? starts[0] - h0 : -1f)} after the hold, gaps {gapText} s");
            c.Check(starts.Count == 3 && gaps.All(g => g >= cooldown - 0.03f && g <= cooldown + 0.15f),
                $"held attack button: a bash every {F(cooldown)} s ({starts.Count} starts, gaps {gapText} s)");

            // (c) Cooldown 0: next bash may start as soon as last swing is over (first test build).
            yield return UseRules(TowerRules.Defaults().With(r => r.BashCooldown = 0f));
            var s0 = new Swing();
            yield return Press(p, draugr, s0, BashName, null);
            var again = p.StartAttack(null, false);
            c.Check(s0.Started && again, $"BashCooldown 0: a press right after the swing is accepted (started {again})");
            yield return WaitIdle(p);

            // Stamina of one bash, world stamina rate back (test world has none): 20 with the vanilla
            // attack rules (equipment, effects, -33% x Blocking skill factor, world stamina rate).
            yield return UseRules(TowerRules.Defaults());
            rig.TakeStaminaRate();
            p.m_stamina = p.GetMaxStamina();
            yield return Fixed;
            var skill = p.GetSkillFactor(Skills.SkillType.Blocking);
            var cost = TowerRules.DefaultBashStamina * (1f + p.GetEquipmentAttackStaminaModifier());
            p.GetSEMan().ModifyAttackStaminaUsage(cost, ref cost);
            cost -= cost * 0.33f * skill;
            cost *= Game.m_staminaRate;
            var st = new Swing();
            yield return Press(p, draugr, st, BashName, null);
            SelfTest.Note(BashName, Describe("stamina bash", st) + $"; expected cost {F(cost)} (Blocking factor {F(skill)}, world stamina rate {F(Game.m_staminaRate)}, max stamina {F(p.GetMaxStamina())})");
            c.Check(Game.m_staminaRate > 0f && st.Started && Near(st.StaminaUsed, cost, 0.3f),
                $"one bash costs {F(st.StaminaUsed)} stamina ({F(cost)} expected: 20 with the vanilla attack rules; world stamina rate {F(Game.m_staminaRate)})");
            c.Report();
        }
        finally
        {
            SelfTestHooks.HoldAttack = false;
            rig.Done();
        }
    }

    // ---------- tower.lock ----------

    // Bash hit as the bearer's game send it (Attack.DoMeleeAttack fields BashStagger read), from the player.
    private static HitData BashHit(Player p, Character target, float blunt, float staggerMultiplier)
    {
        var dir = Flat(target.transform.position - p.transform.position);
        var hit = new HitData();
        hit.m_damage.m_blunt = blunt;
        hit.m_dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Flat(p.transform.forward);
        hit.m_point = target.GetCenterPoint();
        hit.m_blockable = true;
        hit.m_dodgeable = true;
        hit.m_hitType = HitData.HitType.PlayerHit;
        hit.m_skill = Skills.SkillType.Blocking;
        hit.m_staggerMultiplier = staggerMultiplier;
        hit.SetAttacker(p);
        return hit;
    }

    private static IEnumerator RunLock()
    {
        var c = new Checks(LockName);
        var rig = new Rig(LockName);
        try
        {
            var p = rig.P;
            // Lock limit alone: no bash cooldown (it would hide the lock), swing at speed 1 (bash every ~0.8 s).
            var rules = WithAnimation(BashAnimationKind.OtherPunch).With(r =>
            {
                r.BashCooldown = 0f;
                r.BashAnimationSpeed = 1f;
            });
            yield return UseRules(rules);
            c.Check(Near(TowerRules.Defaults().BashStaggerLock, 8f) && Near(rules.BashStaggerLock, 8f), $"default stagger lock 8 s ({F(rules.BashStaggerLock)})");
            var tower = rig.Give(IronTower);
            var draugr = rig.Creature("Draugr");
            if (tower == null || draugr == null)
            {
                c.Check(false, "could not add a ShieldIronTower or spawn a Draugr");
                c.Report();
                yield break;
            }
            p.EquipItem(tower);
            yield return Fixed;
            var first = new Swing();
            yield return Press(p, draugr, first, LockName, null);
            SelfTest.Note(LockName, Describe("first bash", first));
            c.Check(first.Landed && first.Staggered, $"the first bash staggers the Draugr (threshold {F(draugr.GetStaggerTreshold())})");
            if (!first.Landed)
            {
                c.Report();
                yield break;
            }
            var t1 = BashWatch.DebugFirstHitTime;

            // Inside lock: bash every ~0.8 s for 6 s add only what land x1 (like punch).
            var locked = 0;
            var next = t1 + 0.8f;
            while (Time.time < t1 + 6f)
            {
                yield return UntilTime(next);
                var s = new Swing();
                yield return Press(p, draugr, s, LockName, null, resetBar: false);
                next = Mathf.Max(next + 0.8f, Time.time);
                if (!s.Landed)
                {
                    SelfTest.Note(LockName, Describe("locked bash (no hit)", s));
                    continue;
                }
                var at = BashWatch.DebugFirstHitTime - t1;
                if (at >= rules.BashStaggerLock)
                {
                    break;
                }
                locked++;
                c.Check(s.Gained <= s.Lost + 1f, $"bash at {F(at)} s: stagger +{F(s.Gained)}, landed {F(s.Lost)} (x1 at most)");
                SelfTest.Note(LockName, $"bash at {F(at)} s: stagger +{F(s.Gained)}, landed {F(s.Lost)}{(s.Staggered ? " (staggered by the punch-level stagger)" : "")}");
            }
            c.Check(locked >= 5, $"{locked} bashes landed within the lock limit");

            // After the lock: heavy stagger again.
            yield return UntilTime(t1 + rules.BashStaggerLock + 0.5f);
            var again = new Swing();
            yield return Press(p, draugr, again, LockName, null, resetBar: false);
            var againAt = BashWatch.DebugFirstHitTime - t1;
            SelfTest.Note(LockName, Describe($"bash at {F(againAt)} s", again));
            c.Check(again.Landed && again.Staggered, $"a bash {F(againAt)} s after the first (lock {F(rules.BashStaggerLock)} s) staggers it again");
            yield return Until(() => draugr.IsStaggering(), 0.5f);
            var from = Time.time;
            yield return Until(() => !draugr.IsStaggering(), 5f);
            SelfTest.Note(LockName, $"the Draugr's stagger animation lasted about {F(Time.time - from)} s (of the {F(rules.BashStaggerLock)} s lock limit)");

            // BashStaggerLock 0: no limit. Second bash from an empty bar (the bar stop at the threshold, so a x1 bash
            // on the first bash's full bar would read "staggered" too): only a heavy bash fill it (x1 about 13 at most).
            // It land while the Draugr still stagger from the first (about 2.5 s): the bar is what tell heavy from x1.
            yield return UseRules(rules.With(r => r.BashStaggerLock = 0f));
            var free1 = new Swing();
            yield return Press(p, draugr, free1, LockName, null);
            var free2 = new Swing();
            yield return Press(p, draugr, free2, LockName, null);
            SelfTest.Note(LockName, Describe("no limit, bash 1", free1) + " || " + Describe("no limit, bash 2", free2));
            c.Check(free1.Staggered && free2.Staggered, "BashStaggerLock 0: back-to-back bashes add heavy stagger each time (each fills the empty bar)");

            // One heavy stagger per bash (decision 35): Draugr A in the middle, Draugr B beside it, right then left
            // (vanilla sweep the arc from one side: B is hit first once, last once). Arc 160 degrees so B surely is
            // hit. A must take the heavy stagger both times, B only the landed blunt x1 (and still its damage).
            yield return UseRules(rules.With(r =>
            {
                r.BashStaggerLock = 0f;
                r.BashAngle = 160f;
            }));
            var side = rig.Creature("Draugr");
            if (side == null)
            {
                c.Check(false, "could not spawn a second Draugr for the group bash");
                c.Report();
                yield break;
            }
            // Lane free over the middle and both side places (PlaceBeside's angle from the radii, 10 degrees more).
            var besideAngle = 2f * Mathf.Asin(Mathf.Clamp01((Radius(draugr) + Radius(side) + 0.1f)
                                                            / (2f * (Radius(p) + Radius(draugr) + 0.3f)))) * Mathf.Rad2Deg;
            foreach (var sign in new[] { 1f, -1f })
            {
                var where = sign > 0f ? "right" : "left";
                yield return WaitIdle(p);
                yield return WaitCooldown(p);
                FaceClearLane(p, Reach(p, draugr), besideAngle + LaneFan, LockName, draugr, side);
                var d = PlaceInFront(p, draugr);
                var angle = PlaceBeside(p, side, draugr, sign);
                draugr.m_staggerDamage = 0f;
                side.m_staggerDamage = 0f;
                draugr.SetHealth(draugr.GetMaxHealth());
                side.SetHealth(side.GetMaxHealth());
                yield return Fixed;
                var sideHp = side.GetHealth();
                var events0 = BashWatch.DebugHitEvents;
                BashTarget.DebugLastPick = null;
                var started = p.StartAttack(null, false);
                yield return Until(() => BashWatch.DebugHitEvents > events0, 3f);
                yield return null;
                var hit = BashWatch.DebugHitEvents > events0;
                var since = Time.time - BashWatch.DebugFirstHitTime;
                var middleBar = draugr.m_staggerDamage + Drain(draugr) * since;
                var sideBar = side.m_staggerDamage + Drain(side) * since;
                var sideLost = sideHp - side.GetHealth();
                var pick = BashTarget.DebugLastPick;
                SelfTest.Note(LockName, $"group bash, Draugr B on the {where} ({F(angle)} degrees off the middle, both {F(d)} m away): started {started}, hit {hit}, "
                                        + $"picked {(ReferenceEquals(pick, draugr) ? "A (middle)" : ReferenceEquals(pick, side) ? "B (side)" : "none")}; "
                                        + $"A's bar {F(middleBar)} of {F(draugr.GetStaggerTreshold())}, B's bar {F(sideBar)}, B lost {F(sideLost)} health");
                c.Check(started && hit && ReferenceEquals(pick, draugr) && middleBar >= draugr.GetStaggerTreshold() - 0.05f,
                    $"group bash, B on the {where}: the Draugr in the middle takes the heavy stagger (bar {F(middleBar)})");
                c.Check(sideLost > 0.001f, $"group bash, B on the {where}: the Draugr beside it is hit too (health -{F(sideLost)})");
                c.Check(sideBar <= sideLost + 1f && sideBar < side.GetStaggerTreshold() - 0.05f,
                    $"group bash, B on the {where}: the Draugr beside it takes only the landed blunt x1 as stagger (bar {F(sideBar)}, landed {F(sideLost)})");
            }
            ZNetScene.instance.Destroy(side.gameObject);

            // A bash on a creature already staggering (by someone else) staggers nothing: no lock. Bash hits built in
            // code and passed to the creature's RPC_Damage (no swing timing).
            yield return UseRules(rules);
            var other = rig.Creature("Draugr");
            if (other == null)
            {
                c.Check(false, "could not spawn a second Draugr");
                c.Report();
                yield break;
            }
            yield return Fixed;
            var toward = Flat(other.transform.position - p.transform.position);
            other.m_staggerDamage = 0f;
            other.AddStaggerDamage(other.GetStaggerTreshold() + 1f, toward, null);
            yield return Until(() => other.IsStaggering(), 1f);
            var wasStaggering = other.IsStaggering();
            other.RPC_Damage(0L, BashHit(p, other, 12f, rules.BashStagger));
            var during = other.m_staggerDamage;
            c.Check(wasStaggering && during >= other.GetStaggerTreshold() - 0.05f && !BashStagger.DebugLockTime(other, out _),
                $"a bash on a Draugr that is already staggering fills its bar ({F(during)}) but starts no lock limit (staggering {wasStaggering})");
            yield return Until(() => !other.IsStaggering(), 5f);
            yield return Fixed;
            other.m_staggerDamage = 0f;
            other.RPC_Damage(0L, BashHit(p, other, 12f, rules.BashStagger));
            var bar = other.m_staggerDamage;
            c.Check(BashStagger.DebugLockTime(other, out _) && bar >= other.GetStaggerTreshold() - 0.05f,
                $"then a bash that staggers it: heavy stagger (bar {F(bar)} of {F(other.GetStaggerTreshold())}) and the lock starts");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- tower.ng ----------

    private static IEnumerator RunNg()
    {
        var c = new Checks(NgName);
        var rig = new Rig(NgName);
        try
        {
            var p = rig.P;
            rig.SetWorldLevel(1);
            yield return null;
            c.Check(Game.m_worldLevel == 1, $"global key WorldLevel 1 = world level {Game.m_worldLevel}");
            var gear = Game.instance.m_worldLevelGearBaseDamage;
            var ng = Game.m_worldLevel * Game.instance.m_worldLevelEnemyHPMultiplier;
            SelfTest.Note(NgName, $"world level {Game.m_worldLevel}: gear bonus {gear} per level, enemy health x{F(ng)}, enemy armor {Game.m_worldLevel * Game.instance.m_worldLevelEnemyBaseAC}");
            var rules = WithAnimation(BashAnimationKind.OtherPunch);
            yield return UseRules(rules);
            var tower = rig.Give(IronTower);
            if (tower == null)
            {
                c.Check(false, "could not add a ShieldIronTower");
                c.Report();
                yield break;
            }
            var tip = tower.GetTooltip();
            c.Check(tower.m_worldLevel == 1, $"a tower made now carries world level {tower.m_worldLevel}");
            c.Check(Near(tower.GetDamage().m_blunt, 12f) && tip.Contains("$inventory_blunt: <color=orange>12</color>"),
                $"its bash blunt stays 12, no +{gear} (GetDamage {F(tower.GetDamage().m_blunt)}; tooltip '{tip.Replace("\n", " | ")}')");
            p.EquipItem(tower);
            var draugr = rig.Creature("Draugr");
            if (draugr == null)
            {
                c.Check(false, "could not spawn a Draugr");
                c.Report();
                yield break;
            }
            var ai = draugr.GetComponent<MonsterAI>();
            if (ai != null)
            {
                ai.m_viewRange = 0f;
                ai.m_hearRange = 0f;
            }
            yield return Fixed;
            var threshold = draugr.GetStaggerTreshold();
            c.Check(Near(threshold, 50f * ng, 0.5f), $"Draugr made in world level 1: health {F(draugr.GetMaxHealth())}, stagger threshold {F(threshold)} ({F(50f * ng)} expected)");
            c.Check(ai != null && !ai.IsAlerted() && ai.GetTargetCreature() == null, "the Draugr is unaware before the bash");

            // Bash 1 with threshold raised x4 for the measure: full stagger (150-330) above its real threshold.
            var factor = draugr.m_staggerDamageFactor;
            draugr.m_staggerDamageFactor = factor * 4f;
            var s1 = new Swing();
            try
            {
                yield return Press(p, draugr, s1, NgName, "bash");
            }
            finally
            {
                draugr.m_staggerDamageFactor = factor;
            }
            SelfTest.Note(NgName, Describe("bash (threshold raised for the measure)", s1));
            c.Check(s1.Landed, "the bash hits the Draugr");
            if (s1.Landed)
            {
                CheckHit(c, "world level 1", s1, 12f, 25f, ng, -1f);
                SelfTest.Note(NgName, $"health lost to the bash: {F(s1.Lost)} ({(s1.Lost <= 0.1f ? "0.1 or less: this mod made the OnDamaged call vanilla skipped" : "above 0.1: the game made the OnDamaged call")})");
            }
            yield return Fixed;
            c.Check(ai != null && ai.IsAlerted() && ReferenceEquals(ai.GetTargetCreature(), p), "right after the bash the Draugr is alerted and targets the player");

            // Bash 2 at real threshold: one bash stagger it (no lock: bash 1 not stagger).
            var s2 = new Swing();
            yield return Press(p, draugr, s2, NgName, null);
            SelfTest.Note(NgName, Describe("bash at the real threshold", s2));
            c.Check(s2.Landed && s2.Staggered, $"one bash still staggers a world level 1 Draugr (threshold {F(threshold)})");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- tower.toggle ----------

    private static IEnumerator RunToggle()
    {
        var c = new Checks(ToggleName);
        var rig = new Rig(ToggleName);
        var off = false;
        try
        {
            var p = rig.P;
            var rules = WithAnimation(BashAnimationKind.OtherPunch);
            yield return UseRules(rules);
            var tower = rig.Give(IronTower);
            var sword = rig.Give("SwordIron");
            var snap = SnapshotOf(IronTower);
            var prefab = SharedOf(IronTower);
            var chest = rig.Chest(out var chestName);
            var inChest = chest != null ? chest.GetInventory().AddItem(IronTower, 1, 1, 0, 0L, "", false) : null;
            var ground = rig.Ground(IronTower);
            if (tower == null || sword == null || snap == null || inChest == null || ground == null)
            {
                c.Check(false, $"setup: tower {tower != null}, sword {sword != null}, snapshot {snap != null}, chest ({chestName}) copy {inChest != null}, ground copy {ground != null}");
                c.Report();
                yield break;
            }
            yield return Fixed;
            c.Check(IsTower(tower.m_shared, snap, rules) && IsTower(prefab, snap, rules) && IsTower(inChest.m_shared, snap, rules) && IsTower(ground.m_itemData.m_shared, snap, rules),
                "on: inventory, prefab, chest and ground copies are towers");

            // Off (OnDeactivated's steps).
            TowerSync.Deactivate(false);
            off = true;
            c.Check(IsVanilla(tower.m_shared, snap), "off: the inventory copy is vanilla");
            c.Check(IsVanilla(prefab, snap), "off: the prefab is vanilla");
            c.Check(IsVanilla(inChest.m_shared, snap), $"off: the copy in the chest ({chestName}) is vanilla");
            c.Check(IsVanilla(ground.m_itemData.m_shared, snap), "off: the copy on the ground is vanilla");
            p.EquipItem(sword);
            p.EquipItem(tower);
            c.Check(ReferenceEquals(p.m_rightItem, sword) && ReferenceEquals(p.m_leftItem, tower), $"off: sword and the one-handed tower held together ({Hands(p)})");

            // On again (OnActivated's steps): the sword is put away, towers everywhere again.
            TowerSync.Activate();
            off = false;
            yield return Fixed;
            c.Check(p.m_rightItem == null && !sword.m_equipped && ReferenceEquals(p.m_leftItem, tower) && IsTower(tower.m_shared, snap, rules),
                $"on while holding a sword and a tower: the sword is put away, the tower is two-handed ({Hands(p)})");
            c.Check(IsTower(prefab, snap, rules) && IsTower(inChest.m_shared, snap, rules) && IsTower(ground.m_itemData.m_shared, snap, rules),
                "on: prefab, chest and ground copies are towers again");

            // Off in the middle of a bash, before its Hit event: cancelled, no hit.
            var troll = rig.Creature("Troll");
            if (troll == null)
            {
                c.Check(false, "could not spawn a Troll");
                c.Report();
                yield break;
            }
            yield return WaitIdle(p);
            FaceClearLane(p, Reach(p, troll), LaneFan, ToggleName, troll); // "no hit" must mean the cancel, not a rock
            PlaceInFront(p, troll);
            troll.SetHealth(troll.GetMaxHealth());
            yield return Fixed;
            var hp = troll.GetHealth();
            var events = BashWatch.DebugHitEvents;
            var started = p.StartAttack(null, false);
            TowerSync.Deactivate(false);
            off = true;
            c.Check(started && p.m_currentAttack == null, $"bash started ({started}) and cancelled by turning off: no current attack");
            c.Check(tower.m_shared.m_itemType == ItemType.Shield, "the held tower is a one-handed shield in place");
            yield return new WaitForSeconds(1.2f);
            c.Check(Near(troll.GetHealth(), hp) && BashWatch.DebugHitEvents == events,
                $"1.2 s later: no bash hit, the Troll's health unchanged ({F(hp)} -> {F(troll.GetHealth())})");
            c.Check(Near(p.m_animator.speed, 1f, 0.01f), $"animator speed 1 after that clip ({F(p.m_animator.speed)})");
            TowerSync.Activate();
            off = false;

            // Off in the middle of the slowed swing (attack state reached, before its Hit event): cancelled, and the
            // clip's own speed back at once (BashSpeed.End), no hit.
            yield return Fixed;
            yield return WaitIdle(p);
            yield return WaitCooldown(p);
            FaceClearLane(p, Reach(p, troll), LaneFan, ToggleName, troll);
            PlaceInFront(p, troll);
            troll.SetHealth(troll.GetMaxHealth());
            yield return Fixed;
            hp = troll.GetHealth();
            events = BashWatch.DebugHitEvents;
            var t0 = Time.time;
            var midStarted = p.StartAttack(null, false);
            yield return Until(() => p.InAttack() && Time.time - t0 >= 0.15f, 0.5f);
            var slowed = p.m_animator.speed;
            var raw = BashSpeed.DebugRaw;
            var factor = TowerSync.Applied != null ? TowerSync.Applied.BashAnimationSpeed : 1f;
            var beforeHit = BashWatch.DebugHitEvents == events;
            var at = Time.time - t0;
            TowerSync.Deactivate(false);
            off = true;
            var restored = p.m_animator.speed;
            c.Check(midStarted && beforeHit && p.m_currentAttack == null && Near(slowed, raw * factor, 0.02f) && Near(restored, raw, 0.02f),
                $"turned off mid-swing ({F(at)} s after the press, before the hit {beforeHit}): animator speed {F(slowed)} (clip's own {F(raw)} x {F(factor)}) "
                + $"back to {F(restored)} at once; current attack {(p.m_currentAttack == null ? "none" : "still set")}");
            yield return new WaitForSeconds(1.5f);
            c.Check(Near(troll.GetHealth(), hp) && BashWatch.DebugHitEvents == events && Near(p.m_animator.speed, 1f, 0.01f),
                $"1.5 s later: no bash hit (Troll health {F(hp)} -> {F(troll.GetHealth())}), animator speed {F(p.m_animator.speed)}");
            TowerSync.Activate();
            off = false;
            c.Report();
        }
        finally
        {
            if (off && _registered)
            {
                TowerSync.Activate();
            }
            rig.Done();
        }
    }
#endif
}
