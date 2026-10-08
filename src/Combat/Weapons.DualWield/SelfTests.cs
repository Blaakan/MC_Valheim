using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BepInEx.Bootstrap;
using MC.Shared;
using UnityEngine;
#endif

namespace MC.Combat.WeaponsDualWieldMod;

// Debug build only (calls vanish in Release). In-world self tests, run by world probe (tools/Test-InWorld.ps1,
// -Mod Weapons.DualWield). Design 7.5:
//   dual.data     rules on the wire, rule pick while waiting for the server, join check verdicts, other dual wield mods
//                 by name, hand table per trigger, equip rows (pure); every prefab and animator trigger me rely on;
//                 templates (values, triggers, stamina, fallback); pairing rules over every ObjectDB item; R23 fields
//   dual.equip    rows of 2.2 through vanilla EquipItem / UseItem: pair, replace off hand, main-hand key and its
//                 windows, shield, torch, spear, knives, butcher knife, swap (queued vanilla equip: action, refused
//                 attack, dodge, sprint and hide cancel, timing, queue pause, one attack press during a swap; vanilla
//                 hotbar equip sampled next to it), lone off hand, broken weapon, forced exception in the apply step
//   dual.keep     pair survive hide/draw, eating (+ dodge, + full hide, + second eat), load order, drags, unequip
//                 orders, radial hammer, death unequip, stale marker, rules change
//   dual.attack   real swings on a Troll (AI off): triggers, hands, wear, skills, stamina; knife moves; swap restart
//                 the combo, swap refused while swing start; Club special refused until swapped; moves of a non-dual
//                 item (PairMoves = SwordIron)
//   dual.damage   OffHandDamage / BothHandsDamage on real hits (skill 100), compared without vanilla's random skill
//                 factor and multi-object split (both recorded per hit); BothHands pattern, adrenaline and snow
//                 shovel once per event
//   dual.fields   off-hand hits carry the off-hand weapon's own fields (Blood, Lightning), clone put back after
//   dual.visuals  ZDO left item and stance, off-hand trails (none on main weapon own special), notes for R6-R8,
//                 screenshots (R5, R10); sheathed pairs by weapon kind: two swords and sword + axe crossed in an X on
//                 the back (X along the body, sword angle kept, level), two knives one per hip (main one at the game's
//                 spot, off-hand one its mirror image across the hips' centre plane; both ride the pelvis: each on its
//                 own side of the pelvis and of the body and outside its own thigh, idle and while walking, jogging
//                 and sprinting in place), knife + sword in either hand each at the game's own spot; setting off =
//                 game's own pose, one sheathed weapon untouched (R26), screenshots from behind and from the side
//   dual.block    knife pair block (G12): formula (pure), then real BlockAttack calls on the local player: two knives
//                 (same and mixed) block and parry like Skoll and Hati scaled to their damage, KnifePairBlock 0 and
//                 200, knife + sword in either hand and a lone knife as vanilla, durability drain, knife values back
// Me force rules only through ServerRules.TestRules, keys only through Controls.TestMainHandHeld, trails through
// LeftTrails.TestLeftHandTrails, crossed pair through BackCross.TestEnabled: never the config. Every test give its own items, spawn its own Troll, and take all
// back in finally (drags leave clones: me remove every item that was not there before), skills and food put back.
// More tests live in the other parts of this class (same helpers):
//   SelfTests.Hands.cs    dual.pair, dual.swap, dual.hammer, dual.toggle
//   SelfTests.Combat.cs   dual.moves, dual.elements, dual.stamina, dual.trees, dual.parry, dual.wounded
//   SelfTests.World.cs    dual.places, dual.sheath, dual.others, dual.log
//   SelfTests.Mp.cs       dual.mp.* (client joined to a dedicated server, tools/Test-Multiplayer.ps1) and their
//                         server halves
internal static partial class SelfTests
{
    private const string DataName = "dual.data";
    private const string EquipName = "dual.equip";
    private const string KeepName = "dual.keep";
    private const string AttackName = "dual.attack";
    private const string DamageName = "dual.damage";
    private const string FieldsName = "dual.fields";
    private const string VisualsName = "dual.visuals";
    private const string BlockName = "dual.block";

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        SelfTest.Register(DataName, RunData);
        SelfTest.Register(EquipName, RunEquip);
        SelfTest.Register(KeepName, RunKeep);
        SelfTest.Register(AttackName, RunAttack);
        SelfTest.Register(DamageName, RunDamage);
        SelfTest.Register(FieldsName, RunFields);
        SelfTest.Register(VisualsName, RunVisuals);
        SelfTest.Register(BlockName, RunBlock);
        RegisterMore();
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        SelfTest.Unregister(DataName);
        SelfTest.Unregister(EquipName);
        SelfTest.Unregister(KeepName);
        SelfTest.Unregister(AttackName);
        SelfTest.Unregister(DamageName);
        SelfTest.Unregister(FieldsName);
        SelfTest.Unregister(VisualsName);
        SelfTest.Unregister(BlockName);
        UnregisterMore();
#endif
    }

#if DEBUG
    // Spawn names (design section 8 Setup, checked in the runtime dump). dual.data check them all at runtime.
    private const string Sword = "SwordIron";
    private const string Axe = "AxeIron";
    private const string Mace = "MaceIron";
    private const string ClubName = "Club";
    private const string KnifeBlack = "KnifeBlackMetal";
    private const string KnifeFlintName = "KnifeFlint";
    private const string Butcher = "KnifeButcher";
    private const string Spear = "SpearFlint";
    private const string Buckler = "ShieldBronzeBuckler";
    private const string TorchName = "Torch";
    private const string HammerName = "Hammer";
    private const string Berry = "Raspberry";
    private const string Niedhogg = "SwordNiedhogg";
    private const string NiedhoggBlood = "SwordNiedhoggBlood";
    private const string NiedhoggLightning = "SwordNiedhoggLightning";
    private const string TrollName = "Troll";

    private static readonly string[] ItemNames =
    {
        DualRules.DefaultPairMoves, DualRules.DefaultKnifePairMoves, Sword, Axe, Mace, ClubName, KnifeBlack,
        KnifeFlintName, Butcher, Spear, Buckler, TorchName, HammerName, "BombSmoke", Berry, "SwordMistwalker",
        "AxeJotunBane", Niedhogg, NiedhoggBlood, NiedhoggLightning, "AxeBronze", "FW_AxeBronze", "CrossbowArbalest",
        "ShieldIronTower", "MeatPlatter", "SerpentStew", "HoneyGlazedChicken",
    };

    private static readonly string[] CreatureNames = { TrollName, "Greyling" };

    // Every trigger the default templates fire, plus the vanilla ones the tests use.
    private static readonly string[] Triggers =
    {
        "dualaxes0", "dualaxes1", "dualaxes2", "dualaxes3", "dualaxes_secondary", "dual_knives0", "dual_knives1",
        "dual_knives2", "dual_knives_secondary", "eat", "dodge",
    };

    // Primary animations of the one-handed swords, axes, clubs, maces and knives (runtime dump, design 1.6).
    private static readonly string[] MeleeAnimations = { "swing_longsword", "swing_axe", "knife_stab" };

    // Gap between the player centre and the target body surface (Spawn add the collider radius).
    private const float DummyGap = 1f;

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

    // ---------- shared helpers ----------

    // Built-in defaults, few values changed (test rules, never config).
    private static DualRules Rules(int offHand = DualRules.DefaultOffHandDamage, int swing = DualRules.DefaultSwingStamina,
        SecondaryMovesMode secondary = SecondaryMovesMode.PairMoves, HitPatternMode pattern = HitPatternMode.Alternate,
        int both = DualRules.DefaultBothHandsDamage, string pair = DualRules.DefaultPairMoves,
        string knife = DualRules.DefaultKnifePairMoves, string excluded = "",
        int knifeBlock = DualRules.DefaultKnifePairBlock)
    {
        return new DualRules(offHand, swing, secondary, pattern, both, knifeBlock, pair, knife, excluded);
    }

    private static ItemDrop.ItemData PrefabItem(string name)
    {
        var db = ObjectDB.instance;
        var prefab = db != null ? db.GetItemPrefab(name) : null;
        var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        return drop != null ? drop.m_itemData : null;
    }

    // Item made the way Inventory.AddItem(name) make it (load, pickup and crafting the same): Instantiate with ZNetView
    // init off, take its ItemData, destroy the object at end of frame. In a build its m_shared is its own copy.
    private static ItemDrop.ItemData InstantiatedItem(string name)
    {
        var db = ObjectDB.instance;
        var prefab = db != null ? db.GetItemPrefab(name) : null;
        if (prefab == null)
        {
            return null;
        }
        GameObject go;
        ZNetView.m_forceDisableInit = true;
        try
        {
            go = UnityEngine.Object.Instantiate(prefab);
        }
        finally
        {
            ZNetView.m_forceDisableInit = false;
        }
        var drop = go.GetComponent<ItemDrop>();
        var data = drop != null ? drop.m_itemData : null;
        UnityEngine.Object.Destroy(go);
        return data;
    }

    private static string Name(ItemDrop.ItemData item)
    {
        if (item == null)
        {
            return "nothing";
        }
        return item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared.m_name;
    }

    private static string HandsText(Player p) =>
        $"right {Name(p.m_rightItem)}, left {Name(p.m_leftItem)}, hidden right {Name(p.m_hiddenRightItem)}, "
        + $"hidden left {Name(p.m_hiddenLeftItem)}";

    private static bool Holds(Player p, ItemDrop.ItemData right, ItemDrop.ItemData left) =>
        ReferenceEquals(p.m_rightItem, right) && ReferenceEquals(p.m_leftItem, left);

    // Same weapon kind in each hand (drags re-add items as clones: same SharedData, new object).
    private static bool HoldsKinds(Player p, ItemDrop.ItemData right, ItemDrop.ItemData left) =>
        p.m_rightItem != null && p.m_leftItem != null && ReferenceEquals(p.m_rightItem.m_shared, right.m_shared)
        && ReferenceEquals(p.m_leftItem.m_shared, left.m_shared);

    private static int StateI(Player p) => p.m_animator != null ? p.m_animator.GetInteger("statei") : -1;

    private static bool Approx(float a, float b) => Mathf.Abs(a - b) < 0.001f;

    private static string F2(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    // Invariant of design 2.4, checked a frame after each step. Null = holds, else what broke.
    private static string Broken(Player p)
    {
        var inventory = p.GetInventory();
        var markedEquipped = 0;
        foreach (var item in inventory.GetAllItems())
        {
            var equipped = p.IsItemEquiped(item);
            if (item.m_equipped != equipped)
            {
                return $"{Name(item)} flagged equipped {item.m_equipped} but {(equipped ? "in" : "in no")} slot";
            }
            if (equipped && Hands.IsMarked(item))
            {
                markedEquipped++;
                if (!ReferenceEquals(item, p.m_leftItem))
                {
                    return $"{Name(item)} carries the off-hand marker outside the off hand";
                }
            }
        }
        if (markedEquipped > 1)
        {
            return $"{markedEquipped} equipped items carry the off-hand marker";
        }
        var slots = new[] { p.m_rightItem, p.m_leftItem, p.m_hiddenRightItem, p.m_hiddenLeftItem };
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null)
            {
                continue;
            }
            if (!inventory.ContainsItem(slots[i]))
            {
                return $"{Name(slots[i])} sits in a hand slot but is not in the inventory";
            }
            for (var j = i + 1; j < slots.Length; j++)
            {
                if (ReferenceEquals(slots[i], slots[j]))
                {
                    return $"{Name(slots[i])} sits in two hand slots";
                }
            }
        }
        if (p.m_rightItem == null && p.m_hiddenRightItem == null && Eligibility.IsOneHanded(p.m_leftItem))
        {
            return $"lone one-handed weapon {Name(p.m_leftItem)} in the left hand";
        }
        return null;
    }

    private static void Invariant(Checks c, Player p, string step)
    {
        var broken = Broken(p);
        c.Check(broken == null, $"{step}: invariant broken ({broken}; {HandsText(p)})");
    }

    private static Vector3 Forward(Player p)
    {
        var f = p.GetLookYaw() * Vector3.forward;
        f.y = 0f;
        if (f.sqrMagnitude < 0.01f)
        {
            f = p.transform.forward;
            f.y = 0f;
        }
        return f.normalized;
    }

    // Player look and face along dir: a swing at chain level 0 turn to the look yaw.
    private static void Face(Player p, Vector3 dir)
    {
        p.SetLookDir(dir);
        var rot = Quaternion.LookRotation(dir);
        p.transform.rotation = rot;
        if (p.m_body != null)
        {
            p.m_body.rotation = rot;
        }
    }

    private static void NoteDummy(string test, Dummy dummy)
    {
        if (dummy != null)
        {
            var line = dummy.Blocker == null
                ? $"clear line for every move (turned {dummy.Turn.ToString("0", CultureInfo.InvariantCulture)} degrees from the start facing)"
                : $"no direction with a clear line, placed straight ahead ({dummy.Blocker})";
            SelfTest.Note(test, $"target {dummy.Go.name} placed {F2(dummy.Distance)} m in front (collider radius {F2(dummy.Radius)} m), {line}");
        }
    }

    private static void Hold(Dummy dummy)
    {
        if (dummy != null)
        {
            dummy.Hold();
        }
    }

    // Test bench: items given and taken back (also clones from drags), hands as before, overrides cleared.
    private sealed class Bench
    {
        internal readonly Player Player;
        internal readonly Inventory Inventory;
        private readonly HashSet<ItemDrop.ItemData> _before;
        private readonly List<ItemDrop.ItemData> _equippedBefore = new List<ItemDrop.ItemData>();
        private readonly ItemDrop.ItemData _right;
        private readonly ItemDrop.ItemData _left;
        private readonly Dictionary<string, int> _stackCounts = new Dictionary<string, int>();
        private readonly List<GameObject> _spawned = new List<GameObject>();
        // Stacks GiveLinkedStack linked to the prefab's SharedData, with their own copy.
        private readonly List<KeyValuePair<ItemDrop.ItemData, ItemDrop.ItemData.SharedData>> _relinked =
            new List<KeyValuePair<ItemDrop.ItemData, ItemDrop.ItemData.SharedData>>();

        internal Bench(Player player)
        {
            Player = player;
            Inventory = player.GetInventory();
            _before = new HashSet<ItemDrop.ItemData>(Inventory.GetAllItems());
            _right = player.m_rightItem;
            _left = player.m_leftItem;
            foreach (var item in Inventory.GetAllItems())
            {
                if (player.IsItemEquiped(item) && !ReferenceEquals(item, _right) && !ReferenceEquals(item, _left))
                {
                    _equippedBefore.Add(item);
                }
            }
            // Built-in default rules, main-hand key released: a test never depend on the player's config or keyboard.
            ServerRules.TestRules = DualRules.Defaults;
            Controls.TestMainHandHeld = false;
            Empty();
        }

        internal int FreeSlots => Inventory.GetEmptySlots();

        // Object a test made itself (piece, tree, dropped item): TakeBack destroy it like the spawned creatures.
        internal void Track(GameObject go)
        {
            if (go != null)
            {
                _spawned.Add(go);
            }
        }

        // Prefab instance at a spot (piece, tree...), tracked. Null = no such prefab.
        internal GameObject SpawnAt(string prefab, Vector3 position, Quaternion rotation)
        {
            var scene = ZNetScene.instance;
            var prefabGo = scene != null ? scene.GetPrefab(prefab) : null;
            if (prefabGo == null)
            {
                return null;
            }
            var go = UnityEngine.Object.Instantiate(prefabGo, position, rotation);
            _spawned.Add(go);
            return go;
        }

        internal ItemDrop.ItemData Give(string prefab) => Inventory.AddItem(prefab, 1, 1, 0, 0L, "", false);

        // Stackable item (food): me remember count before, give back the inventory stack. Inventory.AddItem(name)
        // Instantiate the prefab: the item carry its own SharedData copy, as loaded, picked up and crafted items do.
        internal ItemDrop.ItemData GiveStack(string prefab, int amount)
        {
            var data = PrefabItem(prefab);
            if (data == null)
            {
                return null;
            }
            var name = data.m_shared.m_name;
            if (!_stackCounts.ContainsKey(name))
            {
                _stackCounts[name] = Inventory.CountItems(name, -1, false);
            }
            Inventory.AddItem(prefab, amount, 1, 0, 0L, "", false);
            return Stack(name);
        }

        // Food vanilla UseItem show in hand: Inventory.AddItem(GameObject, int) add an ItemData.Clone of the prefab's
        // (the prefab's SharedData object, as a chest's first loot), so ObjectDB.TryGetItemPrefab find it; AddItem(name)
        // Instantiate a copy it never find. Merged into an older stack (own copy) = me link that stack to the prefab's
        // SharedData (TakeBack put its own back).
        internal ItemDrop.ItemData GiveLinkedStack(string prefab, int amount)
        {
            var db = ObjectDB.instance;
            var go = db != null ? db.GetItemPrefab(prefab) : null;
            var drop = go != null ? go.GetComponent<ItemDrop>() : null;
            if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null)
            {
                return null;
            }
            var shared = drop.m_itemData.m_shared;
            var name = shared.m_name;
            if (!_stackCounts.ContainsKey(name))
            {
                _stackCounts[name] = Inventory.CountItems(name, -1, false);
            }
            if (!Inventory.AddItem(go, amount))
            {
                return null;
            }
            var stack = Stack(name);
            if (stack != null && !ReferenceEquals(stack.m_shared, shared))
            {
                _relinked.Add(new KeyValuePair<ItemDrop.ItemData, ItemDrop.ItemData.SharedData>(stack, stack.m_shared));
                stack.m_shared = shared;
            }
            return stack;
        }

        internal ItemDrop.ItemData Stack(string sharedName) =>
            Inventory.GetAllItems().FirstOrDefault(i => i.m_shared.m_name == sharedName);

        // Me empty both hands and both hidden hands through vanilla (items stay in inventory).
        internal void Empty()
        {
            var p = Player;
            if (p.m_rightItem != null)
            {
                p.UnequipItem(p.m_rightItem, false);
            }
            if (p.m_leftItem != null)
            {
                p.UnequipItem(p.m_leftItem, false);
            }
            if (p.m_hiddenRightItem != null)
            {
                p.UnequipItem(p.m_hiddenRightItem, false);
            }
            if (p.m_hiddenLeftItem != null)
            {
                p.UnequipItem(p.m_hiddenLeftItem, false);
            }
        }

        // Pair by two vanilla equips (main, then off). Me clear old markers first: marked item equipped alone, then
        // other one same frame = same-frame restore of design 2.4 (hands swap).
        internal void Pair(ItemDrop.ItemData main, ItemDrop.ItemData off)
        {
            Empty();
            Hands.SetMarked(main, false);
            Hands.SetMarked(off, false);
            Player.EquipItem(main);
            Player.EquipItem(off);
        }

        // Creature in front of the player, AI off, health so high it never dies. Player faces it. Its body surface
        // 'gap' metres from the player centre (collider radius read at runtime: a Troll is wide, too close it push
        // the player out of reach), inside every template's reach (knives 1.8 m, axes 2.2 m).
        // Direction: straight ahead if line clear (Blocker), else first clear turn (30, -30, 60, ... 180 degrees).
        // None clear = straight ahead as before, NoteDummy say what block it. Why: pair special (dualaxes_secondary,
        // ray width 0) = one thin ray per angle at 0.6 m, terrain and rocks in mask, stop at first thing it touch.
        // Rock or ground rise between = special never reach creature, while combo still reach it (its extra casts
        // at other heights see only characters). Probe world start next to the start temple's stones.
        internal Dummy Spawn(string prefab, float gap)
        {
            var scene = ZNetScene.instance;
            var prefabGo = scene != null ? scene.GetPrefab(prefab) : null;
            if (prefabGo == null)
            {
                return null;
            }
            var p = Player;
            var ahead = Forward(p);
            var go = UnityEngine.Object.Instantiate(prefabGo, p.transform.position + ahead * 3f,
                Quaternion.LookRotation(-ahead));
            _spawned.Add(go);
            var radius = 0.5f;
            var capsule = go.GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                var scale = go.transform.lossyScale;
                radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            }
            var distance = radius + gap;
            var dir = ahead;
            var turn = 0f;
            var pos = Vector3.zero;
            string blocked = null;
            for (var step = 0; step < 12; step++)
            {
                var angle = step == 0 ? 0f : (step + 1) / 2 * 30f * (step % 2 == 1 ? 1f : -1f);
                var candidate = Quaternion.Euler(0f, angle, 0f) * ahead;
                var blocker = Blocker(candidate, distance, radius, go, out var candidatePos);
                if (step == 0)
                {
                    pos = candidatePos;
                    blocked = blocker;
                }
                if (blocker == null)
                {
                    dir = candidate;
                    turn = angle;
                    pos = candidatePos;
                    blocked = null;
                    break;
                }
            }
            Face(p, dir);
            var rot = Quaternion.LookRotation(-dir);
            go.transform.SetPositionAndRotation(pos, rot);
            return new Dummy(go, pos, rot, radius, distance, turn, blocked);
        }

        // Clear line check. Ground under creature near the player's feet level; swing rays at the heights the
        // templates cast from (m_attackHeight above the feet: knife leap 0.5, pair special 0.6, combos 0.8), over the
        // angles where the creature is (asin radius / distance), up to its centre: no terrain, rock, tree or piece;
        // nothing solid inside its body. Player's and creature's own colliders skipped. Null = clear, else what block.
        private const float MaxGroundStep = 0.3f;
        private const float LineAngleStep = 5f;
        private static readonly float[] LineHeights = { 0.5f, 0.6f, 0.8f };
        private static readonly RaycastHit[] LineHits = new RaycastHit[32];
        private static readonly Collider[] BodyHits = new Collider[32];
        private static int _lineMask;
        private static int _bodyMask;

        private string Blocker(Vector3 dir, float distance, float radius, GameObject creature, out Vector3 pos)
        {
            if (_lineMask == 0)
            {
                // Vanilla melee mask with terrain (Attack.Start), less the character layers.
                _lineMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid",
                    "terrain", "vehicle");
                _bodyMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid",
                    "vehicle");
            }
            var p = Player;
            var feet = p.transform.position;
            pos = feet + dir * distance;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(pos, out var ground))
            {
                pos.y = ground;
            }
            if (Mathf.Abs(pos.y - feet.y) > MaxGroundStep)
            {
                return $"ground {F2(pos.y - feet.y)} m from the player's feet";
            }
            var halfAngle = Mathf.Asin(Mathf.Clamp01(radius / distance)) * Mathf.Rad2Deg;
            foreach (var height in LineHeights)
            {
                var origin = feet + Vector3.up * height;
                for (var angle = -halfAngle; angle <= halfAngle + 0.01f; angle += LineAngleStep)
                {
                    var ray = Quaternion.Euler(0f, angle, 0f) * dir;
                    var count = Physics.RaycastNonAlloc(origin, ray, LineHits, distance, _lineMask,
                        QueryTriggerInteraction.Ignore);
                    for (var i = 0; i < count; i++)
                    {
                        var hit = LineHits[i];
                        if (IsOwn(hit.collider, creature))
                        {
                            continue;
                        }
                        return $"{hit.collider.transform.root.name}/{hit.collider.name} "
                               + $"({LayerMask.LayerToName(hit.collider.gameObject.layer)}) {F2(hit.distance)} m away, "
                               + $"{F2(height)} m up, {angle.ToString("0", CultureInfo.InvariantCulture)} degrees";
                    }
                }
            }
            var bodies = Physics.OverlapSphereNonAlloc(pos + Vector3.up * (radius + MaxGroundStep), radius * 0.9f,
                BodyHits, _bodyMask, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < bodies; i++)
            {
                if (!IsOwn(BodyHits[i], creature))
                {
                    return $"{BodyHits[i].transform.root.name}/{BodyHits[i].name} inside the target's body";
                }
            }
            return null;
        }

        private bool IsOwn(Collider collider, GameObject creature)
        {
            var root = collider.transform.root;
            return root == Player.transform.root || root == creature.transform.root;
        }

        internal void TakeBack()
        {
            var p = Player;
            foreach (var go in _spawned)
            {
                if (go == null)
                {
                    continue;
                }
                var nview = go.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid() && ZNetScene.instance != null)
                {
                    ZNetScene.instance.Destroy(go);
                }
                else
                {
                    UnityEngine.Object.Destroy(go);
                }
            }
            _spawned.Clear();
            foreach (var pair in _relinked)
            {
                pair.Key.m_shared = pair.Value;
            }
            _relinked.Clear();
            if (p == null)
            {
                return;
            }
            // Swap still queued (test failed half way): out of vanilla's queue, else it equip later on its own.
            Hands.CancelSwap(p);
            // Everything that came during the test (drags re-add items as clones): out of the hands, then out.
            foreach (var item in Inventory.GetAllItems().Where(i => !_before.Contains(i)).ToList())
            {
                Hands.SetMarked(item, false);
                p.UnequipItem(item, false);
                Inventory.RemoveItem(item);
            }
            foreach (var pair in _stackCounts)
            {
                var extra = Inventory.CountItems(pair.Key, -1, false) - pair.Value;
                if (extra > 0)
                {
                    Inventory.RemoveItem(pair.Key, extra, -1, false);
                }
            }
            // Own items as before: hands (main, then off), then the rest.
            if (_right != null && Inventory.ContainsItem(_right) && !p.IsItemEquiped(_right))
            {
                p.EquipItem(_right, false);
            }
            if (_left != null && Inventory.ContainsItem(_left) && !p.IsItemEquiped(_left))
            {
                p.EquipItem(_left, false);
            }
            foreach (var item in _equippedBefore)
            {
                if (Inventory.ContainsItem(item) && !p.IsItemEquiped(item))
                {
                    p.EquipItem(item, false);
                }
            }
            foreach (var item in _before)
            {
                if (Inventory.ContainsItem(item))
                {
                    item.m_equipped = p.IsItemEquiped(item);
                }
            }
            p.m_queuedAttackTimer = 0f;
            p.m_queuedSecondAttackTimer = 0f;
        }

        // Every in-memory override back to normal (real config, real keyboard). Every finally call me.
        internal static void ClearOverrides()
        {
            ServerRules.TestRules = null;
            Controls.TestMainHandHeld = null;
            Controls.TestKeyHeld = null;
            Controls.TestKeyDown = null;
            if (Controls.TestMainKey.HasValue || Controls.TestSwapKey.HasValue)
            {
                Controls.TestMainKey = null;
                Controls.TestSwapKey = null;
                Controls.TestForgetWarned();
                Controls.CacheKeys();
            }
            LeftTrails.TestLeftHandTrails = null;
            Hands.TestThrowInApply = false;
            if (BackCross.TestEnabled.HasValue)
            {
                BackCross.TestEnabled = null;
                BackCross.RebuildAll();
            }
        }
    }

    // Creature target: AI off (never walks off, never hits back), held in place every frame, health topped up.
    private sealed class Dummy
    {
        internal readonly GameObject Go;
        internal readonly Character Character;
        internal readonly float Radius;
        internal readonly float Distance;
        internal readonly float Turn;       // degrees from the facing at spawn (clear line search)
        internal readonly string Blocker;   // null = clear line for every move, else what block it straight ahead
        private readonly Vector3 _pos;
        private readonly Quaternion _rot;
        private readonly Rigidbody _body;

        // False = Hold no longer top the health up (a test that let the creature die).
        internal bool Refill = true;

        internal Dummy(GameObject go, Vector3 pos, Quaternion rot, float radius, float distance, float turn,
            string blocker)
        {
            Go = go;
            _pos = pos;
            _rot = rot;
            Radius = radius;
            Distance = distance;
            Turn = turn;
            Blocker = blocker;
            Character = go.GetComponent<Character>();
            _body = go.GetComponent<Rigidbody>();
            foreach (var ai in go.GetComponents<BaseAI>())
            {
                ai.enabled = false;
            }
            if (Character != null)
            {
                Character.SetMaxHealth(100000f);
                Character.SetHealth(100000f);
            }
        }

        internal Vector3 Center => Go != null && Character != null ? Character.GetCenterPoint() : _pos;

        internal void Hold()
        {
            if (Go == null)
            {
                return;
            }
            Go.transform.SetPositionAndRotation(_pos, _rot);
            if (_body != null)
            {
                _body.position = _pos;
                _body.rotation = _rot;
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }
            if (Refill && Character != null && Character.GetHealth() < 50000f)
            {
                Character.SetHealth(100000f);
            }
        }
    }

    // Skills test raise or force: me put back in finally (skill that no exist before = removed again).
    private sealed class SkillSave
    {
        private readonly Skills _skills;
        private readonly Dictionary<Skills.SkillType, KeyValuePair<float, float>> _saved =
            new Dictionary<Skills.SkillType, KeyValuePair<float, float>>();
        private readonly List<Skills.SkillType> _absent = new List<Skills.SkillType>();

        internal SkillSave(Player p, params Skills.SkillType[] types)
        {
            _skills = p.GetSkills();
            foreach (var type in types)
            {
                if (_skills.m_skillData.TryGetValue(type, out var skill))
                {
                    _saved[type] = new KeyValuePair<float, float>(skill.m_level, skill.m_accumulator);
                }
                else
                {
                    _absent.Add(type);
                }
            }
        }

        // Level and progress in one number: go up with every raise (level-up reset accumulator).
        internal static float Value(Player p, Skills.SkillType type)
        {
            var skill = p.GetSkills().GetSkill(type);
            return skill.m_level * 1000f + skill.m_accumulator;
        }

        internal static void SetLevel(Player p, Skills.SkillType type, float level)
        {
            p.GetSkills().GetSkill(type).m_level = level;
        }

        internal void Restore()
        {
            foreach (var pair in _saved)
            {
                var skill = _skills.GetSkill(pair.Key);
                skill.m_level = pair.Value.Key;
                skill.m_accumulator = pair.Value.Value;
            }
            foreach (var type in _absent)
            {
                _skills.m_skillData.Remove(type);
            }
        }
    }

    // Screenshot at each hit event of the given triggers (R3), at most one per frame.
    private sealed class Shots
    {
        private readonly string _test;
        private readonly string[] _triggers;
        private readonly HashSet<string> _done = new HashSet<string>();
        private int _frame = -1;

        internal Shots(string test, params string[] triggers)
        {
            _test = test;
            _triggers = triggers;
        }

        internal void OnHit(DualSwing.HitRecord hit)
        {
            if (Array.IndexOf(_triggers, hit.Trigger) < 0 || Time.frameCount == _frame)
            {
                return;
            }
            var label = $"{hit.Trigger}-event{hit.Event}";
            if (!_done.Add(label))
            {
                return;
            }
            _frame = Time.frameCount;
            SelfTest.Screenshot(_test, label);
        }
    }

    // Me wait: no attack, dodge or equip running (or 'seconds' gone).
    private static IEnumerator WaitIdle(Player p, Dummy dummy = null, float seconds = 3f)
    {
        var until = Time.time + seconds;
        while (Time.time < until && (p.InAttack() || p.InDodge() || p.m_actionQueue.Count > 0
                                     || p.m_actionQueuePause > 0f
                                     || (p.m_currentAttack != null && !p.m_currentAttack.IsDone())))
        {
            Hold(dummy);
            yield return null;
        }
    }

    // Why the game refuse an attack right now: the gates of vanilla Humanoid.StartAttack in its own order (attack
    // running, dodge, cannot move, pushed, staggered, minor action), then no weapon, then stamina of Attack.Start.
    // Null = nothing stand in the way. Text also say what the body do (attached, emote, ground, clips), so a failed
    // start name its cause in the test line instead of "started False".
    private static string AttackGate(Player p)
    {
        var parts = new List<string>();
        if (p.InAttack() && !p.HaveQueuedChain())
        {
            parts.Add("an attack is running");
        }
        if (p.InDodge())
        {
            parts.Add("in a dodge");
        }
        if (!p.CanMove())
        {
            // Player.CanMove: teleporting, cutscene (intro, sleeping), over the carry weight with no stamina; then
            // Character.CanMove: staggering, or the animator state carry the tag freeze or sitting.
            parts.Add($"cannot move (teleporting {p.IsTeleporting()}, cutscene {p.InCutscene()}, sleeping {p.IsSleeping()}, "
                      + $"over the carry weight {p.IsEncumbered()}; else the animation state is tagged freeze or sitting)");
        }
        if (p.IsKnockedBack())
        {
            parts.Add("pushed back");
        }
        if (p.IsStaggering())
        {
            parts.Add("staggering");
        }
        if (p.InMinorAction())
        {
            parts.Add("in a minor action (equip, eat or reload animation)");
        }
        var weapon = p.GetCurrentWeapon();
        if (weapon == null)
        {
            parts.Add("no weapon");
        }
        else if (weapon.m_shared.m_attack != null && weapon.m_shared.m_attack.m_attackStamina > 0f
                 && !p.HaveStamina(weapon.m_shared.m_attack.m_attackStamina + 0.1f))
        {
            parts.Add($"stamina {F2(p.GetStamina())} of {F2(p.GetMaxStamina())}, the attack takes up to "
                      + F2(weapon.m_shared.m_attack.m_attackStamina));
        }
        if (parts.Count == 0)
        {
            return null;
        }
        return string.Join(", ", parts.ToArray()) + $"; attached {p.IsAttached()}, emote {p.InEmote()}, on the ground "
               + $"{p.IsOnGround()}, clips {ClipsNow(p)}";
    }

    // Clips the animator play now, per layer ("0:IdleTweaked | 1:-"), and the next one while in a transition.
    private static string ClipsNow(Player p)
    {
        var animator = p.m_animator;
        if (animator == null)
        {
            return "?";
        }
        var sb = new StringBuilder();
        for (var layer = 0; layer < animator.layerCount; layer++)
        {
            if (layer > 0)
            {
                sb.Append(" | ");
            }
            sb.Append(layer).Append(':').Append(ClipNames(animator.GetCurrentAnimatorClipInfo(layer)));
            if (animator.IsInTransition(layer))
            {
                sb.Append('>').Append(ClipNames(animator.GetNextAnimatorClipInfo(layer)));
            }
        }
        return sb.ToString();
    }

    private static string ClipNames(AnimatorClipInfo[] infos)
    {
        if (infos == null || infos.Length == 0)
        {
            return "-";
        }
        return string.Join("+", infos.Select(i => i.clip != null ? i.clip.name : "?").ToArray());
    }

    // Me wait: the game would start an attack now (or 'seconds' gone). Player still attached or in an emote from a
    // step before = me end that first, as a movement key does (Player.SetControls). Result in _attackGate: null =
    // ready, else what still refuse (AttackGate).
    private static string _attackGate;

    private static IEnumerator WaitCanAttack(Player p, float seconds = 5f)
    {
        var until = Time.time + seconds;
        if (p.IsAttached() || p.InEmote())
        {
            p.StopEmote();
            p.AttachStop();
            yield return null;
        }
        _attackGate = AttackGate(p);
        while (_attackGate != null && Time.time < until)
        {
            yield return null;
            _attackGate = AttackGate(p);
        }
    }

    // End of a test that sat, lay down, ate or used another mod's item: NOTE when the player cannot attack after it
    // (waited 'seconds' first), so the next test's failure point at the test that left it so.
    private static IEnumerator NoteIfStuck(string test, Player p, string after, float seconds = 3f)
    {
        yield return WaitCanAttack(p, seconds);
        if (_attackGate != null)
        {
            SelfTest.Note(test, $"left the player unable to attack after {after}: {_attackGate}");
        }
    }

    // Me wait: queued equip (hotbar path) done.
    private static IEnumerator WaitEquipped(Player p, ItemDrop.ItemData item)
    {
        var until = Time.time + 3f;
        while (Time.time < until && !p.IsItemEquiped(item))
        {
            yield return null;
        }
        yield return WaitIdle(p);
        yield return null;
    }

    // Attack button the way vanilla read it: queued attack timer kept above 0 until 'count' converted swings started
    // (vanilla PlayerAttackInput start them and chain them through HaveQueuedChain), then the last one run to its end.
    // settle = false: return the moment the attack is done (next Swing can still continue the combo, 0.2 s).
    // onFrame: called every frame while the swings run (a test sampling something the swing switches).
    private static IEnumerator Swing(Player p, Dummy dummy, int count, bool secondary, bool settle = true,
        Action<DualSwing.HitRecord> onHit = null, Action onFrame = null)
    {
        yield return WaitIdle(p, dummy);
        var until = Time.time + 3f;
        while (Time.time < until && p.InMinorAction())
        {
            Hold(dummy);
            yield return null;
        }
        var target = DualSwing.Swings.Count + count;
        var seen = DualSwing.Hits.Count;
        until = Time.time + 2.5f * count + 2f;
        while (DualSwing.Swings.Count < target && Time.time < until)
        {
            if (secondary)
            {
                p.m_queuedSecondAttackTimer = 0.5f;
            }
            else
            {
                p.m_queuedAttackTimer = 0.5f;
            }
            Hold(dummy);
            seen = Watch(seen, onHit);
            onFrame?.Invoke();
            yield return null;
        }
        p.m_queuedAttackTimer = 0f;
        p.m_queuedSecondAttackTimer = 0f;
        until = Time.time + 4f;
        while (Time.time < until && (p.InAttack() || (p.m_currentAttack != null && !p.m_currentAttack.IsDone())))
        {
            Hold(dummy);
            seen = Watch(seen, onHit);
            onFrame?.Invoke();
            yield return null;
        }
        if (!settle)
        {
            yield break;
        }
        for (var i = 0; i < 15; i++)
        {
            Hold(dummy);
            seen = Watch(seen, onHit);
            onFrame?.Invoke();
            yield return null;
        }
    }

    private static int Watch(int seen, Action<DualSwing.HitRecord> onHit)
    {
        var hits = DualSwing.Hits;
        if (onHit != null)
        {
            for (var i = seen; i < hits.Count; i++)
            {
                onHit(hits[i]);
            }
        }
        return hits.Count;
    }

    // Hands of one recorded swing: "0M,1O" = event 0 main, event 1 off; a both-hands event reads "0M,0O".
    private static string HandsOf(int swing)
    {
        var sb = new StringBuilder();
        foreach (var hit in DualSwing.Hits)
        {
            if (hit.Swing != swing)
            {
                continue;
            }
            if (sb.Length > 0)
            {
                sb.Append(',');
            }
            sb.Append(hit.Event).Append(hit.Hand == Hand.Off ? 'O' : 'M');
        }
        return sb.ToString();
    }

    // Swings recorded from index 'from': "trigger:hands" each, in order.
    private static void CheckSwings(Checks c, int from, string what, params string[] expected)
    {
        var got = new List<string>();
        for (var i = from; i < DualSwing.Swings.Count; i++)
        {
            got.Add(DualSwing.Swings[i].Trigger + ":" + HandsOf(i));
        }
        var want = string.Join(" | ", expected);
        var have = string.Join(" | ", got.ToArray());
        c.Check(have == want, $"{what}: expected {want}, got {(got.Count == 0 ? "no converted swing" : have)}");
    }

    private static void CheckTriggers(Checks c, int from, string what, params string[] expected)
    {
        var got = new List<string>();
        for (var i = from; i < DualSwing.Swings.Count; i++)
        {
            got.Add(DualSwing.Swings[i].Trigger);
        }
        var want = string.Join(", ", expected);
        var have = string.Join(", ", got.ToArray());
        c.Check(have == want, $"{what}: expected {want}, got {(got.Count == 0 ? "no converted swing" : have)}");
    }

    private static void CheckStamina(Checks c, int from, float expected, string what)
    {
        var got = new List<string>();
        var ok = from < DualSwing.Swings.Count;
        for (var i = from; i < DualSwing.Swings.Count; i++)
        {
            got.Add(F2(DualSwing.Swings[i].Stamina));
            ok &= Approx(DualSwing.Swings[i].Stamina, expected);
        }
        c.Check(ok, $"{what}: clone stamina {F2(expected)} expected, got {string.Join(", ", got.ToArray())}");
    }

    // Every hit of the swings from 'from' struck by the right weapon.
    private static void CheckWeapons(Checks c, int from, ItemDrop.ItemData main, ItemDrop.ItemData off, string what)
    {
        var wrong = DualSwing.Hits.Where(h => h.Swing >= from
                                              && !ReferenceEquals(h.Weapon, h.Hand == Hand.Off ? off : main))
            .Select(h => $"{h.Trigger} event {h.Event} {h.Hand} struck with {Name(h.Weapon)}").ToArray();
        c.Check(wrong.Length == 0, $"{what}: each hit struck by its hand's weapon ({string.Join("; ", wrong)})");
    }

    private static int SwingIndex(int from, string trigger)
    {
        for (var i = from; i < DualSwing.Swings.Count; i++)
        {
            if (DualSwing.Swings[i].Trigger == trigger)
            {
                return i;
            }
        }
        return -1;
    }

    private static DualSwing.HitRecord HitOf(int swing, int eventIndex, Hand hand) =>
        DualSwing.Hits.FirstOrDefault(h => h.Swing == swing && h.Event == eventIndex && h.Hand == hand);

    private static DualSwing.DamageRecord DamageOf(int swing, int eventIndex, Hand hand) =>
        DualSwing.Damages.FirstOrDefault(d => d.Swing == swing && d.Event == eventIndex && d.Hand == hand);

    // One recorded hit for a NOTE: raw damage, vanilla's roll and multi-object split, damage without both.
    private static string DamageText(DualSwing.DamageRecord d)
    {
        if (d == null)
        {
            return "no hit recorded";
        }
        return $"{F2(d.Total)} (skill factor {F2(d.SkillFactor)}, {d.Objects} object(s) hit: {d.ObjectNames}; split "
               + $"{F2(d.Split)}; {F2(d.Base)} without the roll and the split)";
    }

    // Ratio of two recorded hits without vanilla's random skill factor and multi-object split (deterministic).
    // -1 = a hit, its roll or its objects not recorded.
    private static float BaseRatio(DualSwing.DamageRecord part, DualSwing.DamageRecord whole) =>
        part != null && whole != null && part.Base >= 0f && whole.Base > 0f ? part.Base / whole.Base : -1f;

    // Both hits of a ratio carry vanilla's roll (a plausible one: 0.85-1 at skill 100) and their objects.
    private static bool Rolled(DualSwing.DamageRecord d) =>
        d != null && d.SkillFactor >= 0.85f - 0.001f && d.SkillFactor <= 1f + 0.001f && d.Objects > 0;

    // Damage ratio with the roll and the split taken out: only the multipliers differ, so exact but for float error.
    private const float RatioTolerance = 0.01f;

    // R2 clips, R4 hand distances, R13 times, R19 events per swing: NOTE lines for the swings from 'from'.
    private static void NoteSwings(string test, int from, Dummy dummy)
    {
        var swings = DualSwing.Swings;
        if (from >= swings.Count)
        {
            return;
        }
        var t0 = swings[from].Time;
        var sb = new StringBuilder();
        for (var i = from; i < swings.Count; i++)
        {
            var s = swings[i];
            var swing = i;
            var events = DualSwing.Hits.Where(h => h.Swing == swing).Select(h => h.Event).Distinct().Count();
            sb.Append(s.Trigger).Append(" at +").Append(F2(s.Time - t0)).Append(" s, ").Append(events)
                .Append(" event(s), stamina ").Append(F2(s.Stamina)).Append("; ");
        }
        SelfTest.Note(test, "swings (R13 times, R19 events per swing): " + sb);
        var center = dummy != null ? dummy.Center : (Vector3?)null;
        foreach (var hit in DualSwing.Hits)
        {
            if (hit.Swing < from)
            {
                continue;
            }
            var line = new StringBuilder();
            line.Append(hit.Trigger).Append(" event ").Append(hit.Event).Append(' ').Append(hit.Hand).Append(" at +")
                .Append(F2(hit.Time - t0)).Append(" s");
            if (center.HasValue)
            {
                line.Append(", left hand ").Append(F2(Vector3.Distance(hit.LeftHand, center.Value)))
                    .Append(" m / right hand ").Append(F2(Vector3.Distance(hit.RightHand, center.Value)))
                    .Append(" m from the target centre");
            }
            line.Append("; clips ").Append(hit.Clips);
            SelfTest.Note(test, "hit (R2 clips, R4 hands): " + line);
        }
    }

    // ---------- dual.data ----------

    private static IEnumerator RunData()
    {
        var c = new Checks(DataName);
        try
        {
            ServerRules.TestRules = DualRules.Defaults;
            CheckRules(c);
            CheckOwnRules(c);
            CheckJoin(c);
            CheckForeignMods(c);
            CheckHandTable(c);
            CheckRoute(c);
            CheckNames(c);
            var player = Player.m_localPlayer;
            c.Check(player != null, "local player exists");
            if (player != null)
            {
                CheckTemplates(c, player);
            }
            CheckEligibility(c);
            c.Report();
        }
        finally
        {
            Bench.ClearOverrides();
        }
        yield break;
    }

    // Every prefab the tests and TESTING.md spawn, every creature target.
    private static void CheckNames(Checks c)
    {
        var scene = ZNetScene.instance;
        var db = ObjectDB.instance;
        c.Check(scene != null && db != null, "ZNetScene and ObjectDB exist");
        if (scene == null || db == null)
        {
            return;
        }
        var missing = ItemNames.Where(n => scene.GetPrefab(n) == null || db.GetItemPrefab(n) == null).ToArray();
        c.Check(missing.Length == 0, $"item prefabs exist (missing: {string.Join(", ", missing)})");
        var missingCreatures = CreatureNames
            .Where(n => scene.GetPrefab(n) == null || scene.GetPrefab(n).GetComponent<Character>() == null).ToArray();
        c.Check(missingCreatures.Length == 0, $"creature prefabs exist (missing: {string.Join(", ", missingCreatures)})");
        var berry = PrefabItem(Berry);
        if (berry != null)
        {
            SelfTest.Note(DataName, $"{Berry}: eat time {F2(berry.m_shared.m_foodEatAnimTime)} s (dual.keep waits for it)");
        }
        NoteFeasts(scene);
    }

    // Eat window in a real game (design 1.2): Player.Interact on a feast call DoInteractAnimation, which show the food
    // and hide the main weapon only when the feast object carry a Consumable ItemDrop that is a piece (ItemDrop.IsPiece:
    // a Piece and a WearNTear; a Rigidbody go away when placed, MakePiece). Prefab data: me read every Feast prefab.
    private static void NoteFeasts(ZNetScene scene)
    {
        var parts = new List<string>();
        foreach (var prefab in scene.m_prefabs)
        {
            if (prefab == null || prefab.GetComponent<Feast>() == null)
            {
                continue;
            }
            var drop = prefab.GetComponent<ItemDrop>();
            var shared = drop != null && drop.m_itemData != null ? drop.m_itemData.m_shared : null;
            var hides = shared != null && shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable
                        && prefab.GetComponent<Piece>() != null && prefab.GetComponent<WearNTear>() != null;
            parts.Add(hides ? $"{prefab.name} hides it ({F2(shared.m_foodEatAnimTime)} s)" : $"{prefab.name} does not");
        }
        SelfTest.Note(DataName, parts.Count == 0
            ? "no Feast prefab found"
            : $"eating at a feast hides the main weapon (DoInteractAnimation conditions): {string.Join(", ", parts.ToArray())}");
    }

    // Template items (design 1.5 dump values), every trigger they fire, templates as the mod builds them, stamina
    // formula (2.6), fallback of a bad setting (2.5).
    private static void CheckTemplates(Checks c, Player player)
    {
        var axes = PrefabItem(DualRules.DefaultPairMoves);
        var knives = PrefabItem(DualRules.DefaultKnifePairMoves);
        c.Check(axes != null && knives != null, "AxeBerzerkr and KnifeSkollAndHati exist");
        if (axes != null)
        {
            var a = axes.m_shared.m_attack;
            var s = axes.m_shared.m_secondaryAttack;
            c.Check(a.m_attackAnimation == "dualaxes" && a.m_attackChainLevels == 4 && Approx(a.m_attackStamina, 16f)
                    && a.m_resetChainIfHit == DestructibleType.Tree && a.m_attackType == Attack.AttackType.Horizontal,
                $"AxeBerzerkr combo: dualaxes, chain 4, stamina 16, chain reset on trees (got {a.m_attackAnimation}, "
                + $"chain {a.m_attackChainLevels}, stamina {F2(a.m_attackStamina)}, reset {a.m_resetChainIfHit})");
            c.Check(s.m_attackAnimation == "dualaxes_secondary" && s.m_attackChainLevels <= 1 && Approx(s.m_attackStamina, 32f),
                $"AxeBerzerkr special: dualaxes_secondary, stamina 32 (got {s.m_attackAnimation}, {F2(s.m_attackStamina)})");
            c.Check(axes.m_shared.m_animationState == ItemDrop.ItemData.AnimationState.DualAxes
                    && (int)ItemDrop.ItemData.AnimationState.DualAxes == 15,
                $"AxeBerzerkr stance DualAxes (15), got {axes.m_shared.m_animationState}");
        }
        if (knives != null)
        {
            var a = knives.m_shared.m_attack;
            var s = knives.m_shared.m_secondaryAttack;
            c.Check(a.m_attackAnimation == "dual_knives" && a.m_attackChainLevels == 3 && Approx(a.m_attackStamina, 14f)
                    && a.m_resetChainIfHit == DestructibleType.None,
                $"KnifeSkollAndHati combo: dual_knives, chain 3, stamina 14, no chain reset (got {a.m_attackAnimation}, "
                + $"chain {a.m_attackChainLevels}, stamina {F2(a.m_attackStamina)}, reset {a.m_resetChainIfHit})");
            c.Check(s.m_attackAnimation == "dual_knives_secondary" && s.m_attackChainLevels <= 1 && Approx(s.m_attackStamina, 42f),
                $"KnifeSkollAndHati special: dual_knives_secondary, stamina 42 (got {s.m_attackAnimation}, {F2(s.m_attackStamina)})");
            c.Check(knives.m_shared.m_animationState == ItemDrop.ItemData.AnimationState.Knives
                    && (int)ItemDrop.ItemData.AnimationState.Knives == 11,
                $"KnifeSkollAndHati stance Knives (11), got {knives.m_shared.m_animationState}");
        }

        var zanim = player.m_zanim;
        var missing = Triggers.Where(t => !zanim.HasParameter(t, AnimatorControllerParameterType.Trigger)).ToArray();
        c.Check(missing.Length == 0, $"player animator triggers exist (missing: {string.Join(", ", missing)})");
        c.Check(zanim.HasParameter("statef", AnimatorControllerParameterType.Float)
                && zanim.HasParameter("statei", AnimatorControllerParameterType.Int)
                && zanim.HasParameter("blocking", AnimatorControllerParameterType.Bool),
            "player animator stance parameters statef (float), statei (int) and blocking (bool) exist");

        var sword = PrefabItem(Sword);
        var axe = PrefabItem(Axe);
        var knifeBlack = PrefabItem(KnifeBlack);
        var knifeFlint = PrefabItem(KnifeFlintName);
        if (sword == null || axe == null || knifeBlack == null || knifeFlint == null)
        {
            c.Check(false, "template checks need SwordIron, AxeIron, KnifeBlackMetal and KnifeFlint");
            return;
        }
        var rules = ServerRules.Current;
        var pair = MoveTemplates.For(sword, axe, rules, player);
        var knifePair = MoveTemplates.For(knifeBlack, knifeFlint, rules, player);
        c.Check(pair != null && pair.PrefabName == DualRules.DefaultPairMoves && pair.Stance == ItemDrop.ItemData.AnimationState.DualAxes
                && pair.Secondary != null && Approx(pair.SpecialRatio, 2f),
            $"sword + axe use the AxeBerzerkr moves (stance DualAxes, special ratio 2), got {(pair != null ? pair.Summary : "none")}");
        c.Check(knifePair != null && knifePair.PrefabName == DualRules.DefaultKnifePairMoves
                && knifePair.Stance == ItemDrop.ItemData.AnimationState.Knives && knifePair.Secondary != null
                && Approx(knifePair.SpecialRatio, 3f),
            $"two knives use the KnifeSkollAndHati moves (stance Knives, special ratio 3), got {(knifePair != null ? knifePair.Summary : "none")}");
        c.Check(ReferenceEquals(MoveTemplates.For(knifeBlack, sword, rules, player), pair),
            "a knife next to a sword uses the pair moves");
        // Knife pairs block like Skoll and Hati (G12): its block 24, parry x4, physical damage 45 + 45 (1.0.16 data).
        if (knifePair != null)
        {
            c.Check(Approx(knifePair.BlockPower, 24f) && Approx(knifePair.ParryBonus, 4f) && Approx(knifePair.PhysicalDamage, 90f),
                $"Skoll and Hati template: block 24, parry x4, physical damage 90 (got {F2(knifePair.BlockPower)}, "
                + $"x{F2(knifePair.ParryBonus)}, {F2(knifePair.PhysicalDamage)})");
        }
        if (pair != null && knifePair != null)
        {
            c.Check(Approx(MoveTemplates.Stamina(sword, axe, pair, false, rules), 10f)
                    && Approx(MoveTemplates.Stamina(sword, axe, pair, true, rules), 20f),
                "stamina SwordIron + AxeIron: 10 per swing, 20 for the special");
            c.Check(Approx(MoveTemplates.Stamina(knifeBlack, knifeFlint, knifePair, false, rules), 12f)
                    && Approx(MoveTemplates.Stamina(knifeFlint, knifeBlack, knifePair, true, rules), 36f),
                "stamina KnifeBlackMetal + KnifeFlint: 12 per swing, 36 for the leap, whichever hand holds which");
            c.Check(Approx(MoveTemplates.Stamina(knifeBlack, sword, pair, false, rules), 12f)
                    && Approx(MoveTemplates.Stamina(knifeFlint, sword, pair, false, rules), 10f),
                "stamina uses the higher cost: KnifeBlackMetal + SwordIron 12, KnifeFlint + SwordIron 10");
            c.Check(Approx(MoveTemplates.Stamina(sword, axe, pair, false, Rules(swing: 50)), 5f),
                "SwingStamina 50 halves the swing cost");
        }

        // Bad setting = default item, warning name the setting value.
        MoveTemplates.LastWarning = null;
        ServerRules.TestRules = Rules(pair: "NoSuchItem");
        var fallback = MoveTemplates.For(sword, axe, ServerRules.Current, player);
        c.Check(fallback != null && fallback.PrefabName == DualRules.DefaultPairMoves,
            $"PairMoves = NoSuchItem: the default AxeBerzerkr moves are used, got {(fallback != null ? fallback.PrefabName : "none")}");
        c.Check(MoveTemplates.LastWarning != null && MoveTemplates.LastWarning.Contains("NoSuchItem"),
            $"PairMoves = NoSuchItem: a warning names it (got '{MoveTemplates.LastWarning}')");
        ServerRules.TestRules = Rules(knife: Buckler);
        var knifeFallback = MoveTemplates.For(knifeBlack, knifeFlint, ServerRules.Current, player);
        c.Check(knifeFallback != null && knifeFallback.PrefabName == DualRules.DefaultKnifePairMoves,
            "KnifePairMoves = a shield (no melee swing): the default Skoll and Hati moves are used");
        ServerRules.TestRules = Rules(pair: DualRules.DefaultKnifePairMoves);
        var knifeMoves = MoveTemplates.For(sword, axe, ServerRules.Current, player);
        c.Check(knifeMoves != null && knifeMoves.PrefabName == DualRules.DefaultKnifePairMoves
                && knifeMoves.Stance == ItemDrop.ItemData.AnimationState.Knives,
            "PairMoves = KnifeSkollAndHati: a sword + axe pair uses the knife moves and stance");
        ServerRules.TestRules = DualRules.Defaults;
    }

    // Pairing rules (design 2.1) over every ObjectDB item; R23 and the per-weapon fields of 1.4 noted.
    private static void CheckEligibility(Checks c)
    {
        var db = ObjectDB.instance;
        if (db == null)
        {
            c.Check(false, "ObjectDB exists for the pairing checks");
            return;
        }
        ServerRules.TestRules = DualRules.Defaults;
        var eligibleNames = new List<string>();
        var wrong = new List<string>();
        var r23 = new List<string>();
        var fields = new List<string>();
        var otherCount = 0;
        var terrain = 0;
        var shovel = 0;
        foreach (var go in db.m_items)
        {
            var drop = go != null ? go.GetComponent<ItemDrop>() : null;
            if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null)
            {
                continue;
            }
            var shared = drop.m_itemData.m_shared;
            var eligible = Eligibility.IsEligible(drop.m_itemData);
            var playerItem = shared.m_name != null && shared.m_name.StartsWith("$item_", StringComparison.Ordinal);
            if (!playerItem)
            {
                if (eligible)
                {
                    otherCount++;
                }
                continue;
            }
            var skill = shared.m_skillType;
            var family = shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon
                         && (skill == Skills.SkillType.Swords || skill == Skills.SkillType.Axes
                             || skill == Skills.SkillType.Clubs || skill == Skills.SkillType.Knives);
            var expected = family && shared.m_attack != null
                                  && Array.IndexOf(MeleeAnimations, shared.m_attack.m_attackAnimation) >= 0
                                  && go.name != Butcher;
            if (eligible != expected)
            {
                wrong.Add($"{go.name} {(eligible ? "pairs" : "does not pair")}");
            }
            if (!eligible)
            {
                continue;
            }
            eligibleNames.Add(go.name);
            foreach (var a in new[] { shared.m_attack, shared.m_secondaryAttack })
            {
                if (a == null || string.IsNullOrEmpty(a.m_attackAnimation))
                {
                    continue;
                }
                if (a.m_spawnOnTrigger != null || a.m_harvest || a.m_attach || a.m_pickaxeSpecial)
                {
                    r23.Add($"{go.name} {a.m_attackAnimation}: spawnOnTrigger "
                            + $"{(a.m_spawnOnTrigger != null ? a.m_spawnOnTrigger.name : "none")}, harvest {a.m_harvest}, "
                            + $"attach {a.m_attach}, pickaxeSpecial {a.m_pickaxeSpecial}");
                }
            }
            terrain += shared.m_attack.m_hitTerrain ? 1 : 0;
            shovel += shared.m_attack.m_snowShovel ? 1 : 0;
            var parts = new List<string>();
            var p = shared.m_attack;
            if (p.m_damageMultiplierPerMissingHP > 0f)
            {
                parts.Add($"low-health bonus {p.m_damageMultiplierPerMissingHP}");
            }
            if (p.m_damageMultiplierByTotalHealthMissing > 0f)
            {
                parts.Add($"bonus by total health missing {p.m_damageMultiplierByTotalHealthMissing}");
            }
            if (p.m_spawnOnHit != null || p.m_spawnOnHitChance > 0f)
            {
                var secondaryChance = shared.m_secondaryAttack != null ? shared.m_secondaryAttack.m_spawnOnHitChance : 0f;
                parts.Add($"spawn on hit {(p.m_spawnOnHit != null ? p.m_spawnOnHit.name : "none")} "
                          + $"{F2(p.m_spawnOnHitChance)} (special {F2(secondaryChance)})");
            }
            if (p.m_resetChainIfHit != DestructibleType.None)
            {
                parts.Add($"chain reset {p.m_resetChainIfHit}");
            }
            if (p.m_specialHitSkill != Skills.SkillType.None)
            {
                parts.Add($"special hit {p.m_specialHitSkill} on {p.m_specialHitType}");
            }
            if (p.m_attackHealthReturnHit > 0f || p.m_attackEitrAdd > 0f || p.m_staminaReturnPerMissingHP > 0f)
            {
                parts.Add($"health return {p.m_attackHealthReturnHit}, eitr add {p.m_attackEitrAdd}, "
                          + $"stamina return {p.m_staminaReturnPerMissingHP}");
            }
            if (!Approx(p.m_raiseSkillAmount, 1f))
            {
                parts.Add($"skill raise {p.m_raiseSkillAmount}");
            }
            if (parts.Count > 0)
            {
                fields.Add($"{go.name}: {string.Join(", ", parts.ToArray())}");
            }
        }
        c.Check(wrong.Count == 0,
            $"every player sword, axe, club, mace and knife pairs except KnifeButcher, nothing else does (wrong: {string.Join(", ", wrong.ToArray())})");
        var never = new[] { Butcher, Spear, "SpearBronze", "Tankard", "TankardOdin", "BombSmoke", DualRules.DefaultPairMoves,
            DualRules.DefaultKnifePairMoves, Buckler, TorchName, HammerName };
        var pairing = never.Where(n => PrefabItem(n) != null && Eligibility.IsEligible(PrefabItem(n))).ToArray();
        c.Check(pairing.Length == 0, $"butcher knife, spears, tankards, bombs, dual items, shield, torch, hammer never pair (pairing: {string.Join(", ", pairing)})");
        var always = new[] { Sword, Axe, Mace, ClubName, KnifeBlack, KnifeFlintName, Niedhogg, NiedhoggBlood, "AxeBronze" };
        var notPairing = always.Where(n => PrefabItem(n) == null || !Eligibility.IsEligible(PrefabItem(n))).ToArray();
        c.Check(notPairing.Length == 0, $"test weapons pair (not pairing: {string.Join(", ", notPairing)})");
        SelfTest.Note(DataName, $"{eligibleNames.Count} player weapons pair (54 in the 1.0.16 dump): {string.Join(", ", eligibleNames.ToArray())}");
        SelfTest.Note(DataName, $"{otherCount} items without an $item_ name also pass the pairing rules (creature attacks, "
                                + "test items; 184 in the 1.0.16 dump, design D24)");
        SelfTest.Note(DataName, r23.Count == 0
            ? "R23 holds: no pairing weapon has spawnOnTrigger, harvest, attach or pickaxeSpecial on either attack"
            : $"R23: {r23.Count} attack(s) of pairing weapons have fields the off hand does not swap: {string.Join("; ", r23.ToArray())}");
        SelfTest.Note(DataName, $"pairing weapons with hitTerrain on the combo: {terrain} of {eligibleNames.Count}; "
                                + $"snowShovel: {shovel} (both stay the main weapon's during off-hand hits)");
        SelfTest.Note(DataName, $"per-weapon combo values (design 1.4; unlisted = defaults): {string.Join("; ", fields.ToArray())}");

        // ExcludedWeapons by prefab: AxeBronze out, FW_AxeBronze (same display name, own prefab) still pairs.
        ServerRules.TestRules = Rules(excluded: "AxeBronze, NoSuchSword");
        var bronze = PrefabItem("AxeBronze");
        var fw = PrefabItem("FW_AxeBronze");
        c.Check(bronze != null && fw != null && !Eligibility.IsEligible(bronze) && Eligibility.IsEligible(fw),
            "ExcludedWeapons = AxeBronze: AxeBronze never pairs, FW_AxeBronze (same display name) still does");
        // Items as a player holds them: Instantiate copies, each with its own SharedData copy (design 1.2).
        var bronzeCopy = InstantiatedItem("AxeBronze");
        var fwCopy = InstantiatedItem("FW_AxeBronze");
        SelfTest.Note(DataName, "an AxeBronze made like Inventory.AddItem(name) makes it shares the prefab's SharedData "
                                + $"object = {bronze != null && bronzeCopy != null && ReferenceEquals(bronze.m_shared, bronzeCopy.m_shared)}");
        c.Check(bronzeCopy != null && fwCopy != null && !Eligibility.IsEligible(bronzeCopy) && Eligibility.IsEligible(fwCopy),
            "ExcludedWeapons = AxeBronze on items made like the inventory makes them (own SharedData copy): AxeBronze "
            + "never pairs, FW_AxeBronze still does");
        ServerRules.TestRules = DualRules.Defaults;
        c.Check(bronze != null && Eligibility.IsEligible(bronze) && bronzeCopy != null && Eligibility.IsEligible(bronzeCopy),
            "ExcludedWeapons cleared: AxeBronze pairs again");
    }

    // Hand table of design 2.6 (pure): every known trigger, one-off Moveset names, unknown names (chain step rule and
    // single move rule), BothHands.
    private static void CheckHandTable(Checks c)
    {
        string Seq(Hand[] pattern, int events, bool both = false, int step = 0)
        {
            var parts = new string[events];
            for (var i = 0; i < events; i++)
            {
                parts[i] = DualSwing.HandAt(pattern, i, both, step).ToString();
            }
            return string.Join(",", parts);
        }

        string Unknown(string animation, int chainLevels, int level, int events)
        {
            return Seq(DualSwing.PatternFor(animation, chainLevels, level), events, false,
                DualSwing.ChainStep(chainLevels, level));
        }

        c.Check(Seq(DualSwing.PatternFor("dualaxes", 4, 0), 1) == "Main", "dualaxes0 = main");
        c.Check(Seq(DualSwing.PatternFor("dualaxes", 4, 1), 1) == "Off", "dualaxes1 = off");
        c.Check(Seq(DualSwing.PatternFor("dualaxes", 4, 2), 2) == "Main,Off", "dualaxes2 = main, off");
        c.Check(Seq(DualSwing.PatternFor("dualaxes", 4, 3), 2) == "Main,Off", "dualaxes3 = main, off");
        c.Check(Seq(DualSwing.PatternFor("dualaxes_secondary", 0, 0), 1) == "Both", "dualaxes_secondary = both");
        c.Check(Seq(DualSwing.PatternFor("dual_knives", 3, 0), 1) == "Main", "dual_knives0 = main");
        c.Check(Seq(DualSwing.PatternFor("dual_knives", 3, 1), 1) == "Off", "dual_knives1 = off");
        c.Check(Seq(DualSwing.PatternFor("dual_knives", 3, 2), 1) == "Both", "dual_knives2 = both");
        c.Check(Seq(DualSwing.PatternFor("dual_knives_secondary", 0, 0), 1) == "Both", "dual_knives_secondary = both");
        c.Check(Seq(DualSwing.PatternFor("dualaxes1", 0, 0), 1) == "Off", "one-off dualaxes1 (chain levels 0) = off");
        c.Check(Seq(DualSwing.PatternFor("dualaxes3", 0, 0), 2) == "Main,Off", "one-off dualaxes3 = main, off");
        // Unknown chain trigger (PairMoves = SwordIron): one hit event per swing, so the hand changes by chain step.
        var sword = $"{Unknown("swing_longsword", 3, 0, 1)} | {Unknown("swing_longsword", 3, 1, 1)} | "
                    + Unknown("swing_longsword", 3, 2, 1);
        c.Check(sword == "Main | Off | Main",
            $"unknown chain trigger alternates over the combo: swing_longsword0-2 = main, off, main (got {sword})");
        c.Check(Unknown("swing_longsword", 3, 1, 2) == "Off,Main",
            "unknown chain trigger with two events at step 1 = off, main");
        c.Check(DualSwing.ChainStep(3, 1) == 1 && DualSwing.ChainStep(0, 0) == -1 && DualSwing.ChainStep(1, 0) == -1,
            "chain step: the level for a chain move, -1 for a single move");
        c.Check(Unknown("sword_secondary", 0, 0, 2) == "Both,Both",
            "unknown single move (a non-dual special or one-off) = both on every event");
        c.Check(Seq(DualSwing.PatternFor("dualaxes", 4, 0), 3) == "Main,Main,Main",
            "events past the pattern end are main");
        c.Check(Seq(DualSwing.PatternFor("dualaxes", 4, 1), 2, both: true) == "Both,Both", "BothHands: every event both");
    }

    // Equip rows of design 2.2 (pure).
    private static void CheckRoute(Checks c)
    {
        RouteInput In(bool xEligible = true, bool rEligible = true, bool lNull = true, bool lEligible = false,
            bool rNull = false, bool candidate = false, bool marked = false, bool intent = false, bool torch = false)
        {
            return new RouteInput
            {
                Intent = intent,
                XEligible = xEligible,
                XTorch = torch,
                XMarked = marked,
                RNull = rNull,
                REligible = rEligible,
                RCandidate = candidate,
                LNull = lNull,
                LEligible = lEligible,
            };
        }

        c.Check(Hands.Route(In()) == EquipRow.OffHand, "second eligible weapon goes to the off hand");
        c.Check(Hands.Route(In(lNull: false, lEligible: true)) == EquipRow.OffHand, "replaces the off-hand weapon");
        c.Check(Hands.Route(In(intent: true)) == EquipRow.MainHandKey, "main-hand key = vanilla equip");
        c.Check(Hands.Route(In(candidate: true)) == EquipRow.Restore, "same-frame restore");
        c.Check(Hands.Route(In(candidate: true, marked: true)) == EquipRow.OffHand, "marked item never restores");
        c.Check(Hands.Route(In(rNull: true, rEligible: false, lNull: false, lEligible: true)) == EquipRow.MainKeepOff,
            "empty main hand next to a lone off-hand weapon: main hand, off hand kept");
        c.Check(Hands.Route(In(lNull: false, lEligible: false)) == EquipRow.Vanilla, "shield or torch in the off hand: vanilla");
        c.Check(Hands.Route(In(rNull: true, rEligible: false)) == EquipRow.Vanilla, "empty hands: vanilla");
        c.Check(Hands.Route(In(xEligible: false)) == EquipRow.Vanilla, "not pairable: vanilla");
        c.Check(Hands.Route(In(xEligible: false, torch: true, lNull: false, lEligible: true)) == EquipRow.Torch,
            "torch while paired");
        c.Check(Hands.Route(In(xEligible: false, torch: true)) == EquipRow.Vanilla, "torch next to one weapon: vanilla");
    }

    // Server rules: wire format, clamping, refusals, pending defaults.
    private static void CheckRules(Checks c)
    {
        var d = DualRules.Defaults;
        c.Check(d.OffHandDamage == 100 && d.SwingStamina == 100 && d.BothHandsDamage == 50 && d.KnifePairBlock == 100
                && d.HitPattern == HitPatternMode.Alternate && d.SecondaryMoves == SecondaryMovesMode.PairMoves
                && d.PairMoves == "AxeBerzerkr" && d.KnifePairMoves == "KnifeSkollAndHati" && d.ExcludedWeapons == "",
            "built-in defaults match the config defaults");

        var odd = new DualRules(55, 150, SecondaryMovesMode.MainWeapon, HitPatternMode.BothHands, 70, 35, "SwordIron",
            "KnifeFlint", "AxeBronze, SwordWood");
        var pkg = new ZPackage();
        odd.Write(pkg);
        pkg.SetPos(0);
        c.Check(DualRules.TryRead(pkg, out var back, out var clamped) && !clamped && back.SameAs(odd)
                && back.Describe() == odd.Describe(),
            "rules survive the wire unchanged (every field)");

        var wild = new DualRules(500, 5, SecondaryMovesMode.PairMoves, HitPatternMode.Alternate, 0, -5,
            new string('x', 1500), "", "");
        pkg = new ZPackage();
        wild.Write(pkg);
        pkg.SetPos(0);
        c.Check(DualRules.TryRead(pkg, out back, out clamped) && clamped && back.OffHandDamage == DualRules.MaxOffHandDamage
                && back.SwingStamina == DualRules.MinSwingStamina && back.BothHandsDamage == DualRules.MinBothHandsDamage
                && back.KnifePairBlock == DualRules.MinKnifePairBlock && back.PairMoves.Length == DualRules.MaxStringLength,
            "out-of-range rules from the wire are clamped, long strings cut");
        var strong = new DualRules(100, 100, SecondaryMovesMode.PairMoves, HitPatternMode.Alternate, 50, 900, "", "", "");
        pkg = new ZPackage();
        strong.Write(pkg);
        pkg.SetPos(0);
        c.Check(DualRules.TryRead(pkg, out back, out clamped) && clamped && back.KnifePairBlock == DualRules.MaxKnifePairBlock,
            "KnifePairBlock above the range from the wire is brought down to 200");

        pkg = new ZPackage();
        pkg.Write(DualRules.Layout);
        pkg.Write(100);
        pkg.Write(100);
        pkg.Write(7);   // unknown SecondaryMoves
        pkg.Write(-1);  // unknown HitPattern
        pkg.Write(50);
        pkg.Write(100); // KnifePairBlock
        pkg.Write("AxeBerzerkr");
        pkg.Write("KnifeSkollAndHati");
        pkg.Write("");
        pkg.SetPos(0);
        c.Check(DualRules.TryRead(pkg, out back, out clamped) && clamped
                && back.SecondaryMoves == SecondaryMovesMode.PairMoves && back.HitPattern == HitPatternMode.Alternate,
            "unknown enum values from the wire fall back to the defaults");

        pkg = new ZPackage();
        pkg.Write(DualRules.Layout + 1);
        pkg.SetPos(0);
        c.Check(!DualRules.TryRead(pkg, out _, out _), "unknown rules layout refused");

        pkg = new ZPackage();
        pkg.Write(DualRules.Layout);
        pkg.Write(100);
        pkg.SetPos(0);
        c.Check(!DualRules.TryRead(pkg, out _, out _), "rules with missing fields refused");

        pkg = new ZPackage();
        odd.Write(pkg);
        pkg.Write(42);
        pkg.SetPos(0);
        c.Check(!DualRules.TryRead(pkg, out _, out _), "rules with an extra field refused");

        c.Check(ServerRules.Receive(new ZPackage()) == false, "single player never takes rules from a peer");

        var own = new DualRules(20, 30, SecondaryMovesMode.MainWeapon, HitPatternMode.BothHands, 90, 0, "A", "B", "C");
        c.Check(ReferenceEquals(ServerRules.Pick(true, null, own), DualRules.Defaults),
            "client waiting for the server's rules uses the built-in defaults, never its own");
        c.Check(ReferenceEquals(ServerRules.Pick(true, odd, own), odd), "client with the server's rules uses them");
        c.Check(ReferenceEquals(ServerRules.Pick(false, odd, own), own), "not a client: own rules");
        c.Check(ReferenceEquals(ServerRules.Pick(false, null, own), own), "single player: own rules");
    }

    // Own settings -> rules, read only (a test never write the config): every setting land in its own field, and in
    // single player the rules in force are the player's own.
    private static void CheckOwnRules(Checks c)
    {
        var own = DualRules.Own();
        c.Check(own.OffHandDamage == Plugin.OffHandDamage.Value && own.SwingStamina == Plugin.SwingStamina.Value
                && own.SecondaryMoves == Plugin.SecondaryMoves.Value && own.HitPattern == Plugin.HitPattern.Value
                && own.BothHandsDamage == Plugin.BothHandsDamage.Value && own.KnifePairBlock == Plugin.KnifePairBlock.Value
                && own.PairMoves == (Plugin.PairMoves.Value ?? "").Trim()
                && own.KnifePairMoves == (Plugin.KnifePairMoves.Value ?? "").Trim()
                && own.ExcludedWeapons == (Plugin.ExcludedWeapons.Value ?? "").Trim(),
            $"the player's own settings make the own rules, each setting in its own field ({own.Describe()})");
        var net = ZNet.instance;
        if (net != null && net.IsServer())
        {
            var forced = ServerRules.TestRules;
            ServerRules.TestRules = null;
            var current = ServerRules.Current;
            var usingServer = ServerRules.UsingServer;
            ServerRules.TestRules = forced;
            c.Check(current.SameAs(own) && !usingServer,
                $"single player (or host): the rules in force are the player's own settings ({current.Describe()})");
        }
    }

    // Join check verdicts (framework side PeerCompatible / HelloState: its own tests N07, N08).
    private static void CheckJoin(Checks c)
    {
        // Wording of the two server log lines (TESTING.md M01, M02, M04), whoever the player and whatever the reason.
        c.Check(PlayerCheck.RefusedText("Odin", "has the mod turned off")
                == "Refused Odin: their game has the mod turned off. This server requires Dual Wielding on every player "
                + "(everyone fights with the same rules). Their game shows \"Incompatible version\". To let such players in, "
                + "set AllowPlayersWithoutMod = true.",
            $"server log line of a refusal ('{PlayerCheck.RefusedText("Odin", "has the mod turned off")}')");
        c.Check(PlayerCheck.AllowedText("Odin", "does not have the mod")
                == "Odin plays without Dual Wielding: their game does not have the mod. AllowPlayersWithoutMod is on, so they "
                + "may play, without this mod's rules (with another dual wield mod installed they may still dual wield, by "
                + "that mod's rules).",
            $"server log line for a player let in without the mod ('{PlayerCheck.AllowedText("Odin", "does not have the mod")}')");
        c.Check(PlayerCheck.Decide(true, true, true, false, true, false) == JoinVerdict.Compatible,
            "compatible player allowed");
        c.Check(PlayerCheck.Decide(true, true, true, false, false, false) == JoinVerdict.Refuse,
            "not compatible (no mod, turned off, other network version) refused with the setting off");
        c.Check(PlayerCheck.Decide(true, true, true, false, false, true) == JoinVerdict.Allowed,
            "not compatible allowed with AllowPlayersWithoutMod on");
        c.Check(PlayerCheck.Decide(false, true, true, false, false, false) == JoinVerdict.Skip
                && PlayerCheck.Decide(true, false, true, false, false, false) == JoinVerdict.Skip
                && PlayerCheck.Decide(true, true, false, false, false, false) == JoinVerdict.Skip
                && PlayerCheck.Decide(true, true, true, true, false, false) == JoinVerdict.Skip,
            "not the server, gone, not ready or already being kicked: skipped");
    }

    // Other dual wield mods: found by name, never us.
    private static void CheckForeignMods(Checks c)
    {
        c.Check(ForeignMods.Matches("DualWielder") && ForeignMods.Matches("DualWieldCore")
                && ForeignMods.Matches("balrond DualMastery"),
            "DualWielder, DualWieldCore and DualMastery are found by name");
        c.Check(!ForeignMods.Matches(ModInfo.Name) && !ForeignMods.Matches(ModInfo.Guid) && !ForeignMods.Matches(null),
            "our own name and GUID never match");
        var found = ForeignMods.Find();
        c.Check(found == null, $"no other dual wield mod in the test game (found {found})");
        var names = Chainloader.PluginInfos.Values.Where(i => i != null && i.Metadata != null)
            .Select(i => i.Metadata.Name).ToArray();
        SelfTest.Note(DataName, $"plugins scanned for other dual wield mods ({names.Length}): {string.Join(", ", names)}");
    }

    // ---------- dual.equip ----------

    private static IEnumerator RunEquip()
    {
        var c = new Checks(EquipName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(EquipName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        try
        {
            bench = new Bench(player);
            if (bench.FreeSlots < 9)
            {
                c.Check(false, $"needs 9 free inventory slots, has {bench.FreeSlots}");
                c.Report();
                yield break;
            }
            var sword = bench.Give(Sword);
            var axe = bench.Give(Axe);
            var mace = bench.Give(Mace);
            var knifeBlack = bench.Give(KnifeBlack);
            var knifeFlint = bench.Give(KnifeFlintName);
            var butcher = bench.Give(Butcher);
            var spear = bench.Give(Spear);
            var buckler = bench.Give(Buckler);
            var torch = bench.Give(TorchName);
            if (sword == null || axe == null || mace == null || knifeBlack == null || knifeFlint == null
                || butcher == null || spear == null || buckler == null || torch == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            yield return null;

            // T01: SwordIron, then AxeIron = pair.
            player.EquipItem(sword);
            player.EquipItem(axe);
            yield return null;
            c.Check(Holds(player, sword, axe), $"SwordIron then AxeIron: sword main, axe off ({HandsText(player)})");
            c.Check(Hands.IsMarked(axe) && !Hands.IsMarked(sword), "off-hand marker on the axe only");
            c.Check(Hands.IsPaired(player) && StateI(player) == 15, $"dual axe stance (statei 15), got {StateI(player)}");
            Invariant(c, player, "pair");
            yield return new WaitForSeconds(0.6f);
            SelfTest.Screenshot(EquipName, "sword-axe-idle");
            yield return null;
            yield return null;

            // T02: MaceIron replace the off-hand axe.
            player.EquipItem(mace);
            yield return null;
            c.Check(Holds(player, sword, mace) && !axe.m_equipped && !Hands.IsMarked(axe) && Hands.IsMarked(mace),
                $"MaceIron replaces the off-hand axe, marker moves to it ({HandsText(player)})");
            Invariant(c, player, "replace off hand");

            // T03: main-hand key through the hotbar path (ToggleEquipped queues the equip, intent kept for it).
            Controls.TestMainHandHeld = true;
            player.UseItem(null, knifeBlack, true);
            Controls.TestMainHandHeld = false;
            yield return WaitEquipped(player, knifeBlack);
            c.Check(Holds(player, knifeBlack, null) && !sword.m_equipped && !mace.m_equipped,
                $"main-hand key: KnifeBlackMetal alone in the main hand, sword and mace put away ({HandsText(player)})");
            Invariant(c, player, "main-hand key");
            player.UseItem(null, sword, true);
            yield return WaitEquipped(player, sword);
            c.Check(Holds(player, knifeBlack, sword) && Hands.IsMarked(sword),
                $"hotbar equip without the key pairs the sword into the off hand ({HandsText(player)})");
            Invariant(c, player, "hotbar pairing");

            // Intent windows: intent for item used only by that item own queued equip.
            bench.Pair(sword, axe);
            yield return null;
            Controls.TestMainHandHeld = true;
            player.UseItem(null, knifeFlint, true);
            Controls.TestMainHandHeld = false;
            var queued = player.IsEquipActionQueued(knifeFlint);
            player.RemoveEquipAction(knifeFlint);
            player.EquipItem(knifeFlint);
            yield return null;
            c.Check(queued && Holds(player, sword, knifeFlint),
                $"an intent stored for KnifeFlint is not used by an equip outside the windows (a drag): it pairs ({HandsText(player)})");
            Invariant(c, player, "intent outside the windows");
            player.HideHandItems();
            yield return null;
            Controls.TestMainHandHeld = true;
            player.UseItem(null, sword, true);
            Controls.TestMainHandHeld = false;
            var queuedHidden = player.IsEquipActionQueued(sword);
            player.RemoveEquipAction(sword);
            player.ShowHandItems();
            yield return null;
            c.Check(queuedHidden && Holds(player, sword, knifeFlint),
                $"an intent stored for the hidden main weapon is not used by the draw: same pair ({HandsText(player)})");
            Invariant(c, player, "intent and draw");
            Hands.ResetState();
            yield return WaitIdle(player);

            // T05: shield keep its vanilla behaviour (D2).
            bench.Pair(sword, axe);
            player.EquipItem(buckler);
            yield return null;
            c.Check(Holds(player, sword, buckler) && !axe.m_equipped,
                $"buckler takes the off hand, sword kept ({HandsText(player)})");
            player.EquipItem(axe);
            yield return null;
            c.Check(Holds(player, axe, buckler) && !sword.m_equipped && !Hands.IsMarked(axe),
                $"with a buckler, AxeIron replaces the sword, buckler kept ({HandsText(player)})");
            Invariant(c, player, "shield");

            // T06: torch replace the off-hand weapon, main weapon kept (D4).
            bench.Pair(sword, axe);
            player.EquipItem(torch);
            yield return null;
            c.Check(Holds(player, sword, torch) && !axe.m_equipped && !Hands.IsMarked(axe),
                $"torch while paired: torch off hand, sword kept ({HandsText(player)})");
            Invariant(c, player, "torch");

            // T07: spear never pair: vanilla one-handed rule.
            bench.Pair(sword, axe);
            player.EquipItem(spear);
            yield return null;
            c.Check(Holds(player, spear, null) && !sword.m_equipped && !axe.m_equipped,
                $"SpearFlint while paired: spear alone ({HandsText(player)})");
            Invariant(c, player, "spear");

            // T09: two knives = knife stance.
            bench.Pair(knifeFlint, knifeBlack);
            yield return null;
            c.Check(Holds(player, knifeFlint, knifeBlack) && StateI(player) == 11,
                $"KnifeFlint + KnifeBlackMetal: knife stance (statei 11), got {StateI(player)} ({HandsText(player)})");
            Invariant(c, player, "knives");
            yield return new WaitForSeconds(0.6f);
            SelfTest.Screenshot(EquipName, "knife-pair-idle");
            yield return null;
            yield return null;

            // D5: butcher knife never go to the off hand.
            player.EquipItem(butcher);
            yield return null;
            c.Check(Holds(player, butcher, null), $"KnifeButcher next to a knife pair: butcher alone ({HandsText(player)})");
            bench.Empty();
            player.EquipItem(knifeFlint);
            player.EquipItem(butcher);
            yield return null;
            c.Check(Holds(player, butcher, null), $"KnifeButcher next to one knife: replaces it ({HandsText(player)})");
            Invariant(c, player, "butcher knife");
            bench.Empty();
            player.EquipItem(sword);
            player.EquipItem(butcher);
            yield return null;
            c.Check(Holds(player, butcher, null) && !sword.m_equipped && !Hands.IsMarked(butcher),
                $"KnifeButcher next to a lone sword: replaces it, never in the off hand ({HandsText(player)})");
            Invariant(c, player, "butcher knife and sword");

            // T04: swap = vanilla equip of the off-hand weapon through vanilla's queue (design 2.3, D22).
            // Reference first: a vanilla hotbar equip of a weapon (the mace pairs into the off hand), sampled like the
            // swap below, so the NOTE can compare the two (animation, minor action = attacks refused, slowdown).
            bench.Pair(sword, axe);
            yield return WaitIdle(player);
            player.UseItem(null, mace, true);
            var vanillaEquip = new ActionSample();
            var sampleUntil = Time.time + 3f;
            while (Time.time < sampleUntil && player.IsEquipActionQueued(mace))
            {
                vanillaEquip.Take(player);
                yield return null;
            }
            yield return WaitIdle(player);
            c.Check(Holds(player, sword, mace), $"hotbar equip of the mace pairs it into the off hand ({HandsText(player)})");
            SelfTest.Note(EquipName, $"vanilla hotbar equip of MaceIron (equip duration {F2(mace.m_shared.m_equipDuration)} s): "
                                     + vanillaEquip.Text());

            // Cancelled like any equip: a dodge clears vanilla's queue (Player.UpdateDodge), hands unchanged.
            bench.Pair(sword, axe);
            yield return WaitIdle(player);
            var dodgeQueued = Hands.TrySwap(player);
            player.Dodge(Forward(player));
            sampleUntil = Time.time + 2f;
            while (Time.time < sampleUntil && (Hands.PendingSwap != null || player.InDodge()))
            {
                yield return null;
            }
            yield return WaitIdle(player);
            c.Check(dodgeQueued && Holds(player, sword, axe) && Hands.PendingSwap == null && !player.IsEquipActionQueued(axe),
                $"a dodge cancels a queued swap like any equip: same hands ({HandsText(player)})");
            // Sprinting (vanilla clears the queue every tick): the key queues nothing and says why (top-left message).
            // IsRunning faked for this one call; the next physics tick computes it again.
            player.m_running = true;
            var sprintQueued = Hands.TrySwap(player);
            player.m_running = false;
            c.Check(!sprintQueued && Hands.PendingSwap == null && !player.IsEquipActionQueued(axe) && Holds(player, sword, axe),
                $"swap key while sprinting: refused, nothing queued (message \"{Hands.SprintMessage}\")");
            // Pair gone while queued (hidden): the swap is taken out of the queue, the draw gives the same hands.
            var hideQueued = Hands.TrySwap(player);
            player.HideHandItems();
            yield return null;
            yield return null;
            var hideCancelled = Hands.PendingSwap == null && !player.IsEquipActionQueued(axe);
            player.ShowHandItems();
            yield return null;
            yield return WaitIdle(player);
            c.Check(hideQueued && hideCancelled && Holds(player, sword, axe),
                $"weapons hidden while a swap is queued: swap dropped, the draw gives the same hands ({HandsText(player)})");
            Invariant(c, player, "cancelled swaps");

            // The swap itself, sampled while it runs and while the draw animation plays after it.
            var swapStart = Time.time;
            var queuedSwap = Hands.TrySwap(player);
            c.Check(queuedSwap && Holds(player, sword, axe) && player.IsEquipActionQueued(axe)
                    && ReferenceEquals(Hands.PendingSwap, axe),
                $"swap key: queued as the equip of the axe, hands unchanged until it ends ({HandsText(player)})");
            var action = player.m_actionQueue.FirstOrDefault(a => ReferenceEquals(a.m_item, axe));
            c.Check(action != null && action.m_type == Player.MinorActionData.ActionType.Equip
                    && Approx(action.m_duration, axe.m_shared.m_equipDuration) && action.m_doneAnimation == Hands.DrawTrigger,
                "queued action: vanilla equip of the axe with its own equip duration, draw trigger equip_hip at its end");
            c.Check(!Hands.TrySwap(player), "a second press while the swap is queued does nothing");
            var attackDuring = player.StartAttack(null, false);
            c.Check(!attackDuring && player.IsEquipActionQueued(axe),
                "an attack is refused while the swap is queued, and the swap stays queued");
            var during = new ActionSample();
            sampleUntil = Time.time + 3f;
            while (Time.time < sampleUntil && Hands.PendingSwap != null)
            {
                during.Take(player);
                yield return null;
            }
            var swapTime = Time.time - swapStart;
            c.Check(Holds(player, axe, sword) && Hands.IsMarked(sword) && !Hands.IsMarked(axe) && StateI(player) == 15,
                $"swap done: axe main, sword off, marker moved to the sword ({HandsText(player)})");
            var equipTime = axe.m_shared.m_equipDuration;
            c.Check(swapTime >= equipTime - 0.02f && swapTime <= equipTime + 0.3f,
                $"the swap took the axe's equip duration ({F2(equipTime)} s): {F2(swapTime)} s");
            c.Check(during.Equipping, "the game's equip animation ran during the swap (animator bool equipping)");
            c.Check(Hands.ComboRestart, "the swap restarts the combo (flag up until the next converted swing starts)");
            var after = new ActionSample();
            sampleUntil = Time.time + 0.6f;
            while (Time.time < sampleUntil)
            {
                after.Take(player);
                yield return null;
            }
            SelfTest.Note(EquipName, $"swap: {F2(swapTime)} s from the key to the hands changing (equip duration "
                                     + $"{F2(equipTime)} s); while queued: {during.Text()}; for 0.6 s after the swap "
                                     + $"(draw trigger {Hands.DrawTrigger}): {after.Text()}; 'equipping' in the player's "
                                     + $"synced bools = {player.m_zanim.m_syncBools.Contains("equipping")} (other players "
                                     + "see the equip animation only then; the draw trigger is an RPC to everyone)");
            Invariant(c, player, "swap");
            // Swaps in a row: the next one waits vanilla's queue pause (0.3 s) after the last action.
            yield return WaitIdle(player);
            yield return SwapHands(player);
            var secondOk = _swapQueued && _swapDone && Holds(player, sword, axe);
            yield return SwapHands(player);
            c.Check(secondOk && _swapQueued && _swapDone && Holds(player, axe, sword)
                    && _swapSeconds >= equipTime + 0.25f,
                $"two swaps in a row: the second one waits the queue pause first ({F2(_swapSeconds)} s; {HandsText(player)})");
            Invariant(c, player, "swaps in a row");

            // One attack press during a swap (R25, T32): the game keeps a press 0.5 s and retries it every tick
            // (Player.PlayerAttackInput, m_queuedAttackTimer); the mod refuses it while the swap is queued. No swing may
            // start before the hands changed; NOTE whether (and how long after the swap) the press still starts one.
            yield return WaitIdle(player);
            var pressStart = Time.time;
            var pressSwap = Hands.TrySwap(player);
            player.m_queuedAttackTimer = 0.5f;
            var attackBefore = player.m_currentAttack;
            var swingBeforeSwap = false;
            var swappedAt = -1f;
            var swingAt = -1f;
            var pressSample = new ActionSample();
            sampleUntil = Time.time + 2.5f;
            while (Time.time < sampleUntil)
            {
                if (swappedAt < 0f && Hands.PendingSwap == null)
                {
                    swappedAt = Time.time - pressStart;
                }
                if (swappedAt >= 0f)
                {
                    pressSample.Take(player);
                }
                if (player.m_currentAttack != null && !ReferenceEquals(player.m_currentAttack, attackBefore))
                {
                    swingBeforeSwap = swappedAt < 0f;
                    swingAt = Time.time - pressStart;
                    break;
                }
                yield return null;
            }
            player.m_queuedAttackTimer = 0f;
            var swingMain = player.m_rightItem;
            yield return WaitIdle(player);
            c.Check(pressSwap && swappedAt >= 0f && !swingBeforeSwap && (swingAt < 0f || ReferenceEquals(swingMain, sword)),
                $"one attack press during a swap: no swing before the hands changed (swap after {F2(swappedAt)} s)");
            SelfTest.Note(EquipName, swingAt >= 0f
                ? $"one attack press during a swap: the swing started {F2(swingAt - swappedAt)} s after the hands changed "
                  + $"({F2(swingAt)} s after the press; the game keeps a press 0.5 s); between the swap and the swing: "
                  + pressSample.Text()
                : $"one attack press during a swap: no swing started within 2.5 s (the press was lost; swap after "
                  + $"{F2(swappedAt)} s, the game keeps a press 0.5 s); after the swap: {pressSample.Text()}");
            // Hands back to the axe in the main hand for the steps below.
            yield return SwapHands(player);
            c.Check(_swapQueued && _swapDone && Holds(player, axe, sword),
                $"swapped back after the attack press ({HandsText(player)})");
            Invariant(c, player, "attack press during a swap");

            // T22: unequip main weapon: off-hand weapon become main weapon next frame.
            player.UnequipItem(axe);
            yield return null;
            c.Check(Holds(player, sword, null) && !Hands.IsMarked(sword),
                $"main weapon unequipped: next frame the sword is the main weapon, marker cleared ({HandsText(player)})");
            Invariant(c, player, "lone off hand");

            // Broken weapon refused like vanilla.
            bench.Empty();
            player.EquipItem(sword);
            var durability = axe.m_durability;
            axe.m_durability = 0f;
            var equipped = player.EquipItem(axe);
            axe.m_durability = durability;
            yield return null;
            c.Check(!equipped && Holds(player, sword, null), $"a broken AxeIron is refused ({HandsText(player)})");
            Invariant(c, player, "broken weapon");

            // Forced exception inside the apply step (Debug hook). Hands.Apply directly: through the patch,
            // PatchGuard would log an error (a failed in-world run) and hide later real errors of that site.
            var threw = false;
            Hands.TestThrowInApply = true;
            try
            {
                Hands.Apply(player, axe, EquipRow.Restore, false);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
            Hands.TestThrowInApply = false;
            yield return null;
            c.Check(threw && Holds(player, axe, null) && axe.m_equipped && !sword.m_equipped,
                $"exception inside the restore step: hands re-synced, the displaced sword is not flagged equipped ({HandsText(player)})");
            Invariant(c, player, "exception in restore");
            bench.Empty();
            player.EquipItem(sword);
            threw = false;
            Hands.TestThrowInApply = true;
            try
            {
                Hands.Apply(player, axe, EquipRow.OffHand, false);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
            Hands.TestThrowInApply = false;
            yield return null;
            c.Check(threw && Holds(player, sword, null) && !axe.m_equipped && sword.m_equipped,
                $"exception inside the off-hand step: nothing half-equipped ({HandsText(player)})");
            Invariant(c, player, "exception in off hand");
            c.Report();
        }
        finally
        {
            if (bench != null)
            {
                bench.TakeBack();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
        }
    }

    // Swap through the key's path (TrySwap queue it, vanilla's queue end it): wait until done or dropped. Results in
    // the fields below (iterators have no out parameters).
    private static bool _swapQueued;
    private static bool _swapDone;
    private static float _swapSeconds;

    private static IEnumerator SwapHands(Player p, Dummy dummy = null)
    {
        var right = p.m_rightItem;
        var left = p.m_leftItem;
        var start = Time.time;
        _swapQueued = Hands.TrySwap(p);
        _swapDone = false;
        _swapSeconds = 0f;
        if (!_swapQueued)
        {
            yield break;
        }
        var until = Time.time + 3f;
        while (Time.time < until && Hands.PendingSwap != null)
        {
            Hold(dummy);
            yield return null;
        }
        _swapSeconds = Time.time - start;
        _swapDone = Holds(p, left, right);
    }

    // What the player's animator and minor-action checks show while an equip action or a draw runs.
    private sealed class ActionSample
    {
        internal bool Equipping;
        internal bool Minor;
        internal bool Slowdown;
        private readonly List<string> _clips = new List<string>();

        internal void Take(Player p)
        {
            var animator = p.m_animator;
            if (animator == null)
            {
                return;
            }
            Equipping |= animator.GetBool("equipping");
            Minor |= p.InMinorAction();
            Slowdown |= p.InMinorActionSlowdown();
            for (var layer = 0; layer < animator.layerCount; layer++)
            {
                AddClips(layer, animator.GetCurrentAnimatorClipInfo(layer));
                if (animator.IsInTransition(layer))
                {
                    AddClips(layer, animator.GetNextAnimatorClipInfo(layer));
                }
            }
        }

        private void AddClips(int layer, AnimatorClipInfo[] infos)
        {
            foreach (var info in infos)
            {
                var name = $"{layer}:{(info.clip != null ? info.clip.name : "?")}";
                if (!_clips.Contains(name))
                {
                    _clips.Add(name);
                }
            }
        }

        internal string Text() =>
            $"equipping bool {Equipping}, InMinorAction {Minor}, InMinorActionSlowdown (walk speed) {Slowdown}, "
            + $"clips seen {(_clips.Count == 0 ? "none" : string.Join(", ", _clips.ToArray()))}";
    }

    // ---------- dual.keep ----------

    private static IEnumerator RunKeep()
    {
        var c = new Checks(KeepName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(KeepName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        List<Player.Food> foods = null;
        try
        {
            bench = new Bench(player);
            foods = new List<Player.Food>(player.m_foods);
            // Sword, axe, hammer, one food stack at a time, one free slot for the drags.
            if (bench.FreeSlots < 5)
            {
                c.Check(false, $"needs 5 free inventory slots, has {bench.FreeSlots}");
                c.Report();
                yield break;
            }
            var sword = bench.Give(Sword);
            var axe = bench.Give(Axe);
            var hammer = bench.Give(HammerName);
            // Food the way a real game mostly hold it: Inventory.AddItem(name) Instantiate it (load, pickup and crafting
            // too), so it carry its own SharedData copy.
            var copyBerry = bench.GiveStack(Berry, 1);
            if (sword == null || axe == null || hammer == null || copyBerry == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            var berryName = copyBerry.m_shared.m_name;
            var eatTime = copyBerry.m_shared.m_foodEatAnimTime;
            var inventory = bench.Inventory;
            bench.Pair(sword, axe);
            yield return null;
            c.Check(Holds(player, sword, axe), $"pair for the keep tests ({HandsText(player)})");

            // T16: hide and draw, three times.
            for (var i = 1; i <= 3; i++)
            {
                player.HideHandItems();
                yield return null;
                c.Check(player.m_rightItem == null && player.m_leftItem == null
                        && ReferenceEquals(player.m_hiddenRightItem, sword) && ReferenceEquals(player.m_hiddenLeftItem, axe),
                    $"hide {i}: both weapons hidden ({HandsText(player)})");
                player.ShowHandItems();
                yield return null;
                c.Check(Holds(player, sword, axe), $"draw {i}: same hands ({HandsText(player)})");
                Invariant(c, player, $"hide and draw {i}");
            }

            // T17, food with its own SharedData copy (most food of a real game): vanilla UseItem show the food and hide
            // the right hand only when ObjectDB.TryGetItemPrefab find the item's SharedData object, so here only the
            // eat animation play. Pair stay in hand.
            yield return WaitMinor(player);
            var copyKnown = ObjectDB.instance.TryGetItemPrefab(copyBerry.m_shared, out _);
            player.m_foods.Clear();
            player.UseItem(null, copyBerry, true);
            yield return null;
            var copyShown = player.m_useItemTime > 0f;
            SelfTest.Note(KeepName, $"{Berry} made by Inventory.AddItem(name): ObjectDB knows its SharedData object = "
                                    + $"{copyKnown}, eaten = {player.m_foods.Count > 0}, food shown in hand and main "
                                    + $"weapon hidden = {copyShown} (design 1.2)");
            c.Check(copyShown || Holds(player, sword, axe),
                $"eating food with its own SharedData copy: nothing hidden, the pair stays in hand ({HandsText(player)})");
            yield return WaitEat(player);
            yield return WaitMinor(player);
            c.Check(Holds(player, sword, axe), $"after eating food with its own SharedData copy: same hands ({HandsText(player)})");
            Invariant(c, player, "eat own copy");

            // T17, food the game shows in hand (the prefab's SharedData object: Inventory.AddItem(GameObject, int), a
            // chest's first loot; a feast that is a Consumable ItemDrop piece take the same SetUseHandVisual path, the
            // dual.data NOTE say which feasts are). Only the main weapon is hidden; attacks refused meanwhile; same
            // hands after.
            // Five: eat window, eat + dodge, eat + full hide, two eats in a row.
            var berry = bench.GiveLinkedStack(Berry, 5);
            c.Check(berry != null && ObjectDB.instance.TryGetItemPrefab(berry.m_shared, out _),
                $"{Berry} added through Inventory.AddItem(GameObject, int) carries the prefab's SharedData object");
            player.m_foods.Clear();
            if (berry != null)
            {
                player.UseItem(null, bench.Stack(berryName), true);
            }
            yield return null;
            var eating = player.m_useItemTime > 0f;
            c.Check(eating && player.m_rightItem == null && ReferenceEquals(player.m_hiddenRightItem, sword)
                    && Holds(player, null, axe) && Hands.IsEatWindow(player),
                $"eating: main weapon hidden, off-hand weapon stays, eat window seen (eat timer {F2(player.m_useItemTime)} s; "
                + $"{HandsText(player)})");
            Invariant(c, player, "eat window");
            if (!eating)
            {
                // No eat window = eat steps below test nothing (and full hide stay hidden).
                SelfTest.Note(KeepName, "the eat window did not start: eat + attack, eat + dodge and eat + full hide skipped");
            }
            else
            {
                // R21: sample the eat animation a moment (it starts through an animator transition).
                var sawMinor = player.InMinorAction();
                var sampleUntil = Time.time + 0.25f;
                while (Time.time < sampleUntil && player.m_useItemTime > 0.4f)
                {
                    sawMinor |= player.InMinorAction();
                    yield return null;
                }
                SelfTest.Note(KeepName, $"R21: InMinorAction seen while eating = {sawMinor}; eat time {F2(eatTime)} s");
                var started = player.StartAttack(null, false);
                yield return null;
                c.Check(!started && !player.InAttack(), "an attack during the eat window is refused");
                yield return WaitEat(player);
                c.Check(Holds(player, sword, axe), $"after eating: same hands ({HandsText(player)})");
                Invariant(c, player, "after eating");

                // Eat, then dodge just before eat end: main weapon come back after the dodge.
                yield return WaitMinor(player);
                player.m_foods.Clear();
                player.UseItem(null, bench.Stack(berryName), true);
                yield return null;
                var dodgeEat = player.m_useItemTime > 0f;
                c.Check(dodgeEat, $"eat + dodge: the eat window started (eat timer {F2(player.m_useItemTime)} s)");
                var until = Time.time + 3f;
                while (Time.time < until && player.m_useItemTime > 0.5f)
                {
                    yield return null;
                }
                player.Dodge(Forward(player));
                var dodgeAtEnd = false;
                var sawPending = false;
                var before = player.m_useItemTime;
                until = Time.time + 4f;
                while (Time.time < until && (player.m_useItemTime > 0f || player.InDodge() || Hands.PendingEatReturn))
                {
                    var now = player.m_useItemTime;
                    if (before > 0f && now <= 0f)
                    {
                        dodgeAtEnd = player.InDodge();
                    }
                    before = now;
                    sawPending |= Hands.PendingEatReturn;
                    yield return null;
                }
                yield return null;
                yield return null;
                SelfTest.Note(KeepName, $"eat + dodge: dodging when the eat ended = {dodgeAtEnd}, delayed return used = {sawPending}");
                c.Check(!dodgeAtEnd || sawPending, "an eat that ends during a dodge waits for the dodge (delayed return)");
                c.Check(Holds(player, sword, axe), $"eat + dodge: same hands after the dodge ({HandsText(player)})");
                Invariant(c, player, "eat and dodge");
                yield return WaitIdle(player);

                // Eat, then full hide during eat (dive, station): eat return draw both.
                yield return WaitMinor(player);
                player.m_foods.Clear();
                player.UseItem(null, bench.Stack(berryName), true);
                yield return null;
                if (player.m_useItemTime > 0f)
                {
                    player.HideHandItems();
                    yield return null;
                    c.Check(player.m_rightItem == null && player.m_leftItem == null
                            && ReferenceEquals(player.m_hiddenRightItem, sword) && ReferenceEquals(player.m_hiddenLeftItem, axe),
                        $"full hide during the eat keeps the eat-hidden main weapon ({HandsText(player)})");
                    yield return WaitEat(player);
                    c.Check(Holds(player, sword, axe), $"eat + full hide: the eat return draws both, same hands ({HandsText(player)})");
                    Invariant(c, player, "eat and full hide");
                }
                else
                {
                    c.Check(false, $"eat + full hide: the eat window did not start ({HandsText(player)})");
                }

                // Two eats in a row (hotbar food, one key after the other): second eat hide the right hand again while
                // the main weapon is already hidden. Without the keep, vanilla write the empty right hand over it and
                // the axe end alone in the main hand.
                yield return WaitIdle(player);
                yield return WaitMinor(player);
                player.m_foods.Clear();
                player.UseItem(null, bench.Stack(berryName), true);
                yield return null;
                if (player.m_useItemTime > 0f)
                {
                    player.m_foods.Clear();
                    player.UseItem(null, bench.Stack(berryName), true);
                    yield return null;
                    c.Check(player.m_useItemTime > 0f && player.m_rightItem == null
                            && ReferenceEquals(player.m_hiddenRightItem, sword) && Holds(player, null, axe),
                        $"second eat inside the eat window keeps the eat-hidden main weapon (eat timer "
                        + $"{F2(player.m_useItemTime)} s; {HandsText(player)})");
                    Invariant(c, player, "second eat");
                    yield return WaitEat(player);
                    c.Check(Holds(player, sword, axe), $"two eats in a row: same hands after ({HandsText(player)})");
                    Invariant(c, player, "two eats in a row");
                }
                else
                {
                    c.Check(false, $"two eats in a row: the eat window did not start ({HandsText(player)})");
                }
            }

            // Step above lost pair (its check failed): me pair again, so steps below still test something.
            if (!Holds(player, sword, axe))
            {
                SelfTest.Note(KeepName, $"pair lost in the eat steps ({HandsText(player)}): paired again for the next steps");
                bench.Pair(sword, axe);
                yield return null;
            }

            // T19: load order (Player.Load equips every flagged item in inventory list order).
            for (var round = 0; round < 2; round++)
            {
                var offFirst = round == 0;
                var main = player.m_rightItem;
                var off = player.m_leftItem;
                if (main == null || off == null)
                {
                    c.Check(false, $"load order {round + 1}: no pair to start from ({HandsText(player)})");
                    break;
                }
                var others = inventory.GetAllItems()
                    .Where(i => player.IsItemEquiped(i) && !ReferenceEquals(i, main) && !ReferenceEquals(i, off)).ToList();
                player.UnequipItem(main, false);
                player.UnequipItem(off, false);
                main.m_equipped = true;
                off.m_equipped = true;
                var first = offFirst ? off : main;
                inventory.m_inventory.Remove(first);
                inventory.m_inventory.Insert(0, first);
                player.EquipInventoryItems();
                // Vanilla clear the flag of items it could not equip (already equipped): put theirs back.
                foreach (var other in others)
                {
                    other.m_equipped = player.IsItemEquiped(other);
                }
                yield return null;
                c.Check(Holds(player, sword, axe),
                    $"load with the {(offFirst ? "off-hand" : "main")} weapon first in the inventory list: same hands ({HandsText(player)})");
                Invariant(c, player, $"load order {round + 1}");
            }
            if (!Holds(player, sword, axe))
            {
                SelfTest.Note(KeepName, $"pair lost in the load-order steps ({HandsText(player)}): paired again for the drags");
                bench.Pair(sword, axe);
                yield return null;
            }

            // T21: drags through the real InventoryGrid.DropItem with InventoryGui.OnSelectedItem's sequence.
            var gui = InventoryGui.instance;
            var grid = gui != null ? gui.m_playerGrid : null;
            if (grid == null || !ReferenceEquals(grid.GetInventory(), inventory))
            {
                SelfTest.Note(KeepName, "the inventory grid has not shown the player inventory yet: drags use the same moves by hand");
                grid = null;
            }
            var empty = EmptySlot(inventory);
            c.Check(empty.x >= 0, "a free inventory slot for the drag");
            if (empty.x >= 0 && HoldsKinds(player, sword, axe))
            {
                Drag(player, inventory, grid, player.m_rightItem, empty);
                yield return null;
                c.Check(HoldsKinds(player, sword, axe), $"drag the main weapon to an empty slot: same hands ({HandsText(player)})");
                Invariant(c, player, "drag main to empty slot");
            }
            if (HoldsKinds(player, sword, axe))
            {
                Drag(player, inventory, grid, player.m_rightItem, player.m_leftItem.m_gridPos);
                yield return null;
                c.Check(HoldsKinds(player, sword, axe), $"drag the main weapon onto the off-hand weapon: same hands ({HandsText(player)})");
                Invariant(c, player, "drag main onto off");
            }
            if (HoldsKinds(player, sword, axe))
            {
                Drag(player, inventory, grid, player.m_leftItem, player.m_rightItem.m_gridPos);
                yield return null;
                c.Check(HoldsKinds(player, sword, axe), $"drag the off-hand weapon onto the main weapon: same hands ({HandsText(player)})");
                Invariant(c, player, "drag off onto main");
            }
            // Drags re-add items as clones: me follow them. Pair lost = rest cannot run.
            if (!HoldsKinds(player, sword, axe))
            {
                c.Check(false, $"pair lost after the drags, later steps skipped ({HandsText(player)})");
                c.Report();
                yield break;
            }
            sword = player.m_rightItem;
            axe = player.m_leftItem;

            // Unequip main weapon, then nothing: off-hand weapon become main weapon next frame.
            player.UnequipItem(sword);
            yield return null;
            c.Check(Holds(player, axe, null) && !Hands.IsMarked(axe),
                $"main weapon unequipped: the axe is the main weapon next frame, marker cleared ({HandsText(player)})");
            Invariant(c, player, "lone off hand");

            // Right then left in one call chain (tool branch, loadout mods): both hands empty.
            bench.Pair(sword, axe);
            player.UnequipItem(player.m_rightItem, false);
            player.UnequipItem(player.m_leftItem, false);
            yield return null;
            c.Check(player.m_rightItem == null && player.m_leftItem == null && !sword.m_equipped && !axe.m_equipped,
                $"unequip right then left: both hands empty, nothing flagged equipped ({HandsText(player)})");
            Invariant(c, player, "unequip right then left");

            // T23: radial hammer: equip the hammer, put it away, EquipItem(last left), EquipItem(last right), one frame.
            bench.Pair(sword, axe);
            var lastLeft = player.m_leftItem;
            var lastRight = player.m_rightItem;
            player.EquipItem(hammer);
            player.UnequipItem(hammer);
            player.EquipItem(lastLeft);
            player.EquipItem(lastRight);
            yield return null;
            c.Check(Holds(player, sword, axe), $"radial hammer and back: same hands ({HandsText(player)})");
            Invariant(c, player, "radial hammer");

            // T20: death (UnequipAllItems): nothing equipped, nothing flagged.
            var worn = inventory.GetAllItems()
                .Where(i => player.IsItemEquiped(i) && !ReferenceEquals(i, sword) && !ReferenceEquals(i, axe)).ToList();
            player.UnequipAllItems();
            yield return null;
            c.Check(player.m_rightItem == null && player.m_leftItem == null && !sword.m_equipped && !axe.m_equipped
                    && Broken(player) == null,
                $"UnequipAllItems: both hands empty, no item flagged equipped in no slot ({Broken(player)})");
            foreach (var item in worn)
            {
                player.EquipItem(item, false);
            }

            // Stale marker on main weapon: without clean-up, draw swap the hands (control).
            bench.Pair(sword, axe);
            Hands.SetMarked(sword, true);
            player.HideHandItems();
            player.ShowHandItems();
            yield return null;
            c.Check(Holds(player, axe, sword), $"control: two marked weapons swap hands at the draw ({HandsText(player)})");
            bench.Pair(sword, axe);
            Hands.SetMarked(sword, true);
            Hands.ClearStaleMarkers(player);
            player.HideHandItems();
            player.ShowHandItems();
            yield return null;
            c.Check(Holds(player, sword, axe) && !Hands.IsMarked(sword),
                $"stale marker cleared (activation, login): same hands after hide and draw ({HandsText(player)})");
            Invariant(c, player, "stale marker");

            // Rule 2 leave main weapon unmarked.
            bench.Empty();
            player.EquipItem(sword);
            Hands.SetMarked(sword, true);
            player.EquipItem(axe);
            yield return null;
            c.Check(Holds(player, sword, axe) && !Hands.IsMarked(sword) && Hands.IsMarked(axe),
                "forming a pair clears a stale marker on the main weapon");
            Invariant(c, player, "rule 2 marker");

            // T30: rules change while paired: excluded off-hand weapon put away, stance one-handed.
            ServerRules.TestRules = Rules(excluded: Axe);
            yield return null;
            yield return null;
            c.Check(Holds(player, sword, null) && !axe.m_equipped && StateI(player) == 1,
                $"ExcludedWeapons = AxeIron while paired: axe put away, sword kept, one-handed stance (statei {StateI(player)}) ({HandsText(player)})");
            Invariant(c, player, "rules change");
            ServerRules.TestRules = DualRules.Defaults;
            yield return null;
            player.EquipItem(axe);
            yield return null;
            c.Check(Holds(player, sword, axe), $"exclusion cleared: pairing works again ({HandsText(player)})");
            c.Report();
        }
        finally
        {
            if (foods != null)
            {
                player.m_foods.Clear();
                player.m_foods.AddRange(foods);
                player.UpdateFood(0f, true);
            }
            if (bench != null)
            {
                bench.TakeBack();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
        }
    }

    // Eat timer run out (vanilla UpdateUseVisual), then the eat return had its frame.
    private static IEnumerator WaitEat(Player p)
    {
        var until = Time.time + 5f;
        while (Time.time < until && (p.m_useItemTime > 0f || Hands.PendingEatReturn))
        {
            yield return null;
        }
        yield return null;
        yield return null;
    }

    // Minor action over (eat animation, equip animation of a draw), so the next eat sample start clean.
    private static IEnumerator WaitMinor(Player p)
    {
        var until = Time.time + 3f;
        while (Time.time < until && p.InMinorAction())
        {
            yield return null;
        }
    }

    private static Vector2i EmptySlot(Inventory inventory)
    {
        for (var y = 0; y < inventory.GetHeight(); y++)
        {
            for (var x = 0; x < inventory.GetWidth(); x++)
            {
                if (inventory.GetItemAt(x, y) == null)
                {
                    return new Vector2i(x, y);
                }
            }
        }
        return new Vector2i(-1, -1);
    }

    // What InventoryGui.OnSelectedItem does with a dragged item dropped at pos, in one frame: remove queued equips,
    // unequip both, move (InventoryGrid.DropItem), re-equip the item now at each of the two slots.
    private static void Drag(Player p, Inventory inventory, InventoryGrid grid, ItemDrop.ItemData drag, Vector2i pos)
    {
        var target = inventory.GetItemAt(pos.x, pos.y);
        var dragEquipped = p.IsItemEquiped(drag);
        var targetEquipped = target != null && p.IsItemEquiped(target);
        var from = drag.m_gridPos;
        p.RemoveEquipAction(target);
        p.RemoveEquipAction(drag);
        p.UnequipItem(drag, false);
        p.UnequipItem(target, false);
        if (grid != null)
        {
            grid.DropItem(inventory, drag, drag.m_stack, pos);
        }
        else if (target != null && !ReferenceEquals(target, drag))
        {
            // InventoryGrid.DropItem, swap branch (different items, one per stack).
            inventory.RemoveItem(drag);
            inventory.MoveItemToThis(inventory, target, target.m_stack, from.x, from.y);
            inventory.MoveItemToThis(inventory, drag, drag.m_stack, pos.x, pos.y);
        }
        else if (target == null)
        {
            inventory.MoveItemToThis(inventory, drag, drag.m_stack, pos.x, pos.y);
        }
        if (dragEquipped)
        {
            var at = inventory.GetItemAt(pos.x, pos.y);
            if (at != null)
            {
                p.EquipItem(at, false);
            }
            if (inventory.ContainsItem(drag))
            {
                p.EquipItem(drag, false);
            }
        }
        if (targetEquipped)
        {
            var at = inventory.GetItemAt(from.x, from.y);
            if (at != null)
            {
                p.EquipItem(at, false);
            }
            if (inventory.ContainsItem(target))
            {
                p.EquipItem(target, false);
            }
        }
    }

    // ---------- dual.attack ----------

    private static IEnumerator RunAttack()
    {
        var c = new Checks(AttackName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(AttackName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        SkillSave skills = null;
        try
        {
            bench = new Bench(player);
            skills = new SkillSave(player, Skills.SkillType.Swords, Skills.SkillType.Axes, Skills.SkillType.Knives,
                Skills.SkillType.Clubs, Skills.SkillType.WoodCutting);
            var sword = bench.Give(Sword);
            var axe = bench.Give(Axe);
            var knifeFlint = bench.Give(KnifeFlintName);
            var knifeBlack = bench.Give(KnifeBlack);
            var club = bench.Give(ClubName);
            if (sword == null || axe == null || knifeFlint == null || knifeBlack == null || club == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            var dummy = bench.Spawn(TrollName, DummyGap);
            c.Check(dummy != null && dummy.Character != null, "Troll spawned in front of the player");
            NoteDummy(AttackName, dummy);
            yield return new WaitForSeconds(0.5f);
            Hold(dummy);
            DualSwing.ResetRecords();
            DualSwing.Recording = true;

            // A: hands, wear, skills, stamina. SwordIron main, AxeIron off.
            bench.Pair(sword, axe);
            yield return null;
            c.Check(Holds(player, sword, axe), $"SwordIron + AxeIron paired ({HandsText(player)})");
            var swordWear = sword.m_durability;
            var axeWear = axe.m_durability;
            var swords = SkillSave.Value(player, Skills.SkillType.Swords);
            var axes = SkillSave.Value(player, Skills.SkillType.Axes);
            var damagesBefore = DualSwing.Damages.Count;
            var from = DualSwing.Swings.Count;
            var shots = new Shots(AttackName, "dualaxes2", "dualaxes3");
            yield return Swing(player, dummy, 4, false, onHit: shots.OnHit);
            CheckSwings(c, from, "four chained swings", "dualaxes0:0M", "dualaxes1:0O", "dualaxes2:0M,1O", "dualaxes3:0M,1O");
            CheckWeapons(c, from, sword, axe, "combo");
            CheckStamina(c, from, 10f, "combo swings: max(10, 10)");
            NoteSwings(AttackName, from, dummy);
            c.Check(DualSwing.Damages.Count > damagesBefore, $"the Troll was hit ({DualSwing.Damages.Count - damagesBefore} hits)");
            c.Check(sword.m_durability < swordWear && axe.m_durability < axeWear,
                $"both weapons wear (sword {F2(swordWear)} -> {F2(sword.m_durability)}, axe {F2(axeWear)} -> {F2(axe.m_durability)})");
            c.Check(SkillSave.Value(player, Skills.SkillType.Swords) > swords && SkillSave.Value(player, Skills.SkillType.Axes) > axes,
                "Swords and Axes both rise (each hit trains its own weapon's skill)");
            from = DualSwing.Swings.Count;
            yield return Swing(player, dummy, 1, true);
            CheckSwings(c, from, "special", "dualaxes_secondary:0M,0O");
            CheckWeapons(c, from, sword, axe, "special");
            CheckStamina(c, from, 20f, "special: 10 x 32 / 16");
            NoteSwings(AttackName, from, dummy);

            // D: knife moves. KnifeFlint main, KnifeBlackMetal off.
            bench.Pair(knifeFlint, knifeBlack);
            yield return new WaitForSeconds(0.4f);
            c.Check(Holds(player, knifeFlint, knifeBlack) && StateI(player) == 11, $"knife pair, knife stance ({HandsText(player)})");
            from = DualSwing.Swings.Count;
            yield return Swing(player, dummy, 3, false);
            CheckSwings(c, from, "knife combo", "dual_knives0:0M", "dual_knives1:0O", "dual_knives2:0M,0O");
            CheckWeapons(c, from, knifeFlint, knifeBlack, "knife combo");
            CheckStamina(c, from, 12f, "knife combo: max(4, 12)");
            NoteSwings(AttackName, from, dummy);
            from = DualSwing.Swings.Count;
            yield return Swing(player, dummy, 1, true);
            CheckSwings(c, from, "knife leap", "dual_knives_secondary:0M,0O");
            CheckStamina(c, from, 36f, "knife leap: 12 x 42 / 14");
            NoteSwings(AttackName, from, dummy);

            // F: swap restart the combo (control first: without swap, next swing continue it). The swap is a queued
            // equip now (equip time, then queue pause), so the chain window (0.2 s) is over anyway when the next
            // swing come; the flag is checked too (it matter for a weapon with no equip time).
            bench.Pair(sword, axe);
            yield return WaitIdle(player, dummy);
            from = DualSwing.Swings.Count;
            yield return Swing(player, dummy, 1, false, settle: false);
            yield return Swing(player, dummy, 1, false);
            CheckTriggers(c, from, "control: a swing started right after another continues the combo", "dualaxes0", "dualaxes1");
            yield return new WaitForSeconds(0.5f);
            from = DualSwing.Swings.Count;
            yield return Swing(player, dummy, 1, false, settle: false);
            yield return SwapHands(player, dummy);
            var restartAfterSwap = Hands.ComboRestart;
            yield return Swing(player, dummy, 1, false);
            c.Check(_swapQueued && _swapDone && Holds(player, axe, sword),
                $"swap right after a swing: queued, then done ({HandsText(player)})");
            c.Check(restartAfterSwap && !Hands.ComboRestart,
                "the swap raised the combo-restart flag, the next swing used it up");
            CheckTriggers(c, from, "after a swap the next swing starts the combo again", "dualaxes0", "dualaxes0");
            c.Check(DualSwing.Swings.Count == from + 2 && ReferenceEquals(DualSwing.Swings[from + 1].Main, axe),
                "the swing after the swap is struck first by the new main weapon");

            // Restart gap: StartAttack done, animator not in the attack state yet (InAttack false): swap refused, so
            // the recorded swing keep its off-hand weapon. Control after: swing over = swap work.
            yield return WaitIdle(player, dummy);
            var gapStarted = player.StartAttack(null, false);
            var gapInAttack = player.InAttack();
            var gapSwapped = Hands.TrySwap(player);
            c.Check(gapStarted && !gapSwapped && Hands.PendingSwap == null && Holds(player, axe, sword),
                $"swap refused while a swing is starting (InAttack {gapInAttack}; {HandsText(player)})");
            yield return WaitIdle(player, dummy);
            yield return SwapHands(player, dummy);
            c.Check(_swapQueued && _swapDone && Holds(player, sword, axe),
                $"control: once the swing is over the swap works ({HandsText(player)})");

            // T13: Club in main hand: no special; after swap the special play.
            bench.Pair(club, axe);
            yield return WaitIdle(player, dummy);
            c.Check(Holds(player, club, axe), $"Club + AxeIron paired ({HandsText(player)})");
            from = DualSwing.Swings.Count;
            var started = player.StartAttack(null, true);
            yield return null;
            c.Check(!started && DualSwing.Swings.Count == from, "Club in the main hand: the special attack is refused");
            yield return WaitIdle(player, dummy);
            yield return SwapHands(player, dummy);
            c.Check(_swapQueued && _swapDone && Holds(player, axe, club), $"swap: axe main, club off ({HandsText(player)})");
            from = DualSwing.Swings.Count;
            yield return Swing(player, dummy, 1, true);
            CheckSwings(c, from, "special with the club in the off hand", "dualaxes_secondary:0M,0O");

            // E3, moves of an item that is no dual weapon: PairMoves = SwordIron (one hit event per swing). Combo hand
            // change by chain step (main, off, main), its special strike with both. Its own stance (OneHanded).
            ServerRules.TestRules = Rules(pair: Sword);
            bench.Pair(sword, axe);
            yield return WaitIdle(player, dummy);
            yield return new WaitForSeconds(0.4f);
            var swordMoves = MoveTemplates.For(sword, axe, ServerRules.Current, player);
            c.Check(swordMoves != null && swordMoves.PrefabName == Sword
                    && StateI(player) == (int)sword.m_shared.m_animationState,
                $"PairMoves = SwordIron: the sword's moves and stance (statei {StateI(player)}, "
                + $"got {(swordMoves != null ? swordMoves.Summary : "none")})");
            from = DualSwing.Swings.Count;
            yield return Swing(player, dummy, 3, false);
            CheckSwings(c, from, "PairMoves = SwordIron: the combo alternates by swing", "swing_longsword0:0M",
                "swing_longsword1:0O", "swing_longsword2:0M");
            CheckWeapons(c, from, sword, axe, "SwordIron moves");
            NoteSwings(AttackName, from, dummy);
            from = DualSwing.Swings.Count;
            yield return Swing(player, dummy, 1, true);
            CheckSwings(c, from, "PairMoves = SwordIron: the special strikes with both", "sword_secondary:0M,0O");
            NoteSwings(AttackName, from, dummy);
            ServerRules.TestRules = DualRules.Defaults;
            c.Report();
        }
        finally
        {
            DualSwing.Recording = false;
            DualSwing.ResetRecords();
            if (bench != null)
            {
                bench.TakeBack();
            }
            if (skills != null)
            {
                skills.Restore();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
        }
    }

    // ---------- dual.damage ----------

    private static IEnumerator RunDamage()
    {
        var c = new Checks(DamageName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(DamageName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        SkillSave skills = null;
        try
        {
            bench = new Bench(player);
            skills = new SkillSave(player, Skills.SkillType.Swords, Skills.SkillType.Axes);
            var first = bench.Give(Sword);
            var second = bench.Give(Sword);
            if (first == null || second == null)
            {
                c.Check(false, "could not give two SwordIron");
                c.Report();
                yield break;
            }
            var dummy = bench.Spawn(TrollName, DummyGap);
            c.Check(dummy != null && dummy.Character != null, "Troll spawned in front of the player");
            NoteDummy(DamageName, dummy);
            // Hits compared without vanilla's two own factors (DualSwing.DamageRecord): the random skill factor, a
            // fresh roll per hit, and the multi-object split (a sweep that also touch the ground or a rock deal
            // 1 / (objects x 0.75) to each). Skill 100 = the roll within 0.85-1, which the checks use to see the
            // roll recorded is a real one.
            SkillSave.SetLevel(player, Skills.SkillType.Swords, 100f);
            ServerRules.TestRules = Rules(offHand: 50);
            bench.Pair(first, second);
            yield return new WaitForSeconds(0.5f);
            c.Check(Holds(player, first, second), $"two SwordIron copies pair ({HandsText(player)})");
            var template = MoveTemplates.For(first, second, ServerRules.Current, player);
            if (template == null || template.Secondary == null)
            {
                c.Check(false, "pair template with a special");
                c.Report();
                yield break;
            }
            DualSwing.ResetRecords();
            DualSwing.Recording = true;

            // B: OffHandDamage 50 on the combo.
            var from = DualSwing.Swings.Count;
            yield return Swing(player, dummy, 4, false);
            CheckSwings(c, from, "combo with OffHandDamage 50", "dualaxes0:0M", "dualaxes1:0O", "dualaxes2:0M,1O", "dualaxes3:0M,1O");
            var baseMultiplier = template.Primary.m_damageMultiplier;
            foreach (var trigger in new[] { "dualaxes2", "dualaxes3" })
            {
                var swing = SwingIndex(from, trigger);
                var mainHit = HitOf(swing, 0, Hand.Main);
                var offHit = HitOf(swing, 1, Hand.Off);
                c.Check(mainHit != null && offHit != null && Approx(mainHit.DamageMultiplier, baseMultiplier)
                        && Approx(offHit.DamageMultiplier, baseMultiplier * 0.5f),
                    $"{trigger}: damage multiplier main {F2(baseMultiplier)}, off {F2(baseMultiplier * 0.5f)} expected "
                    + $"(got {(mainHit != null ? F2(mainHit.DamageMultiplier) : "none")} / {(offHit != null ? F2(offHit.DamageMultiplier) : "none")})");
                var mainDamage = DamageOf(swing, 0, Hand.Main);
                var offDamage = DamageOf(swing, 1, Hand.Off);
                var ratio = BaseRatio(offDamage, mainDamage);
                SelfTest.Note(DamageName, $"{trigger}: main-hand hit {DamageText(mainDamage)}, off-hand hit "
                                          + $"{DamageText(offDamage)}, ratio {F2(ratio)} (raw "
                                          + $"{(mainDamage != null && offDamage != null && mainDamage.Total > 0f ? F2(offDamage.Total / mainDamage.Total) : "none")})");
                c.Check(Rolled(mainDamage) && Rolled(offDamage),
                    $"{trigger}: each hit's random skill factor (0.85-1 at skill 100) and the objects its sweep touched "
                    + $"recorded (main: {DamageText(mainDamage)}; off hand: {DamageText(offDamage)})");
                c.Check(Mathf.Abs(ratio - 0.5f) <= RatioTolerance,
                    $"{trigger}: off-hand hit / main-hand hit 0.50 with OffHandDamage 50, without the random skill factor "
                    + $"and the multi-object split (got {F2(ratio)})");
            }

            // T11: off-hand hit of the second swing = half the main-hand hit of the first swing; both hits of the fourth
            // swing doubled (vanilla double every hit of the last chain step).
            var firstMain = DamageOf(SwingIndex(from, "dualaxes0"), 0, Hand.Main);
            var secondOff = DamageOf(SwingIndex(from, "dualaxes1"), 0, Hand.Off);
            var lastMain = DamageOf(SwingIndex(from, "dualaxes3"), 0, Hand.Main);
            var lastOff = DamageOf(SwingIndex(from, "dualaxes3"), 1, Hand.Off);
            var stepRatio = BaseRatio(secondOff, firstMain);
            SelfTest.Note(DamageName, $"dualaxes0 main-hand hit {DamageText(firstMain)}, dualaxes1 off-hand hit "
                                      + $"{DamageText(secondOff)}, ratio {F2(stepRatio)}; dualaxes3 main-hand hit "
                                      + $"{DamageText(lastMain)}, off-hand hit {DamageText(lastOff)}");
            c.Check(Rolled(firstMain) && Rolled(secondOff) && Mathf.Abs(stepRatio - 0.5f) <= RatioTolerance,
                "the second swing's off-hand hit / the first swing's main-hand hit 0.50 with OffHandDamage 50, without "
                + $"the random skill factor and the multi-object split (got {F2(stepRatio)})");
            var lastMainRatio = BaseRatio(lastMain, firstMain);
            var lastOffRatio = BaseRatio(lastOff, secondOff);
            c.Check(Rolled(lastMain) && Rolled(lastOff) && Mathf.Abs(lastMainRatio - 2f) <= 2f * RatioTolerance
                    && Mathf.Abs(lastOffRatio - 2f) <= 2f * RatioTolerance,
                "both hits of the fourth swing are doubled: main-hand hit / the first swing's 2.00, off-hand hit / the "
                + $"second swing's 2.00 (got {F2(lastMainRatio)} and {F2(lastOffRatio)})");

            // BothHandsDamage 100 against 50 on the special: each hand hit about half.
            ServerRules.TestRules = Rules(both: 100);
            yield return new WaitForSeconds(0.3f);
            var full = DualSwing.Swings.Count;
            yield return Swing(player, dummy, 1, true);
            ServerRules.TestRules = Rules(both: 50);
            yield return new WaitForSeconds(0.3f);
            var half = DualSwing.Swings.Count;
            yield return Swing(player, dummy, 1, true);
            CheckTriggers(c, full, "two specials", "dualaxes_secondary", "dualaxes_secondary");
            var special = template.Secondary;
            var mainRaise = first.m_shared.m_secondaryAttack.m_raiseSkillAmount;
            var offRaise = second.m_shared.m_secondaryAttack.m_raiseSkillAmount;
            foreach (var hand in new[] { Hand.Main, Hand.Off })
            {
                var fullHit = HitOf(full, 0, hand);
                var halfHit = HitOf(half, 0, hand);
                var raise = hand == Hand.Main ? mainRaise : offRaise;
                c.Check(fullHit != null && halfHit != null && Approx(fullHit.DamageMultiplier, special.m_damageMultiplier)
                        && Approx(halfHit.DamageMultiplier, special.m_damageMultiplier * 0.5f)
                        && Approx(halfHit.ForceMultiplier, special.m_forceMultiplier * 0.5f)
                        && Approx(halfHit.RaiseSkillAmount, raise * 0.5f),
                    $"special, {hand} hand: damage x{F2(special.m_damageMultiplier)} at 100%, half of it, half the push and "
                    + $"half a skill raise at 50% (got {(fullHit != null ? F2(fullHit.DamageMultiplier) : "none")} / "
                    + $"{(halfHit != null ? F2(halfHit.DamageMultiplier) + ", force " + F2(halfHit.ForceMultiplier) + ", raise " + F2(halfHit.RaiseSkillAmount) : "none")})");
                var fullDamage = DamageOf(full, 0, hand);
                var halfDamage = DamageOf(half, 0, hand);
                var ratio = BaseRatio(halfDamage, fullDamage);
                SelfTest.Note(DamageName, $"special, {hand} hand: {DamageText(fullDamage)} at BothHandsDamage 100, "
                                          + $"{DamageText(halfDamage)} at 50, ratio {F2(ratio)}");
                c.Check(Rolled(fullDamage) && Rolled(halfDamage),
                    $"special, {hand} hand: each hit's random skill factor and the objects its sweep touched recorded");
                c.Check(Mathf.Abs(ratio - 0.5f) <= RatioTolerance,
                    $"special, {hand} hand: hit at 50% / hit at 100% 0.50, without the random skill factor and the "
                    + $"multi-object split (got {F2(ratio)})");
            }

            // E: HitPattern BothHands: both weapons on every event, adrenaline counted once per event.
            ServerRules.TestRules = Rules(pattern: HitPatternMode.BothHands);
            yield return new WaitForSeconds(0.5f);
            var missAdrenaline = player.m_attackMissAdrenaline;
            from = DualSwing.Swings.Count;
            yield return Swing(player, dummy, 2, false);
            CheckSwings(c, from, "BothHands", "dualaxes0:0M,0O", "dualaxes1:0M,0O");
            var both = DualSwing.Hits.Where(h => h.Swing >= from).ToList();
            var offs = both.Where(h => h.Hand == Hand.Off).ToList();
            var mains = both.Where(h => h.Hand == Hand.Main).ToList();
            c.Check(offs.Count > 0 && offs.All(h => Approx(h.Adrenaline, 0f) && Approx(h.MissAdrenaline, 0f))
                    && mains.All(h => Approx(h.Adrenaline, template.Primary.m_attackAdrenaline) && Approx(h.MissAdrenaline, missAdrenaline)),
                "both-hands events: the off-hand half gains and loses no adrenaline, the main-hand half the template's");
            // Deep North snow shovel: once per event (main-hand half only), clone back to the weapon's value after.
            var shovel = first.m_shared.m_attack.m_snowShovel;
            c.Check(offs.Count > 0 && offs.All(h => !h.SnowShovel) && mains.All(h => h.SnowShovel == shovel)
                    && DualSwing.RecordedClone != null && DualSwing.RecordedClone.m_snowShovel == shovel,
                $"both-hands events: the off-hand half clears no snow, the main-hand half as the weapon ({shovel}), "
                + "the clone has the weapon's value again after");
            c.Check(Approx(player.m_attackMissAdrenaline, missAdrenaline), "the player's miss adrenaline is back after the events");

            // T27: BothHandsDamage 100 (OffHandDamage 100): each of the two numbers of a both-hands hit is as large as
            // a normal hit of that weapon at the same combo step (default rules: Alternate, off hand 100).
            ServerRules.TestRules = Rules();
            yield return new WaitForSeconds(0.5f);
            var plain = DualSwing.Swings.Count;
            yield return Swing(player, dummy, 2, false);
            ServerRules.TestRules = Rules(pattern: HitPatternMode.BothHands, both: 100);
            yield return new WaitForSeconds(0.5f);
            var strong = DualSwing.Swings.Count;
            yield return Swing(player, dummy, 2, false);
            CheckSwings(c, plain, "normal hits, then both-hands hits at 100%", "dualaxes0:0M", "dualaxes1:0O",
                "dualaxes0:0M,0O", "dualaxes1:0M,0O");
            var normalMain = DamageOf(plain, 0, Hand.Main);
            var normalOff = DamageOf(plain + 1, 0, Hand.Off);
            var strongMain = DamageOf(strong, 0, Hand.Main);
            var strongOff = DamageOf(strong + 1, 0, Hand.Off);
            var strongMainRatio = BaseRatio(strongMain, normalMain);
            var strongOffRatio = BaseRatio(strongOff, normalOff);
            SelfTest.Note(DamageName, $"BothHandsDamage 100: main-hand number {DamageText(strongMain)} against a normal "
                                      + $"main-hand hit {DamageText(normalMain)}; off-hand number {DamageText(strongOff)} "
                                      + $"against a normal off-hand hit {DamageText(normalOff)}");
            c.Check(Rolled(normalMain) && Rolled(normalOff) && Rolled(strongMain) && Rolled(strongOff)
                    && Mathf.Abs(strongMainRatio - 1f) <= RatioTolerance && Mathf.Abs(strongOffRatio - 1f) <= RatioTolerance,
                "BothHandsDamage 100: each number of a both-hands hit is as large as a normal hit of that weapon at the "
                + $"same combo step (main hand {F2(strongMainRatio)}, off hand {F2(strongOffRatio)}, 1.00 expected)");
            ServerRules.TestRules = Rules(pattern: HitPatternMode.BothHands);
            yield return new WaitForSeconds(0.3f);
            // A missed both-hands swing (facing away): NOTE the adrenaline.
            Face(player, -Forward(player));
            yield return new WaitForSeconds(0.6f);
            var adrenaline = player.GetAdrenaline();
            var damages = DualSwing.Damages.Count;
            from = DualSwing.Swings.Count;
            yield return Swing(player, dummy, 1, false);
            SelfTest.Note(DamageName, $"missed BothHands swing ({(DualSwing.Swings.Count > from ? DualSwing.Swings[from].Trigger : "none")}): "
                                      + $"adrenaline {F2(adrenaline)} -> {F2(player.GetAdrenaline())} (max {F2(player.GetMaxAdrenaline())}), "
                                      + $"miss adrenaline {F2(missAdrenaline)} per missed event (R8), {DualSwing.Damages.Count - damages} hit(s)");
            c.Check(Approx(player.m_attackMissAdrenaline, missAdrenaline), "the player's miss adrenaline is back after a missed swing");
            c.Report();
        }
        finally
        {
            DualSwing.Recording = false;
            DualSwing.ResetRecords();
            if (bench != null)
            {
                bench.TakeBack();
            }
            if (skills != null)
            {
                skills.Restore();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
        }
    }

    // ---------- dual.fields ----------

    private static IEnumerator RunFields()
    {
        var c = new Checks(FieldsName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(FieldsName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        SkillSave skills = null;
        try
        {
            bench = new Bench(player);
            skills = new SkillSave(player, Skills.SkillType.Swords);
            var main = bench.Give(Niedhogg);
            var blood = bench.Give(NiedhoggBlood);
            var lightning = bench.Give(NiedhoggLightning);
            if (main == null || blood == null || lightning == null)
            {
                c.Check(false, "could not give the Niedhogg swords");
                c.Report();
                yield break;
            }
            DualSwing.ResetRecords();
            DualSwing.Recording = true;
            foreach (var off in new[] { blood, lightning })
            {
                bench.Pair(main, off);
                yield return new WaitForSeconds(0.5f);
                c.Check(Holds(player, main, off), $"{Name(main)} + {Name(off)} paired ({HandsText(player)})");
                var template = MoveTemplates.For(main, off, ServerRules.Current, player);
                if (template == null)
                {
                    c.Check(false, "pair template");
                    continue;
                }
                var from = DualSwing.Swings.Count;
                yield return Swing(player, null, 2, false);
                CheckSwings(c, from, $"{Name(off)} off: two swings", "dualaxes0:0M", "dualaxes1:0O");
                CheckFields(c, from, main, off, false, template.Primary);
                CheckCloneBack(c, main, false, $"{Name(off)} combo");
                if (off == lightning && template.Secondary != null)
                {
                    from = DualSwing.Swings.Count;
                    yield return Swing(player, null, 1, true);
                    CheckSwings(c, from, $"{Name(off)} off: special", "dualaxes_secondary:0M,0O");
                    CheckFields(c, from, main, off, true, template.Secondary);
                    CheckCloneBack(c, main, true, $"{Name(off)} special");
                }
            }
            c.Report();
        }
        finally
        {
            DualSwing.Recording = false;
            DualSwing.ResetRecords();
            if (bench != null)
            {
                bench.TakeBack();
            }
            if (skills != null)
            {
                skills.Restore();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
        }
    }

    // Each hit of the swings from 'from' carry its own weapon's fields (design 2.6); the chain reset is the template's.
    private static void CheckFields(Checks c, int from, ItemDrop.ItemData main, ItemDrop.ItemData off, bool special,
        Attack template)
    {
        var mainAttack = special ? main.m_shared.m_secondaryAttack : main.m_shared.m_attack;
        var offAttack = special && off.HaveSecondaryAttack() ? off.m_shared.m_secondaryAttack : off.m_shared.m_attack;
        var hits = DualSwing.Hits.Where(h => h.Swing >= from).ToList();
        c.Check(hits.Any(h => h.Hand == Hand.Main) && hits.Any(h => h.Hand == Hand.Off),
            $"{Name(off)}{(special ? " special" : "")}: main-hand and off-hand hits recorded");
        foreach (var hit in hits)
        {
            var weapon = hit.Hand == Hand.Off ? off : main;
            var own = hit.Hand == Hand.Off ? offAttack : mainAttack;
            c.Check(ReferenceEquals(hit.Weapon, weapon)
                    && Approx(hit.DamagePerMissingHp, own.m_damageMultiplierPerMissingHP)
                    && Approx(hit.SpawnOnHitChance, own.m_spawnOnHitChance)
                    && hit.SpawnOnHit == own.m_spawnOnHit
                    && hit.SpecialHitType == own.m_specialHitType
                    && hit.SpecialHitSkill == own.m_specialHitSkill
                    && ReferenceEquals(hit.HitEffect, own.m_hitEffect)
                    && ReferenceEquals(hit.TriggerEffect, own.m_triggerEffect)
                    && hit.ResetChainIfHit == template.m_resetChainIfHit,
                $"{hit.Trigger} event {hit.Event} {hit.Hand}: fields of {Name(weapon)} expected (weapon {Name(hit.Weapon)}, "
                + $"low-health bonus {hit.DamagePerMissingHp} vs {own.m_damageMultiplierPerMissingHP}, spawn chance "
                + $"{F2(hit.SpawnOnHitChance)} vs {F2(own.m_spawnOnHitChance)}, special hit {hit.SpecialHitType} vs "
                + $"{own.m_specialHitType}, chain reset {hit.ResetChainIfHit} vs template {template.m_resetChainIfHit})");
        }
    }

    // After the swing the clone hold the main weapon's own fields again.
    private static void CheckCloneBack(Checks c, ItemDrop.ItemData main, bool special, string what)
    {
        var clone = DualSwing.RecordedClone;
        var own = special ? main.m_shared.m_secondaryAttack : main.m_shared.m_attack;
        c.Check(clone != null && ReferenceEquals(clone.m_weapon, main)
                && Approx(clone.m_damageMultiplierPerMissingHP, own.m_damageMultiplierPerMissingHP)
                && Approx(clone.m_spawnOnHitChance, own.m_spawnOnHitChance)
                && clone.m_spawnOnHit == own.m_spawnOnHit
                && clone.m_specialHitType == own.m_specialHitType
                && ReferenceEquals(clone.m_hitEffect, own.m_hitEffect)
                && ReferenceEquals(clone.m_triggerEffect, own.m_triggerEffect)
                && clone.m_snowShovel == own.m_snowShovel,
            $"{what}: after the swing the clone holds the main weapon's own fields again");
    }

    // ---------- dual.visuals ----------

    private static IEnumerator RunVisuals()
    {
        var c = new Checks(VisualsName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(VisualsName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        var camera = GameCamera.instance;
        var cameraDistance = camera != null ? camera.m_distance : 0f;
        var blockingSet = false;
        try
        {
            bench = new Bench(player);
            var sword = bench.Give(Sword);
            var sword2 = bench.Give(Sword);
            var axe = bench.Give(Axe);
            var knifeFlint = bench.Give(KnifeFlintName);
            var knifeBlack = bench.Give(KnifeBlack);
            var knifeBlack2 = bench.Give(KnifeBlack);
            if (sword == null || sword2 == null || axe == null || knifeFlint == null || knifeBlack == null
                || knifeBlack2 == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            bench.Pair(sword, axe);
            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }
            c.Check(Holds(player, sword, axe), $"SwordIron + AxeIron paired ({HandsText(player)})");

            // Every game draw the pair from the player ZDO (design 1.7).
            var zdo = player.m_nview.GetZDO();
            c.Check(zdo != null && zdo.GetInt(ZDOVars.s_leftItem) == axe.m_dropPrefab.name.GetStableHashCode()
                    && zdo.GetInt(ZDOVars.s_rightItem) == sword.m_dropPrefab.name.GetStableHashCode(),
                "player ZDO: LeftItem is the off-hand weapon, RightItem the main weapon");
            // ZSyncAnimation.SetInt store the value under 438569 + hash (vanilla), read there by every other game.
            c.Check(zdo != null && zdo.GetInt(438569 + ZSyncAnimation.GetHash("statei")) == 15,
                "stance in the player ZDO: statei 15");
            var zanim = player.m_zanim;
            SelfTest.Note(VisualsName, $"R7: statei in the player's ZSyncAnimation sync ints = {zanim.m_syncInts.Contains("statei")}, "
                                       + $"statef in its sync floats = {zanim.m_syncFloats.Contains("statef")}");
            SelfTest.Note(VisualsName, $"R8: player m_attackMissAdrenaline = {F2(player.m_attackMissAdrenaline)}");

            // E1: off-hand trails follow SetWeaponTrails.
            var vis = player.m_visEquipment;
            var left = vis != null ? vis.m_leftItemInstance : null;
            c.Check(left != null, "the off-hand weapon is drawn in the left hand");
            SelfTest.Note(VisualsName, $"R6: player VisEquipment.m_useAllTrails = {(vis != null && vis.m_useAllTrails)}");
            if (left != null)
            {
                var trails = left.GetComponentsInChildren<MeleeWeaponTrail>(true);
                SelfTest.Note(VisualsName, $"R6: {trails.Length} MeleeWeaponTrail component(s) on the left {Name(axe)}");
                if (vis.m_useAllTrails)
                {
                    SelfTest.Note(VisualsName, "the player prefab trails every weapon itself: the off-hand trail patch stands aside");
                }
                else if (trails.Length == 0)
                {
                    SelfTest.Note(VisualsName, $"{Name(axe)} has no trail component: nothing for the off-hand trail patch to switch (R6)");
                }
                else
                {
                    // LeftHandTrails on in memory: the player's own setting never changes the result.
                    LeftTrails.TestLeftHandTrails = true;
                    vis.SetWeaponTrails(false);
                    c.Check(TrailsAre(trails, false), "SetWeaponTrails(false): off-hand trails off");
                    vis.SetWeaponTrails(true);
                    var cached = LeftTrails.CachedTrails(vis);
                    c.Check(TrailsAre(trails, true), "SetWeaponTrails(true): off-hand trails on");
                    vis.SetWeaponTrails(false);
                    c.Check(TrailsAre(trails, false) && cached != null && ReferenceEquals(cached, LeftTrails.CachedTrails(vis)),
                        "off again, and the second call reused the cached trail array");
                    LeftTrails.TestLeftHandTrails = false;
                    vis.SetWeaponTrails(true);
                    c.Check(TrailsAre(trails, false), "LeftHandTrails off: the off-hand weapon does not trail");
                    vis.SetWeaponTrails(false);

                    // Swing the off hand not strike (SecondaryMoves = MainWeapon: sword own special, not converted):
                    // no off-hand trail while it runs.
                    LeftTrails.TestLeftHandTrails = true;
                    ServerRules.TestRules = Rules(secondary: SecondaryMovesMode.MainWeapon);
                    var plainStarted = player.StartAttack(null, true);
                    var plain = player.m_currentAttack;
                    vis.SetWeaponTrails(true);
                    c.Check(plainStarted && plain != null && !ReferenceEquals(plain, DualSwing.RecordedClone)
                            && TrailsAre(trails, false),
                        $"the main weapon's own special (SecondaryMoves = MainWeapon, started {plainStarted}): no off-hand trail");
                    vis.SetWeaponTrails(false);
                    yield return WaitIdle(player);
                    ServerRules.TestRules = DualRules.Defaults;
                    LeftTrails.TestLeftHandTrails = null;
                }
            }
            NoteTrailCounts();

            // Screenshots for the unverified looks: R5 grip (sword in the left hand, close), R10 block pose,
            // R11 sheathed pairs.
            bench.Pair(sword, sword2);
            yield return new WaitForSeconds(0.6f);
            if (camera != null)
            {
                camera.m_distance = Mathf.Max(camera.m_minDistance, 1.3f);
            }
            yield return new WaitForSeconds(0.4f);
            SelfTest.Screenshot(VisualsName, "sword-left-hand-closeup");
            yield return null;
            yield return null;
            if (camera != null)
            {
                camera.m_distance = cameraDistance;
            }
            zanim.SetBool("blocking", true);
            blockingSet = true;
            yield return new WaitForSeconds(0.6f);
            SelfTest.Screenshot(VisualsName, "block-pose-sword-pair");
            yield return null;
            yield return null;
            zanim.SetBool("blocking", false);
            blockingSet = false;

            // Sheathed pairs placed by weapon kind (design 2.7, D27), setting forced on in memory. Vanilla put both on
            // one joint with one pose: two SwordIron on m_backMelee overlap exactly; there me cross them in an X.
            if (vis == null)
            {
                c.Check(false, "the player has a VisEquipment");
                c.Report();
                yield break;
            }
            SelfTest.Note(VisualsName, $"back joints: m_backMelee {JointText(vis.m_backMelee)}, m_backTool "
                                       + $"{JointText(vis.m_backTool)} (relative to the player root)");
            BackCross.TestEnabled = true;
            yield return Sheathe(c, player, vis, "two SwordIron", vis.m_backMelee, "sheathed-sword-pair", true);
            // Setting off (and what turning the mod off leaves): both back to the game's own pose, next frame.
            BackCross.TestEnabled = false;
            BackCross.RebuildAll();
            yield return null;
            yield return null;
            CheckVanillaPose(c, vis, vis.m_rightBackItemInstance, vis.m_currentRightBackItemHash, vis.m_backMelee,
                "setting off: main SwordIron");
            CheckVanillaPose(c, vis, vis.m_leftBackItemInstance, vis.m_currentLeftBackItemHash, vis.m_backMelee,
                "setting off: off-hand SwordIron");
            yield return new WaitForSeconds(0.4f);
            SelfTest.Screenshot(VisualsName, "sheathed-sword-pair-vanilla");
            yield return null;
            yield return null;
            BackCross.TestEnabled = true;
            // Record of the first Sheathe gone: the check below must see this rebuild's crossing.
            BackCross.ResetRecord();
            BackCross.RebuildAll();
            yield return null;
            yield return null;
            CheckCrossed(c, vis, vis.m_backMelee, "setting on again: two SwordIron", true);
            player.ShowHandItems();
            yield return null;
            c.Check(Holds(player, sword, sword2), $"sword pair drawn again ({HandsText(player)})");

            // Two different weapons on the same joint.
            bench.Pair(sword, axe);
            yield return new WaitForSeconds(0.3f);
            yield return Sheathe(c, player, vis, "SwordIron + AxeIron", vis.m_backMelee, "sheathed-sword-axe", true);
            player.ShowHandItems();
            yield return null;
            c.Check(Holds(player, sword, axe), $"sword + axe drawn again ({HandsText(player)})");

            // Two knives: vanilla put both on m_backTool (right hip). Main-hand one stay there, off-hand one go to the
            // mirror spot on the left hip (user rule, D27).
            bench.Pair(knifeBlack, knifeBlack2);
            yield return new WaitForSeconds(0.3f);
            yield return SheatheHips(c, player, vis, "two KnifeBlackMetal", "sheathed-knife-pair", true);
            // Setting off: both back on the right hip in the game's own pose (the moved knife too), next frame.
            BackCross.TestEnabled = false;
            BackCross.RebuildAll();
            yield return null;
            yield return null;
            CheckVanillaPose(c, vis, vis.m_rightBackItemInstance, vis.m_currentRightBackItemHash, vis.m_backTool,
                "setting off: main-hand KnifeBlackMetal");
            CheckVanillaPose(c, vis, vis.m_leftBackItemInstance, vis.m_currentLeftBackItemHash, vis.m_backTool,
                "setting off: off-hand KnifeBlackMetal");
            BackCross.TestEnabled = true;
            player.ShowHandItems();
            yield return null;
            c.Check(Holds(player, knifeBlack, knifeBlack2), $"knife pair drawn again ({HandsText(player)})");

            // Two different knives: each at the mirror of its own game pose.
            bench.Pair(knifeFlint, knifeBlack);
            yield return new WaitForSeconds(0.3f);
            yield return SheatheHips(c, player, vis, "KnifeFlint + KnifeBlackMetal", null, false);
            player.ShowHandItems();
            yield return null;
            c.Check(Holds(player, knifeFlint, knifeBlack), $"KnifeFlint + KnifeBlackMetal drawn again ({HandsText(player)})");

            // Knife + sword, either hand: each at the game's own spot and pose (knife at the hip, sword on the back).
            bench.Pair(knifeBlack, sword);
            yield return new WaitForSeconds(0.3f);
            yield return SheatheApart(c, player, vis, "KnifeBlackMetal + SwordIron", vis.m_backTool, vis.m_backMelee,
                "sheathed-knife-sword");
            player.ShowHandItems();
            yield return null;
            c.Check(Holds(player, knifeBlack, sword), $"knife + sword drawn again ({HandsText(player)})");
            bench.Pair(sword, knifeBlack);
            yield return new WaitForSeconds(0.3f);
            yield return SheatheApart(c, player, vis, "SwordIron + KnifeBlackMetal", vis.m_backMelee, vis.m_backTool,
                null);
            player.ShowHandItems();
            yield return null;
            c.Check(Holds(player, sword, knifeBlack), $"sword + knife drawn again ({HandsText(player)})");

            // One weapon sheathed: the game's own pose.
            bench.Empty();
            player.EquipItem(sword);
            yield return null;
            BackCross.ResetRecord();
            player.HideHandItems();
            yield return null;
            yield return null;
            c.Check(vis.m_leftBackItemInstance == null && BackCross.LastRecord == null,
                "one SwordIron sheathed: nothing crossed");
            CheckVanillaPose(c, vis, vis.m_rightBackItemInstance, vis.m_currentRightBackItemHash, vis.m_backMelee,
                "one SwordIron sheathed");
            player.ShowHandItems();
            yield return null;
            BackCross.TestEnabled = null;
            c.Report();
        }
        finally
        {
            if (blockingSet)
            {
                player.m_zanim.SetBool("blocking", false);
            }
            if (camera != null)
            {
                camera.m_distance = cameraDistance;
            }
            if (bench != null)
            {
                bench.TakeBack();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
        }
    }

    // Test the sheath helpers below report under (NOTE lines, screenshots): dual.visuals, or dual.sheath while it runs.
    private static string _visName = VisualsName;

    // Hide a back pair (R path), check the X a couple of frames later (vanilla rebuild the back items in its next
    // visual update), NOTE what BackCross did, screenshot from behind once the draw animation settled, then one from
    // the right side (an X standing off the back shows edge-on from behind).
    private static IEnumerator Sheathe(Checks c, Player p, VisEquipment vis, string what, Transform joint, string shot,
        bool swordReference)
    {
        BackCross.ResetRecord();
        p.HideHandItems();
        yield return null;
        yield return null;
        CheckCrossed(c, vis, joint, what, swordReference);
        SelfTest.Note(_visName, $"{what} sheathed: {BackCross.LastRecord ?? "nothing crossed"}");
        yield return new WaitForSeconds(0.8f);
        // Stance change over (armed idle to unarmed idle: the joint leaned since the rebuild frame): still level.
        CheckSymmetric(c, vis, $"{what}, 0.8 s later");
        SelfTest.Note(_visName, $"{what}: Level turned the X by {BackCross.LastLevelTotal.ToString("0.0", CultureInfo.InvariantCulture)} "
                                   + $"degrees in all since it crossed (biggest single turn "
                                   + $"{BackCross.LastLevelMax.ToString("0.0", CultureInfo.InvariantCulture)} degrees)");
        SelfTest.Screenshot(_visName, shot);
        yield return null;
        yield return null;
        yield return SideShot(p, shot + "-side");
    }

    // Hide a knife pair (R path): one knife per hip a couple of frames later, and again 0.8 s later once the stance
    // change is over (both ride the hips bone, so still mirror images). NOTE what BackCross did; screenshots from
    // behind and from the right side when 'shot' is given, then the pair while walking, jogging and sprinting
    // (GaitHips).
    private static IEnumerator SheatheHips(Checks c, Player p, VisEquipment vis, string what, string shot,
        bool sameKnife)
    {
        BackCross.ResetRecord();
        p.HideHandItems();
        yield return null;
        yield return null;
        c.Check(BackCross.LastLayout == BackCross.Layout.Hips,
            $"{what}: placed by the SetBackEquipped patch as a knife pair (layout {BackCross.LastLayout})");
        c.Check(BackCross.LastPlaneFromBind,
            $"{what}: mirror plane taken from the body's bind pose (not from this frame's pose)");
        CheckHips(c, vis, what, sameKnife);
        SelfTest.Note(_visName, $"{what} sheathed: {BackCross.LastRecord ?? "nothing done"}");
        yield return new WaitForSeconds(0.8f);
        CheckHips(c, vis, $"{what}, 0.8 s later", sameKnife);
        if (shot == null)
        {
            yield break;
        }
        SelfTest.Screenshot(_visName, shot);
        yield return null;
        yield return null;
        yield return SideShot(p, shot + "-side");
        yield return GaitHips(c, p, vis, what, shot);
    }

    // Knife pair on the hips: main-hand knife at the game's own pose on m_backTool; off-hand knife under the hips bone,
    // the mirror image of its own game pose across the hips' centre plane (BackCross.HipsPlane: through the hips bone,
    // from the bind pose), showing its other face; same knife twice: each other's mirror image across that plane.
    // The pair ride the pelvis like knives on a belt (design 2.7, D27): where the pelvis turn against the body (the
    // unarmed idle: 31-43 degrees in the in-world run of 2026-09-30), the knives turn with it, so symmetry about the
    // player root's centre plane is no check, only a NOTE (pelvis angle and how far). What must hold in every pose
    // (CheckHipsPose): each knife on its own side of the pelvis and of the body, and outside its own thigh.
    private static void CheckHips(Checks c, VisEquipment vis, string what, bool sameKnife)
    {
        var hip = vis.m_backTool;
        var rig = KnifeRig.Make(vis);
        if (rig.Hips == null || rig.Main == null || rig.Off == null)
        {
            c.Check(false, $"{what}: {rig.Error}");
            return;
        }
        CheckVanillaPose(c, vis, rig.Main.gameObject, vis.m_currentRightBackItemHash, hip, $"{what}: main-hand knife");
        var r = rig.Main;
        var l = rig.Off;
        c.Check(l.parent == rig.Hips, $"{what}: off-hand knife on the hips bone {rig.Hips.name} (on {ParentName(l)})");
        if (rig.Error != null)
        {
            c.Check(false, $"{what}: {rig.Error}");
            return;
        }
        var rs = rig.MainShape;
        var ls = rig.OffShape;
        var rc = r.TransformPoint(rs.Center);
        var lc = l.TransformPoint(ls.Center);
        var n = rig.Normal;
        var q = rig.Hips.position;
        if (VanillaWorldPose(hip, vis.m_currentLeftBackItemHash, out var vPos, out var vRot))
        {
            var vCenter = vPos + vRot * Vector3.Scale(l.lossyScale, ls.Center);
            var expected = vCenter - 2f * Vector3.Dot(vCenter - q, n) * n;
            var gap = Vector3.Distance(lc, expected);
            var turn = Vector3.Angle(l.rotation * ls.Long, Vector3.Reflect(vRot * ls.Long, n));
            c.Check(gap < 0.01f && turn < 2f,
                $"{what}: off-hand knife = mirror image of its own game pose across the hips' centre plane "
                + $"({Cm(gap)} cm and {Deg(turn)} degrees off)");
            if (ls.Thin.sqrMagnitude > 0.5f)
            {
                var face = Vector3.Angle(l.rotation * ls.Thin, -Vector3.Reflect(vRot * ls.Thin, n));
                c.Check(face < 2f, $"{what}: off-hand knife shows its other face, as a mirror image ({Deg(face)} "
                                   + "degrees off)");
            }
        }
        else
        {
            c.Check(false, $"{what}: the game's own pose of the off-hand knife is known (prefab)");
        }
        var pose = rig.Measure();
        CheckHipsPose(c, what, pose, rig);
        SelfTest.Note(_visName, $"{what}: {PoseText(pose, rig)}");
        if (!sameKnife)
        {
            return;
        }
        var mirroredMain = rc - 2f * Vector3.Dot(rc - q, n) * n;
        var pairGap = Vector3.Distance(lc, mirroredMain);
        var pairTurn = Vector3.Angle(l.rotation * ls.Long, Vector3.Reflect(r.rotation * rs.Long, n));
        c.Check(pairGap < 0.01f && pairTurn < 2f,
            $"{what}: the two knives are mirror images across the hips' centre plane ({Cm(pairGap)} cm and "
            + $"{Deg(pairTurn)} degrees off)");
        // Body's centre plane = the player root's (x = 0): mirror of the main knife there vs the off-hand knife. No
        // check (the pair follow the pelvis, and the pelvis turn against the body in some poses): NOTE only.
        var root = rig.Root;
        var rLocal = root.InverseTransformPoint(rc);
        var lLocal = root.InverseTransformPoint(lc);
        var bodyGap = Vector3.Distance(lLocal, new Vector3(-rLocal.x, rLocal.y, rLocal.z));
        var rl = root.InverseTransformDirection(r.rotation * rs.Long);
        var ll = root.InverseTransformDirection(l.rotation * ls.Long);
        var bodyTurn = Vector3.Angle(ll, new Vector3(-rl.x, rl.y, rl.z));
        SelfTest.Note(_visName, $"{what}: about the body's centre plane (no check: the pair rides the pelvis, "
                                   + $"{Deg(pose.TwistMax)} degrees from it here) the off-hand knife is {Cm(bodyGap)} cm "
                                   + $"and {Deg(bodyTurn)} degrees from the main knife's mirror image");
    }

    // A knife's centre at least this far (metres) on its own side of a centre plane (the hips', the body's).
    private const float HipSide = 0.03f;

    // Shortest bone from a hips child to a child of its own that count as a thigh (a thigh is about 0.4 m).
    private const float MinThighLength = 0.15f;

    // Each knife on its own side of the pelvis (hips' centre plane) and of the body (player root's), at least HipSide,
    // and outside its own thigh: its centre and its lower end out from the thigh bone's line (hip joint to knee), away
    // from the pelvis centre, so never between the legs nor through the middle of a thigh. 'p' is one pose or the
    // least of several (GaitHips).
    private static void CheckHipsPose(Checks c, string what, HipsPose p, KnifeRig rig)
    {
        var over = p.Frames > 1 ? $"; least over {p.Frames} frames" : "";
        c.Check(p.MainPelvis > HipSide && p.OffPelvis > HipSide,
            $"{what}: each knife on its own side of the pelvis (main-hand knife {Cm(p.MainPelvis)} cm right of the "
            + $"hips' centre plane, off-hand knife {Cm(p.OffPelvis)} cm left of it, at least {Cm(HipSide)} cm{over})");
        c.Check(p.MainBody > HipSide && p.OffBody > HipSide,
            $"{what}: one knife on each side of the body (main-hand knife {Cm(p.MainBody)} cm right of the body's "
            + $"centre plane, off-hand knife {Cm(p.OffBody)} cm left of it, at least {Cm(HipSide)} cm; pelvis "
            + $"{Deg(p.TwistMin)}-{Deg(p.TwistMax)} degrees from the body's centre plane{over})");
        if (rig.ThighError != null)
        {
            c.Check(false, $"{what}: {rig.ThighError}");
            return;
        }
        c.Check(p.MainThigh > 0f && p.OffThigh > 0f,
            $"{what}: each knife outside its own thigh, not between the legs (centre and lower end {Cm(p.MainThigh)} cm "
            + $"(main-hand knife, {rig.RightThigh.name}) and {Cm(p.OffThigh)} cm (off-hand knife, {rig.LeftThigh.name}) "
            + $"out from the thigh bone's line{over})");
    }

    private static string PoseText(HipsPose p, KnifeRig rig)
    {
        var thighs = rig.ThighError == null
            ? $"out from the thigh bone's line ({rig.RightThigh.name} to {rig.RightKnee.name}, {rig.LeftThigh.name} to "
              + $"{rig.LeftKnee.name}) by {Cm(p.MainThigh)} / {Cm(p.OffThigh)} cm"
            : "thighs not found";
        return $"pelvis {Deg(p.TwistMin)}-{Deg(p.TwistMax)} degrees from the body's centre plane; main-hand knife "
               + $"{Cm(p.MainPelvis)} cm right of the hips' centre plane ({Cm(p.MainBody)} cm right of the body's), "
               + $"off-hand knife {Cm(p.OffPelvis)} cm left of it ({Cm(p.OffBody)} cm left of the body's); {thighs}"
               + (p.Frames > 1 ? $" (least over {p.Frames} frames)" : "");
    }

    // Knife pair against the body in one pose, or the least of several (Merge). Metres, + = on the side it belongs.
    private struct HipsPose
    {
        internal int Frames;
        internal float MainPelvis;  // main-hand knife centre right of the hips' centre plane
        internal float OffPelvis;   // off-hand knife centre left of it
        internal float MainBody;    // the same about the player root's centre plane
        internal float OffBody;
        internal float MainThigh;   // main-hand knife (centre and lower end, the nearer) out from its thigh bone's line
        internal float OffThigh;
        internal float TwistMin;    // hips' centre plane from the root's (degrees)
        internal float TwistMax;

        internal void Merge(HipsPose p)
        {
            if (Frames == 0)
            {
                this = p;
                return;
            }
            Frames += p.Frames;
            MainPelvis = Mathf.Min(MainPelvis, p.MainPelvis);
            OffPelvis = Mathf.Min(OffPelvis, p.OffPelvis);
            MainBody = Mathf.Min(MainBody, p.MainBody);
            OffBody = Mathf.Min(OffBody, p.OffBody);
            MainThigh = Mathf.Min(MainThigh, p.MainThigh);
            OffThigh = Mathf.Min(OffThigh, p.OffThigh);
            TwistMin = Mathf.Min(TwistMin, p.TwistMin);
            TwistMax = Mathf.Max(TwistMax, p.TwistMax);
        }
    }

    // Sheathed knife pair ready to measure: shapes, hips' centre plane and thighs found once, then Measure every frame
    // (no alloc).
    private sealed class KnifeRig
    {
        internal Transform Root;
        internal Transform Hips;
        internal Transform Main;
        internal Transform Off;
        internal BackCross.Shape MainShape;
        internal BackCross.Shape OffShape;
        internal Vector3 PlaneNormal;   // hips' centre plane normal (body's right) in the hips bone's space
        internal Transform RightThigh;
        internal Transform RightKnee;
        internal Transform LeftThigh;
        internal Transform LeftKnee;
        internal string Error;          // null = knives, shapes and plane ready
        internal string ThighError;     // null = both thighs found

        internal Vector3 Normal => Hips.TransformDirection(PlaneNormal);

        internal static KnifeRig Make(VisEquipment vis)
        {
            var rig = new KnifeRig { Root = vis.transform };
            var hip = vis.m_backTool;
            rig.Hips = hip != null ? hip.parent : null;
            var right = vis.m_rightBackItemInstance;
            var left = vis.m_leftBackItemInstance;
            rig.Main = right != null ? right.transform : null;
            rig.Off = left != null ? left.transform : null;
            if (rig.Hips == null || rig.Main == null || rig.Off == null)
            {
                rig.Error = $"two knives sheathed and a hips bone (main {right != null}, off-hand {left != null}, hips "
                            + $"{rig.Hips != null})";
                return rig;
            }
            var up = rig.Root.up;
            if (!BackCross.Measure(rig.Main, up, out rig.MainShape) || !BackCross.Measure(rig.Off, up, out rig.OffShape)
                || !BackCross.HipsPlane(vis, rig.Hips, out rig.PlaneNormal, out _))
            {
                rig.Error = "both knives have a mesh to measure, and the hips a centre plane";
                return rig;
            }
            var n = rig.Normal;
            if (!FindThigh(rig.Hips, n, 1f, out rig.RightThigh, out rig.RightKnee)
                || !FindThigh(rig.Hips, n, -1f, out rig.LeftThigh, out rig.LeftKnee))
            {
                var children = new List<string>();
                for (var i = 0; i < rig.Hips.childCount; i++)
                {
                    children.Add(rig.Hips.GetChild(i).name);
                }
                rig.ThighError = $"a thigh bone on each side of the pelvis (a bone under {rig.Hips.name} named like a "
                                 + $"leg, with a bone of at least {Cm(MinThighLength)} cm to a child of its own; children "
                                 + $"of {rig.Hips.name}: {string.Join(", ", children.ToArray())})";
            }
            return rig;
        }

        // Thigh of one side: a bone under the hips named like a leg (the rig's bone names are not in .ref, so any name
        // with "leg"; the NOTE says which), a child of the hips or, under a bone of another name (a pelvis bone), a
        // grandchild (never a leg bone's child: that is the shin), its hip joint on that side of the hips' centre
        // plane, the one with the longest bone to a child of its own (the knee).
        private static bool FindThigh(Transform hips, Vector3 n, float side, out Transform thigh, out Transform knee)
        {
            thigh = null;
            knee = null;
            var best = MinThighLength;
            for (var i = 0; i < hips.childCount; i++)
            {
                var child = hips.GetChild(i);
                if (IsLeg(child))
                {
                    TryThigh(child, hips, n, side, ref best, ref thigh, ref knee);
                    continue;
                }
                for (var j = 0; j < child.childCount; j++)
                {
                    var grandchild = child.GetChild(j);
                    if (IsLeg(grandchild))
                    {
                        TryThigh(grandchild, hips, n, side, ref best, ref thigh, ref knee);
                    }
                }
            }
            return thigh != null;
        }

        private static bool IsLeg(Transform t) => t.name.IndexOf("leg", StringComparison.OrdinalIgnoreCase) >= 0;

        private static void TryThigh(Transform candidate, Transform hips, Vector3 n, float side, ref float best,
            ref Transform thigh, ref Transform knee)
        {
            if (side * Vector3.Dot(candidate.position - hips.position, n) <= 0f)
            {
                return;
            }
            for (var k = 0; k < candidate.childCount; k++)
            {
                var end = candidate.GetChild(k);
                var length = Vector3.Distance(candidate.position, end.position);
                if (length > best)
                {
                    best = length;
                    thigh = candidate;
                    knee = end;
                }
            }
        }

        // This frame's pose. Knife's lower end = its centre less half its length along its long axis (which point to
        // the end that was up when measured).
        internal HipsPose Measure()
        {
            var n = Normal;
            var q = Hips.position;
            var rc = Main.TransformPoint(MainShape.Center);
            var lc = Off.TransformPoint(OffShape.Center);
            var rEnd = rc - Main.rotation * MainShape.Long * (MainShape.Length * 0.5f);
            var lEnd = lc - Off.rotation * OffShape.Long * (OffShape.Length * 0.5f);
            var right = Root.right;
            var origin = Root.position;
            var twist = Vector3.Angle(n, right);
            var pose = new HipsPose
            {
                Frames = 1,
                MainPelvis = Vector3.Dot(rc - q, n),
                OffPelvis = -Vector3.Dot(lc - q, n),
                MainBody = Vector3.Dot(rc - origin, right),
                OffBody = -Vector3.Dot(lc - origin, right),
                TwistMin = twist,
                TwistMax = twist,
            };
            if (ThighError == null)
            {
                pose.MainThigh = Mathf.Min(OutOfThigh(rc, RightThigh, RightKnee, n, 1f),
                    OutOfThigh(rEnd, RightThigh, RightKnee, n, 1f));
                pose.OffThigh = Mathf.Min(OutOfThigh(lc, LeftThigh, LeftKnee, n, -1f),
                    OutOfThigh(lEnd, LeftThigh, LeftKnee, n, -1f));
            }
            return pose;
        }

        // How far 'point' lies out from the thigh bone's line (hip joint to knee, nearest point on it), along the
        // pelvis's right for the right thigh (side 1) or its left (side -1). Negative = on the inner side.
        private static float OutOfThigh(Vector3 point, Transform thigh, Transform knee, Vector3 n, float side)
        {
            var a = thigh.position;
            var ab = knee.position - a;
            var t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude) : 0f;
            return side * Vector3.Dot(point - (a + ab * t), n);
        }
    }

    // Knife pair while moving, as T33 asks by hand: walk, jog and sprint in place. Controller off (it write the
    // controls every physics step), Player.SetControls forward each frame with the gait's walk / run flags, the player
    // put back on its spot every frame: the animator's forward speed is the commanded velocity (m_currentVel,
    // Character.UpdateWalking), so a pinned player still plays the gait. Every frame of the last GaitSample seconds
    // measured; the least numbers checked (CheckHipsPose) and NOTEd, one screenshot per gait; each gait's top forward
    // speed checked against the player's own speed for it, so a pass mean the gait really played. Controller, walk
    // toggle, spot, facing and the Run skill (a sprint raise it) put back after.
    private static readonly string[] GaitNames = { "walk", "jog", "sprint" };
    private const float GaitWarmup = 0.6f;
    private const float GaitSample = 1.2f;

    // Top forward speed at least this part of the player's own speed for the gait (armour may slow a little).
    private const float GaitSpeedPart = 0.8f;

    private static IEnumerator GaitHips(Checks c, Player p, VisEquipment vis, string what, string shot)
    {
        var rig = KnifeRig.Make(vis);
        if (rig.Error != null)
        {
            c.Check(false, $"{what}, moving: {rig.Error}");
            yield break;
        }
        var controller = p.GetComponent<PlayerController>();
        var controllerOn = controller != null && controller.enabled;
        var walkBefore = p.GetWalk();
        var spot = p.transform.position;
        var facing = p.transform.rotation;
        var skills = new SkillSave(p, Skills.SkillType.Run);
        var speeds = new[] { p.m_walkSpeed, p.m_speed, p.m_runSpeed };
        try
        {
            if (controller != null)
            {
                controller.enabled = false;
            }
            for (var g = 0; g < GaitNames.Length; g++)
            {
                var sprint = g == 2;
                p.SetWalk(g == 0);
                var least = new HipsPose();
                var top = 0f;
                var running = false;
                var shotTaken = false;
                var start = Time.time;
                while (Time.time - start < GaitWarmup + GaitSample)
                {
                    p.SetControls(Vector3.forward, false, false, false, false, false, false, false, false, sprint, false);
                    yield return null;
                    Pin(p, spot);
                    if (Time.time - start < GaitWarmup)
                    {
                        continue;
                    }
                    least.Merge(rig.Measure());
                    top = Mathf.Max(top, Vector3.Dot(p.m_currentVel, p.transform.forward));
                    running |= p.IsRunning();
                    if (!shotTaken && Time.time - start >= GaitWarmup + GaitSample * 0.5f)
                    {
                        shotTaken = true;
                        SelfTest.Screenshot(_visName, $"{shot}-{GaitNames[g]}");
                    }
                }
                var gait = $"{what}, {GaitNames[g]}";
                SelfTest.Note(_visName, $"{gait}: forward speed up to {F2(top)} m/s (the player's {GaitNames[g]} "
                                           + $"speed {F2(speeds[g])} m/s, sprinting seen {running}); {PoseText(least, rig)}");
                c.Check(top >= GaitSpeedPart * speeds[g] && running == sprint,
                    $"{gait}: the gait played (forward speed up to {F2(top)} m/s, at least {F2(GaitSpeedPart * speeds[g])}; "
                    + $"sprinting seen {running}, expected {sprint})");
                if (least.Frames == 0)
                {
                    c.Check(false, $"{gait}: no frame measured");
                    continue;
                }
                CheckHipsPose(c, gait, least, rig);
            }
        }
        finally
        {
            p.SetControls(Vector3.zero, false, false, false, false, false, false, false, false, false, false);
            p.SetWalk(walkBefore);
            p.m_currentVel = Vector3.zero;
            Pin(p, spot);
            p.transform.rotation = facing;
            if (p.m_body != null)
            {
                p.m_body.rotation = facing;
                p.m_body.linearVelocity = Vector3.zero;
            }
            if (controller != null)
            {
                controller.enabled = controllerOn;
            }
            skills.Restore();
        }
    }

    // Player back on its spot (height left to the ground), sideways speed gone: the gait plays in place.
    private static void Pin(Player p, Vector3 spot)
    {
        var pos = p.transform.position;
        pos.x = spot.x;
        pos.z = spot.z;
        p.transform.position = pos;
        var body = p.m_body;
        if (body != null)
        {
            body.position = pos;
            var v = body.linearVelocity;
            body.linearVelocity = new Vector3(0f, v.y, 0f);
        }
    }

    // Hide a knife + back weapon pair: each stays at the game's own spot and pose (checked at once and 0.8 s later:
    // nothing moved, nothing levelled). NOTE; screenshots from behind and from the right side when 'shot' is given.
    private static IEnumerator SheatheApart(Checks c, Player p, VisEquipment vis, string what, Transform mainJoint,
        Transform offJoint, string shot)
    {
        BackCross.ResetRecord();
        p.HideHandItems();
        yield return null;
        yield return null;
        c.Check(BackCross.LastLayout == BackCross.Layout.Apart,
            $"{what}: seen by the SetBackEquipped patch and left apart (layout {BackCross.LastLayout})");
        SelfTest.Note(_visName, $"{what} sheathed: {BackCross.LastRecord ?? "nothing done"}");
        CheckVanillaPose(c, vis, vis.m_rightBackItemInstance, vis.m_currentRightBackItemHash, mainJoint,
            $"{what}: main-hand weapon");
        CheckVanillaPose(c, vis, vis.m_leftBackItemInstance, vis.m_currentLeftBackItemHash, offJoint,
            $"{what}: off-hand weapon");
        yield return new WaitForSeconds(0.8f);
        CheckVanillaPose(c, vis, vis.m_rightBackItemInstance, vis.m_currentRightBackItemHash, mainJoint,
            $"{what}, 0.8 s later: main-hand weapon");
        CheckVanillaPose(c, vis, vis.m_leftBackItemInstance, vis.m_currentLeftBackItemHash, offJoint,
            $"{what}, 0.8 s later: off-hand weapon");
        if (shot == null)
        {
            yield break;
        }
        SelfTest.Screenshot(_visName, shot);
        yield return null;
        yield return null;
        yield return SideShot(p, shot + "-side");
    }

    // World pose vanilla AttachItem give prefab 'hash' on 'joint' now: local pose = the prefab's equipoffset.
    private static bool VanillaWorldPose(Transform joint, int hash, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        var db = ObjectDB.instance;
        var prefab = db != null ? db.GetItemPrefab(hash) : null;
        if (joint == null || prefab == null)
        {
            return false;
        }
        var offset = prefab.transform.Find("equipoffset");
        position = joint.TransformPoint(offset != null ? offset.position : Vector3.zero);
        rotation = joint.rotation * (offset != null ? offset.rotation : Quaternion.identity);
        return true;
    }

    private static string Cm(float metres) => (metres * 100f).ToString("0.0", CultureInfo.InvariantCulture);

    private static string Deg(float degrees) => degrees.ToString("0.0", CultureInfo.InvariantCulture);

    // Screenshot of the player's right side: body turned a quarter right for one frame. The camera follows the eye's
    // look rotation (GameCamera, m_eye), a child of the body, so the eye keeps its world rotation: the camera stays.
    private static IEnumerator SideShot(Player p, string shot)
    {
        var rotation = p.transform.rotation;
        var eye = p.m_eye != null ? p.m_eye.rotation : Quaternion.identity;
        var turned = Quaternion.AngleAxis(90f, Vector3.up) * rotation;
        p.transform.rotation = turned;
        if (p.m_body != null)
        {
            p.m_body.rotation = turned;
        }
        if (p.m_eye != null)
        {
            p.m_eye.rotation = eye;
        }
        SelfTest.Screenshot(_visName, shot);
        yield return null;
        p.transform.rotation = rotation;
        if (p.m_body != null)
        {
            p.m_body.rotation = rotation;
        }
        yield return null;
    }

    // Both back weapons on 'joint', crossed (angle between their long axes), crossing at their centres, symmetric
    // about the vertical of their plane (mirror images), the plane along the body (its normal near the horizontal
    // direction from the body's axis to the crossing: not standing off the back with a blade through the body), made
    // by the patch. Sword reference: left at the game's own angle (MinAngle-MaxAngle, BackCross comment). Shapes
    // measured the way BackCross does.
    private static void CheckCrossed(Checks c, VisEquipment vis, Transform joint, string what, bool swordReference)
    {
        var right = vis.m_rightBackItemInstance;
        var left = vis.m_leftBackItemInstance;
        if (right == null || left == null || joint == null)
        {
            c.Check(false, $"{what}: two weapons on the back (main {right != null}, off-hand {left != null})");
            return;
        }
        var r = right.transform;
        var l = left.transform;
        c.Check(r.parent == joint && l.parent == joint,
            $"{what}: both on {joint.name} (main on {ParentName(r)}, off-hand on {ParentName(l)})");
        var up = vis.transform.up;
        if (!BackCross.Measure(r, up, out var rs) || !BackCross.Measure(l, up, out var ls))
        {
            c.Check(false, $"{what}: both weapons have a mesh to measure");
            return;
        }
        var rl = r.rotation * rs.Long;
        var ll = l.rotation * ls.Long;
        var angle = Vector3.Angle(rl, ll);
        c.Check(angle >= 2f * BackCross.MinAngle - 2f && angle <= 2f * BackCross.MaxAngle + 2f,
            $"{what}: crossed, {angle.ToString("0.0", CultureInfo.InvariantCulture)} degrees between them");
        var gap = Vector3.Distance(r.TransformPoint(rs.Center), l.TransformPoint(ls.Center));
        c.Check(gap <= BackCross.Lift + 0.01f,
            $"{what}: they cross at their centres ({(gap * 100f).ToString("0.0", CultureInfo.InvariantCulture)} cm apart)");
        var normal = Vector3.Cross(rl, ll);
        if (normal.sqrMagnitude > 1e-6f)
        {
            normal.Normalize();
            CheckSymmetric(c, vis, what);
            var root = vis.transform;
            var crossing = (r.TransformPoint(rs.Center) + l.TransformPoint(ls.Center)) * 0.5f;
            var outward = crossing - root.position;
            outward -= Vector3.Dot(outward, up) * up;
            if (outward.sqrMagnitude < 0.0025f)
            {
                outward = -root.forward;
            }
            outward.Normalize();
            var facing = Vector3.Dot(normal, outward);
            c.Check(Mathf.Abs(facing) > 0.7f,
                $"{what}: the X lies along the body (plane normal {F2(Mathf.Abs(facing))} along the outward direction, "
                + "at least 0.70 wanted)");
            var local = root.InverseTransformDirection(facing < 0f ? -normal : normal);
            SelfTest.Note(_visName, $"{what}: X plane normal (outward) relative to the player: right {F2(local.x)}, "
                                       + $"back {F2(-local.z)}, up {F2(local.y)}");
        }
        c.Check(BackCross.LastLayout == BackCross.Layout.Crossed,
            $"{what}: crossed by the SetBackEquipped patch (layout {BackCross.LastLayout})");
        if (swordReference)
        {
            c.Check(!BackCross.LastTilted,
                $"{what}: the reference sword keeps the game's own angle ({BackCross.LastVanillaAngle.ToString("0.0", CultureInfo.InvariantCulture)} "
                + $"degrees from the vertical, between {BackCross.MinAngle:0} and {BackCross.MaxAngle:0})");
        }
    }

    // Both back weapons at the same angle either side of the body's vertical (the player root's up, projected into the
    // plane of their long axes), now. BackCross.Level keeps it so while the joint leans (design 2.7).
    private static void CheckSymmetric(Checks c, VisEquipment vis, string what)
    {
        var right = vis.m_rightBackItemInstance;
        var left = vis.m_leftBackItemInstance;
        var up = vis.transform.up;
        if (right == null || left == null || !BackCross.Measure(right.transform, up, out var rs)
            || !BackCross.Measure(left.transform, up, out var ls))
        {
            c.Check(false, $"{what}: two measurable weapons on the back (main {right != null}, off-hand {left != null})");
            return;
        }
        var rl = right.transform.rotation * rs.Long;
        var ll = left.transform.rotation * ls.Long;
        var normal = Vector3.Cross(rl, ll);
        if (normal.sqrMagnitude < 1e-6f)
        {
            c.Check(false, $"{what}: the two weapons are not parallel");
            return;
        }
        normal.Normalize();
        var vertical = up - Vector3.Dot(up, normal) * normal;
        var ra = Vector3.Angle(rl, vertical);
        var la = Vector3.Angle(ll, vertical);
        c.Check(vertical.sqrMagnitude > 0.01f && Mathf.Abs(ra - la) < 2f,
            $"{what}: symmetric about the vertical ({ra.ToString("0.0", CultureInfo.InvariantCulture)} and "
            + $"{la.ToString("0.0", CultureInfo.InvariantCulture)} degrees)");
    }

    // Instance exactly where vanilla AttachItem put it: parent 'joint', local pose = the prefab's equipoffset.
    private static void CheckVanillaPose(Checks c, VisEquipment vis, GameObject instance, int hash, Transform joint,
        string what)
    {
        if (instance == null || joint == null)
        {
            c.Check(false, $"{what}: on the back");
            return;
        }
        var db = ObjectDB.instance;
        var prefab = db != null ? db.GetItemPrefab(hash) : null;
        var offset = prefab != null ? prefab.transform.Find("equipoffset") : null;
        var position = offset != null ? offset.position : Vector3.zero;
        var rotation = offset != null ? offset.rotation : Quaternion.identity;
        var t = instance.transform;
        var angle = Quaternion.Angle(t.localRotation, rotation);
        c.Check(t.parent == joint && (t.localPosition - position).sqrMagnitude < 1e-6f && angle < 0.1f,
            $"{what}: the game's own pose on {joint.name} (on {ParentName(t)}, turned "
            + $"{angle.ToString("0.0", CultureInfo.InvariantCulture)} degrees from it)");
    }

    private static string ParentName(Transform t) => t != null && t.parent != null ? t.parent.name : "nothing";

    // Joint relative to the player root: name, parent, position, rotation.
    private static string JointText(Transform joint)
    {
        if (joint == null)
        {
            return "missing";
        }
        var root = joint.root;
        var position = root.InverseTransformPoint(joint.position);
        var euler = (Quaternion.Inverse(root.rotation) * joint.rotation).eulerAngles;
        return $"'{joint.name}' under '{ParentName(joint)}' at ({F2(position.x)}, {F2(position.y)}, {F2(position.z)}) m, "
               + $"rotation ({euler.x.ToString("0", CultureInfo.InvariantCulture)}, "
               + $"{euler.y.ToString("0", CultureInfo.InvariantCulture)}, "
               + $"{euler.z.ToString("0", CultureInfo.InvariantCulture)}) degrees";
    }

    private static bool TrailsAre(MeleeWeaponTrail[] trails, bool on) => trails.All(t => t == null || t._emit == on);

    // R6: trail components on every pairing player weapon prefab.
    private static void NoteTrailCounts()
    {
        var db = ObjectDB.instance;
        if (db == null)
        {
            return;
        }
        var byCount = new SortedDictionary<int, List<string>>();
        foreach (var go in db.m_items)
        {
            var drop = go != null ? go.GetComponent<ItemDrop>() : null;
            if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null
                || drop.m_itemData.m_shared.m_name == null
                || !drop.m_itemData.m_shared.m_name.StartsWith("$item_", StringComparison.Ordinal)
                || !Eligibility.IsEligible(drop.m_itemData))
            {
                continue;
            }
            var count = go.GetComponentsInChildren<MeleeWeaponTrail>(true).Length;
            if (!byCount.TryGetValue(count, out var names))
            {
                byCount[count] = names = new List<string>();
            }
            names.Add(go.name);
        }
        var sb = new StringBuilder("R6: MeleeWeaponTrail components per pairing weapon prefab: ");
        foreach (var pair in byCount)
        {
            sb.Append(pair.Key).Append(" on ").Append(pair.Value.Count).Append(" (")
                .Append(string.Join(", ", pair.Value.ToArray())).Append("); ");
        }
        SelfTest.Note(_visName, sb.ToString());
    }

    // ---------- dual.block ----------

    // Fire hit for real block calls: blockable, no stagger damage (player never stagger, block hold).
    private const float BlockHitFire = 40f;

    // Knife pair block (design 2.6, G12, D29). Formula (pure), then real Humanoid.BlockAttack calls on local player,
    // no attacker (no push back, no parry stagger, no adrenaline): me check what get through, blocker durability
    // drain, knife own values back after each call.
    private static IEnumerator RunBlock()
    {
        var c = new Checks(BlockName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(BlockName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        SkillSave skills = null;
        try
        {
            CheckBlockFormula(c);
            bench = new Bench(player);
            skills = new SkillSave(player, Skills.SkillType.Blocking);
            var template = PrefabItem(DualRules.DefaultKnifePairMoves);
            var black = bench.Give(KnifeBlack);
            var black2 = bench.Give(KnifeBlack);
            var flint = bench.Give(KnifeFlintName);
            var sword = bench.Give(Sword);
            if (template == null || black == null || black2 == null || flint == null || sword == null)
            {
                c.Check(false, "dual.block needs KnifeSkollAndHati, two KnifeBlackMetal, KnifeFlint and SwordIron");
                c.Report();
                yield break;
            }
            NoteKnifeBlocks(template);
            var t = template.m_shared;
            var phys = KnifeBlock.PhysicalDamage(black.m_shared);

            // Two knives same kind: Skoll and Hati block scaled to their damage, its parry bonus.
            bench.Pair(black, black2);
            yield return null;
            c.Check(Holds(player, black, black2), $"two Black Metal knives paired ({HandsText(player)})");
            var expected = t.m_blockPower * phys / KnifeBlock.PhysicalDamage(t);
            c.Check(Mathf.Abs(expected - 24f * 68f / 90f) < 0.01f,
                $"two Black Metal knives block {F2(24f * 68f / 90f)} in the 1.0.16 data (24 x 68 / 90), got {F2(expected)}");
            CheckBlock(c, player, expected, t.m_timedBlockBonus, false, "two Black Metal knives, block");
            CheckBlock(c, player, expected, t.m_timedBlockBonus, true, "two Black Metal knives, parry");

            // Mixed knives: mean damage, whatever hand hold which.
            var mixed = t.m_blockPower * (phys + KnifeBlock.PhysicalDamage(flint.m_shared)) * 0.5f
                        / KnifeBlock.PhysicalDamage(t);
            bench.Pair(black, flint);
            yield return null;
            c.Check(Holds(player, black, flint), $"Black Metal + Flint knives paired ({HandsText(player)})");
            CheckBlock(c, player, mixed, t.m_timedBlockBonus, false, "Black Metal (main) + Flint (off) knives");
            bench.Pair(flint, black);
            yield return null;
            c.Check(Holds(player, flint, black), $"Flint + Black Metal knives paired ({HandsText(player)})");
            CheckBlock(c, player, mixed, t.m_timedBlockBonus, false, "Flint (main) + Black Metal (off) knives");

            // Setting: 0 = off-hand knife alone (vanilla), 200 = twice.
            bench.Pair(black, black2);
            yield return null;
            ServerRules.TestRules = Rules(knifeBlock: 0);
            CheckBlock(c, player, black2.GetBaseBlockPower(), black2.m_shared.m_timedBlockBonus, false,
                "KnifePairBlock 0, block");
            CheckBlock(c, player, black2.GetBaseBlockPower(), black2.m_shared.m_timedBlockBonus, true,
                "KnifePairBlock 0, parry");
            ServerRules.TestRules = Rules(knifeBlock: 200);
            CheckBlock(c, player, expected * 2f, t.m_timedBlockBonus, false, "KnifePairBlock 200");
            ServerRules.TestRules = DualRules.Defaults;

            // Knife next to other weapon, lone knife: as game (off-hand weapon, or knife, block alone).
            bench.Pair(black, sword);
            yield return null;
            c.Check(Holds(player, black, sword), $"knife + sword paired ({HandsText(player)})");
            CheckBlock(c, player, sword.GetBaseBlockPower(), sword.m_shared.m_timedBlockBonus, true,
                "knife (main) + sword (off): the sword parries");
            bench.Pair(sword, black);
            yield return null;
            c.Check(Holds(player, sword, black), $"sword + knife paired ({HandsText(player)})");
            CheckBlock(c, player, black.GetBaseBlockPower(), black.m_shared.m_timedBlockBonus, false,
                "sword (main) + knife (off): the knife blocks alone");
            bench.Empty();
            player.EquipItem(black);
            yield return null;
            c.Check(Holds(player, black, null), $"lone knife in hand ({HandsText(player)})");
            CheckBlock(c, player, black.GetBaseBlockPower(), black.m_shared.m_timedBlockBonus, false,
                "lone knife blocks alone");
            c.Report();
        }
        finally
        {
            if (bench != null)
            {
                bench.TakeBack();
            }
            if (skills != null)
            {
                skills.Restore();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
        }
    }

    // Pure formula of KnifeBlock (literal Skoll and Hati values: block 24, parry x4, physical damage 90).
    private static void CheckBlockFormula(Checks c)
    {
        c.Check(Approx(KnifeBlock.PairBlockPower(24f, 90f, 68f, 68f, 2f, 100), 24f * 68f / 90f),
            "two knives of 68 physical damage block 24 x 68 / 90");
        c.Check(Approx(KnifeBlock.PairBlockPower(24f, 90f, 68f, 10f, 2f, 100), 10.4f),
            "two different knives: mean damage (68 + 10) / 2 = 39, block 10.4");
        c.Check(Approx(KnifeBlock.PairBlockPower(24f, 90f, 90f, 90f, 2f, 100), 24f),
            "knives as strong as Skoll and Hati block 24");
        c.Check(Approx(KnifeBlock.PairBlockPower(24f, 90f, 2f, 2f, 12f, 100), 12f),
            "never below the off-hand knife's own block (wooden knife 12)");
        c.Check(Approx(KnifeBlock.PairBlockPower(24f, 90f, 68f, 68f, 2f, 0), 2f),
            "KnifePairBlock 0: the off-hand knife's own block");
        c.Check(Approx(KnifeBlock.PairBlockPower(24f, 90f, 68f, 68f, 2f, 200), 2f * 24f * 68f / 90f),
            "KnifePairBlock 200 doubles it");
        c.Check(Approx(KnifeBlock.PairBlockPower(24f, 0f, 68f, 68f, 2f, 100), 24f),
            "moves item without physical damage: its block, not scaled");
        c.Check(Approx(KnifeBlock.PairParryBonus(4f, 4f, 100), 4f) && Approx(KnifeBlock.PairParryBonus(4f, 2f, 100), 4f)
                && Approx(KnifeBlock.PairParryBonus(2f, 4f, 100), 4f) && Approx(KnifeBlock.PairParryBonus(4f, 2f, 0), 2f),
            "parry bonus: the moves item's, never below the knife's own; KnifePairBlock 0: the knife's own");
    }

    // One real BlockAttack call: 40 fire from straight ahead, normal block (block timer 1 s) or parry (0.1 s, inside
    // vanilla 0.25 s window). basePower = before Blocking skill (x 1 + skill x 0.5) and parry bonus.
    private static void CheckBlock(Checks c, Player p, float basePower, float parryBonus, bool parry, string what)
    {
        var blocker = p.GetCurrentBlocker();
        if (blocker == null)
        {
            c.Check(false, $"{what}: something blocks");
            return;
        }
        var own = blocker.m_shared;
        var ownBlock = own.m_blockPower;
        var ownPerLevel = own.m_blockPowerPerLevel;
        var ownParry = own.m_timedBlockBonus;
        var power = basePower * (1f + p.GetSkillFactor(Skills.SkillType.Blocking) * 0.5f) * (parry ? parryBonus : 1f);
        var through = HitData.DamageTypes.ApplyArmor(BlockHitFire, power);
        var drainExpected = own.m_useDurability
            ? own.m_useDurabilityDrain * BlockHitFire / power * Game.m_durabilityRate
            : 0f;
        var hit = new HitData();
        hit.m_damage.m_fire = BlockHitFire;
        hit.m_dir = -p.transform.forward;
        hit.m_point = p.GetCenterPoint();
        p.AddStamina(p.GetMaxStamina());
        var durability = blocker.m_durability;
        var timer = p.m_blockTimer;
        bool blocked;
        p.m_blockTimer = parry ? 0.1f : 1f;
        try
        {
            blocked = p.BlockAttack(hit, null);
        }
        finally
        {
            p.m_blockTimer = timer;
        }
        var drain = durability - blocker.m_durability;
        c.Check(blocked && Mathf.Abs(hit.m_damage.m_fire - through) < 0.01f,
            $"{what}: {F2(BlockHitFire)} fire against power {F2(power)} leaves {F2(through)}, got {F2(hit.m_damage.m_fire)}"
            + (blocked ? "" : " (not blocked: hit from behind?)"));
        c.Check(Mathf.Abs(drain - drainExpected) < 0.01f,
            $"{what}: {Name(blocker)} loses {F2(drainExpected)} durability, got {F2(drain)}");
        c.Check(Approx(own.m_blockPower, ownBlock) && Approx(own.m_blockPowerPerLevel, ownPerLevel)
                && Approx(own.m_timedBlockBonus, ownParry),
            $"{what}: {Name(blocker)} has its own block values again after the call (block {F2(own.m_blockPower)}, "
            + $"per level {F2(own.m_blockPowerPerLevel)}, parry x{F2(own.m_timedBlockBonus)})");
    }

    // Two of each player knife (README table), default 100%, Blocking skill 0.
    private static void NoteKnifeBlocks(ItemDrop.ItemData template)
    {
        var db = ObjectDB.instance;
        if (db == null)
        {
            return;
        }
        var t = template.m_shared;
        var parts = new List<string>();
        foreach (var go in db.m_items)
        {
            var drop = go != null ? go.GetComponent<ItemDrop>() : null;
            var item = drop != null ? drop.m_itemData : null;
            if (item == null || item.m_shared == null || item.m_shared.m_skillType != Skills.SkillType.Knives
                || item.m_shared.m_name == null || !item.m_shared.m_name.StartsWith("$item_", StringComparison.Ordinal)
                || !Eligibility.IsEligible(item))
            {
                continue;
            }
            var phys = KnifeBlock.PhysicalDamage(item.m_shared);
            var block = KnifeBlock.PairBlockPower(t.m_blockPower, KnifeBlock.PhysicalDamage(t), phys, phys,
                item.GetBaseBlockPower(1), DualRules.DefaultKnifePairBlock);
            var parry = KnifeBlock.PairParryBonus(t.m_timedBlockBonus, item.m_shared.m_timedBlockBonus,
                DualRules.DefaultKnifePairBlock);
            parts.Add($"{go.name} {F2(phys)} damage: block {F2(block)}, parry {F2(block * parry)}");
        }
        SelfTest.Note(BlockName, $"two of a knife at KnifePairBlock 100, Blocking 0 ({DualRules.DefaultKnifePairMoves} "
                                 + $"blocks {F2(t.m_blockPower)}, parry x{F2(t.m_timedBlockBonus)}, physical damage "
                                 + $"{F2(KnifeBlock.PhysicalDamage(t))}): {string.Join("; ", parts.ToArray())}");
    }
#endif
}
