using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MC.Shared;
using UnityEngine;
using GridPatches = MC.Farming.CultivatorReplantMod.Patches.InventoryGridPatches;
using HealPatches = MC.Farming.CultivatorReplantMod.Patches.InventoryGuiHealPatches;
using Object = UnityEngine.Object;
#endif

namespace MC.Farming.CultivatorReplantMod;

// Debug build only (calls vanish in Release). In-world self tests, run by world probe (tools/Test-InWorld.ps1,
// -Mod Cultivator.Replant). Design 8:
//   replant.network  rules on the wire (round trip, wild values clamped, unknown layout and cut-off refused), cost per
//                    level, which rules apply (own / server / pending), push debounce, join check verdicts (pure),
//                    root range minimum above Yggdrasil grow radius
//   replant.content  every transplant item and sapling registered (item database, network list, piece table), fields
//                    and where-it-grows text per plant table, vanilla produce untouched, pieces on/off with feature
//                    and pending rules (pending flip = rebuild at once), grow time and root range follow rules, also
//                    on saplings already in the world, pending hold set = our saplings only, root search mask = root
//                    collider layers; NOTE real wild plant and root values
//   replant.tiers    max quality 7 / 3 with feature and pending, rows and amounts per level 2-7, idol row 0 past level
//                    1, Forge level per level, stale copy healed (also by always-on crafting list prefix, feature
//                    off), other mod swap recipe array = resync, other cultivator recipe hide level 4+ upgrade,
//                    tooltip lines, cost without valid material caps the level, level 3 -> 4 upgrade in the real Forge
//                    Upgrade tab (Forge + 3 extensions), Forge of Potential hide the cultivator
//   replant.action   spawned bushes and forage: hint over plant, Replant level 1 (transplant + berries, bush gone),
//                    level 3 on cloudberry refused, level 4 ok, Yggdrasil placing hint (both limits), picked forage,
//                    another player's sapling back, full inventory drop, ward refuse, pending refuse; screenshots
//   replant.grow     every sapling held while rules pending (vanilla sapling not), forced to grow (Plant.Grow) become
//                    the vanilla plant, ripe, scale in range; wrong biome (fiddlehead, smoke puff, lingonberry in
//                    Meadows) wait, never vanish
//   replant.root     spawned Ancient Root + Yggdrasil sapling: hover, drain at grow, wait at low sap, pending wait, root
//                    of another peer (drain ledger: two saplings one drain each, third wait), no root wait; screenshot
//   replant.icons    transplant icons painted, sprout in upper right (stack text band untouched), tier gems 4-7,
//                    GetIcon gem; PNG export (package icon); inventory: level 4-7 no level number, 3 keep it, number
//                    back with grid patch off; screenshot with every transplant and cultivators level 3-7
// Newer tests live in SelfTestsKeys / Plant / Forge / Off / Far / Mp .cs (same class, helpers in SelfTestsHelpers.cs):
// real button presses, placing through the vanilla ghost, growth on the clock, real Forge upgrades of every level, the
// mod really turned off and on, far biomes, and multiplayer tests against a dedicated server. Registered from here
// (RegisterMore).
// Me force rules only with ServerRules.TestRules / TestPending and feature off with Plugin.TestInactive: never config.
// Rig put back controls, look, time, auto pickup, crafting station, known recipe, inventory and equipment, and destroy
// all spawned things and new item drops.
internal static partial class SelfTests
{
    private const string NetworkName = "replant.network";
    private const string ContentName = "replant.content";
    private const string TiersName = "replant.tiers";
    private const string ActionName = "replant.action";
    private const string GrowName = "replant.grow";
    private const string RootName = "replant.root";
    private const string IconsName = "replant.icons";

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        SelfTest.Register(NetworkName, RunNetwork);
        SelfTest.Register(ContentName, RunContent);
        SelfTest.Register(TiersName, RunTiers);
        SelfTest.Register(ActionName, RunAction);
        SelfTest.Register(GrowName, RunGrow);
        SelfTest.Register(RootName, RunRoot);
        SelfTest.Register(IconsName, RunIcons);
        RegisterMore();
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        SelfTest.Unregister(NetworkName);
        SelfTest.Unregister(ContentName);
        SelfTest.Unregister(TiersName);
        SelfTest.Unregister(ActionName);
        SelfTest.Unregister(GrowName);
        SelfTest.Unregister(RootName);
        SelfTest.Unregister(IconsName);
        UnregisterMore();
        ClearOverrides();
#endif
    }

#if DEBUG
    private static void ClearOverrides()
    {
        ServerRules.TestPending = false;
        ServerRules.TestRules = null;
        Plugin.TestInactive = false;
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

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string F(Vector3 v) => $"({F(v.x)}, {F(v.y)}, {F(v.z)})";

    private static bool Near(float a, float b, float tolerance = 0.001f) => Mathf.Abs(a - b) <= tolerance;

    private static IEnumerator Frames(int n)
    {
        for (var i = 0; i < n; i++)
        {
            yield return null;
        }
    }

    // Spawned objects in physics queries for sure (new colliders join the scene at the next simulation step).
    private static IEnumerator Settle()
    {
        yield return null;
        yield return new WaitForFixedUpdate();
        yield return null;
    }

    private static int _spaceMask;
    private static int _roofMask;

    // Layers vanilla Plant.HaveGrowSpace look at, plus items (forage) and ships.
    private static int SpaceMask => _spaceMask != 0
        ? _spaceMask
        : _spaceMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "item",
            "vehicle");

    private static int RoofMask => _roofMask != 0
        ? _roofMask
        : _roofMask = LayerMask.GetMask("Default", "static_solid", "piece");

    // Free spot on land near center: tries each distance, 24 directions starting at forward. Free = nothing in the grow
    // space layers within clear of the ground point, open sky, not under water, not a cliff, not near a spot already
    // used (taken). accept = extra test (root reach). Same biome as the centre ground: Meadows here. maxRise = how
    // much higher or lower than the centre the spot may lie (far spots on a mountain: more).
    private static bool FindSpot(Vector3 center, Vector3 forward, float[] distances, float clear, List<Vector3> taken,
        out Vector3 spot, Func<Vector3, bool> accept = null, float maxRise = 2.5f)
    {
        spot = Vector3.zero;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
        var zs = ZoneSystem.instance;
        var water = zs.m_waterLevel;
        foreach (var d in distances)
        {
            for (var step = 0; step < 24; step++)
            {
                var angle = (step % 2 == 0 ? 1f : -1f) * ((step + 1) / 2) * 15f;
                var dir = Quaternion.Euler(0f, angle, 0f) * forward;
                var p = center + dir * d;
                p.y = zs.GetGroundHeight(p);
                if (p.y < water + 0.5f || Mathf.Abs(p.y - center.y) > maxRise)
                {
                    continue;
                }
                if (taken != null && taken.Any(t => (t - p).sqrMagnitude < 4f * 4f))
                {
                    continue;
                }
                if (Physics.CheckCapsule(p + Vector3.up * 0.1f, p + Vector3.up * 3f, clear, SpaceMask,
                        QueryTriggerInteraction.Ignore))
                {
                    continue;
                }
                if (Physics.Raycast(p + Vector3.up * 0.2f, Vector3.up, 100f, RoofMask))
                {
                    continue;
                }
                if (accept != null && !accept(p))
                {
                    continue;
                }
                spot = p;
                taken?.Add(p);
                return true;
            }
        }
        return false;
    }

    private static int CountByName(Inventory inv, string sharedName) => inv.CountItems(sharedName, -1, false);

    // Item drops lying near pos whose item has this shared name.
    private static List<ItemDrop> DropsNear(Vector3 pos, string sharedName, float radius = 2f)
    {
        var list = new List<ItemDrop>();
        foreach (var drop in ItemDrop.s_instances)
        {
            if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null)
            {
                continue;
            }
            if (drop.m_itemData.m_shared.m_name != sharedName)
            {
                continue;
            }
            var d = drop.transform.position - pos;
            d.y = 0f;
            if (d.sqrMagnitude <= radius * radius)
            {
                list.Add(drop);
            }
        }
        return list;
    }

    private static string SharedName(string itemPrefab)
    {
        var go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(itemPrefab) : null;
        var drop = go != null ? go.GetComponent<ItemDrop>() : null;
        return drop != null ? drop.m_itemData.m_shared.m_name : "?" + itemPrefab;
    }

    private static string L(string token) => Localization.instance != null ? Localization.instance.Localize(token) : token;

    private static int PrefabHash(GameObject go)
    {
        var nv = go != null ? go.GetComponent<ZNetView>() : null;
        return nv != null && nv.IsValid() ? nv.GetZDO().GetPrefab() : 0;
    }

    private static bool Alive(ZNetView nv) => nv != null && nv.IsValid();

    // Vanilla pick roll a skill bonus (Pickable.Interact: chance = skill of the plant x m_maxLevelBonusChance, then
    // m_bonusYieldAmount more and log line "Bonus food picked!"). Mod dig call that same Interact, so same roll. It is
    // random: run of 2026-10-08 gave 2 yellow mushrooms for 1 in replant.bronze. Me switch the roll off on this one
    // test plant (the prefab stay as it is): drop counts of the tests are then exact, by hand and by dig.
    private static void NoSkillBonus(GameObject go)
    {
        var pick = go != null ? go.GetComponent<Pickable>() : null;
        if (pick != null)
        {
            pick.m_maxLevelBonusChance = 0f;
        }
    }

    // Center of the object's colliders the interact ray can hit (no view-block layer).
    private static Vector3 AimPoint(GameObject go)
    {
        var mask = Player.m_localPlayer != null ? Player.m_localPlayer.m_interactMask : ~0;
        var have = false;
        var bounds = new Bounds();
        foreach (var col in go.GetComponentsInChildren<Collider>())
        {
            if (!col.enabled || col.isTrigger || (mask & (1 << col.gameObject.layer)) == 0)
            {
                continue;
            }
            if (!have)
            {
                bounds = col.bounds;
                have = true;
            }
            else
            {
                bounds.Encapsulate(col.bounds);
            }
        }
        return have ? bounds.center : go.transform.position + Vector3.up * 0.5f;
    }

    // Me = what the tests change on the player and world. Restore (finally, no yield) put everything back.
    private sealed class Rig
    {
        private struct Slot
        {
            internal ItemDrop.ItemData Item;
            internal int Stack;
            internal Vector2i Pos;
        }

        internal readonly Player Player;
        internal readonly Vector3 Origin;
        private readonly string _name;
        private readonly float _pitch;
        private readonly Quaternion _yaw;
        private readonly ItemDrop.ItemData _right;
        private readonly ItemDrop.ItemData _left;
        private readonly List<Slot> _inventory = new List<Slot>();
        private readonly HashSet<ItemDrop.ItemData> _inventorySet = new HashSet<ItemDrop.ItemData>();
        private readonly HashSet<ItemDrop> _drops;
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly bool _autoPickup;
        private readonly CraftingStation _station;
        private PlayerController _controller;
        private bool _controllerEnabled;
        private bool _envSaved;
        private bool _todOn;
        private float _tod;
        private string _knownRecipe;
        // Extra things a test changed (player place, key state, stamina rate, real turn off...): Restore run them
        // first, last added first.
        private readonly List<Action> _undo = new List<Action>();
        // Player moved away from Origin by a test (put back once, through the undo list).
        internal bool Moved;

        internal void Undo(Action undo)
        {
            if (undo != null)
            {
                _undo.Add(undo);
            }
        }

        internal Rig(Player player, string name)
        {
            Player = player;
            _name = name;
            Origin = player.transform.position;
            _pitch = player.m_lookPitch;
            _yaw = player.m_lookYaw;
            _right = player.GetRightItem();
            _left = player.GetLeftItem();
            foreach (var item in player.GetInventory().GetAllItems())
            {
                _inventory.Add(new Slot { Item = item, Stack = item.m_stack, Pos = item.m_gridPos });
                _inventorySet.Add(item);
            }
            _drops = new HashSet<ItemDrop>(ItemDrop.s_instances);
            _autoPickup = Player.m_enableAutoPickup;
            _station = player.m_currentStation;
        }

        internal Inventory Inv => Player.GetInventory();

        // Test name (notes of the helpers).
        internal string Name => _name;

        internal ItemDrop.ItemData Give(string prefab, int stack = 1, int quality = 1)
        {
            return Inv.AddItem(prefab, stack, quality, 0, 0L, "", false);
        }

        internal GameObject Spawn(string prefabName, Vector3 pos, Quaternion rot)
        {
            var prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null)
            {
                return null;
            }
            var go = Object.Instantiate(prefab, pos, rot);
            NoSkillBonus(go);
            _spawned.Add(go);
            return go;
        }

        // Grown plant made by vanilla Grow: me destroy it at the end too.
        internal void Track(GameObject go)
        {
            if (go != null)
            {
                NoSkillBonus(go);
                _spawned.Add(go);
            }
        }

        internal void Destroy(GameObject go)
        {
            if (go != null && ZNetScene.instance != null)
            {
                ZNetScene.instance.Destroy(go);
            }
        }

        // Upgrade tab list only recipes the player know: me teach it, and forget it again at the end.
        internal void Know(Recipe recipe)
        {
            var key = recipe.m_item.m_itemData.m_shared.m_name;
            if (Player.m_knownRecipes.Add(key))
            {
                _knownRecipe = key;
            }
        }

        internal void NoAutoPickup() => Player.m_enableAutoPickup = false;

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
            Player.SetControls(Vector3.zero, false, false, false, false, false, false, false, false, false, false);
        }

        internal void Noon()
        {
            var env = EnvMan.instance;
            if (env == null)
            {
                return;
            }
            if (!_envSaved)
            {
                _envSaved = true;
                _todOn = env.m_debugTimeOfDay;
                _tod = env.m_debugTime;
            }
            env.m_debugTimeOfDay = true;
            env.m_debugTime = 0.5f;
        }

        internal void Look(float yaw, float pitch)
        {
            Player.m_lookYaw = Quaternion.Euler(0f, yaw, 0f);
            Player.m_lookPitch = pitch;
            Player.UpdateEyeRotation();
            Player.m_lookDir = Player.m_eye.forward;
        }

        // Look along dir (vanilla pitch: positive = down).
        internal void LookDir(Vector3 dir)
        {
            var flat = new Vector2(dir.x, dir.z).magnitude;
            Look(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, -Mathf.Atan2(dir.y, Mathf.Max(0.001f, flat)) * Mathf.Rad2Deg);
        }

        // Camera ray (FindHoverObject) through target: aim from the camera, again once the camera followed.
        internal IEnumerator AimAt(Vector3 target)
        {
            LookDir(target - Player.m_eye.position);
            for (var i = 0; i < 5; i++)
            {
                yield return null;
                yield return null;
                var cam = GameCamera.instance;
                if (cam == null)
                {
                    yield break;
                }
                LookDir(target - cam.transform.position);
            }
            yield return null;
            yield return null;
        }

        internal void Restore()
        {
            for (var i = _undo.Count - 1; i >= 0; i--)
            {
                Try("undo " + i, _undo[i]);
            }
            _undo.Clear();
            ClearOverrides();
            Try("content rebuild", TransplantContent.Rebuild);
            Try("gui", () =>
            {
                if (InventoryGui.instance != null && InventoryGui.IsVisible())
                {
                    InventoryGui.instance.Hide();
                }
            });
            Try("station", () => Player.m_currentStation = _station);
            Try("spawned", () =>
            {
                for (var i = _spawned.Count - 1; i >= 0; i--)
                {
                    Destroy(_spawned[i]);
                }
                _spawned.Clear();
            });
            Try("drops", () =>
            {
                foreach (var drop in ItemDrop.s_instances.ToArray())
                {
                    if (drop != null && !_drops.Contains(drop) && (drop.transform.position - Origin).sqrMagnitude < 80f * 80f)
                    {
                        Destroy(drop.gameObject);
                    }
                }
            });
            Try("controls", () =>
            {
                if (_controller != null)
                {
                    Player.SetControls(Vector3.zero, false, false, false, false, false, false, false, false, false, false);
                    _controller.enabled = _controllerEnabled;
                }
            });
            Try("look", () =>
            {
                Player.m_lookYaw = _yaw;
                Player.m_lookPitch = _pitch;
                Player.UpdateEyeRotation();
            });
            Try("time", () =>
            {
                if (_envSaved && EnvMan.instance != null)
                {
                    EnvMan.instance.m_debugTimeOfDay = _todOn;
                    EnvMan.instance.m_debugTime = _tod;
                }
            });
            Try("auto pickup", () => Player.m_enableAutoPickup = _autoPickup);
            Try("known recipe", () =>
            {
                if (_knownRecipe != null)
                {
                    Player.m_knownRecipes.Remove(_knownRecipe);
                }
            });
            Try("inventory", () =>
            {
                var inv = Inv;
                foreach (var item in inv.GetAllItems().ToArray())
                {
                    if (_inventorySet.Contains(item))
                    {
                        continue;
                    }
                    if (Player.IsItemEquiped(item))
                    {
                        Player.UnequipItem(item, false);
                    }
                    inv.RemoveItem(item);
                }
                foreach (var slot in _inventory)
                {
                    slot.Item.m_stack = slot.Stack;
                    if (!inv.ContainsItem(slot.Item))
                    {
                        inv.AddItem(slot.Item, slot.Pos);
                    }
                }
                inv.Changed();
                if (_right != null && inv.ContainsItem(_right) && !Player.IsItemEquiped(_right))
                {
                    Player.EquipItem(_right, false);
                }
                if (_left != null && inv.ContainsItem(_left) && !Player.IsItemEquiped(_left))
                {
                    Player.EquipItem(_left, false);
                }
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
                SelfTest.Note(_name, $"restore {what} failed: {e.Message}");
            }
        }
    }

    // Defaults of the design, whatever the tester's config says.
    private static void UseDefaultRules()
    {
        ServerRules.TestRules = new CultivatorRules();
        TransplantContent.Rebuild();
    }

    private static Piece.Requirement Row(Recipe recipe, string itemPrefab)
    {
        foreach (var r in recipe.m_resources)
        {
            if (r != null && r.m_resItem != null && r.m_resItem.name == itemPrefab)
            {
                return r;
            }
        }
        return null;
    }

    // Number as the in-game texts write it ("2", "6", "2.5").
    private static string F1(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    // Where-it-grows sentence the item and piece text must carry (lead decisions C20, C22).
    private static string WhereText(PlantKind kind)
    {
        if (kind.NeedsRoot)
        {
            return "Must be planted within a few metres of an Ancient Root in the Mistlands, but not right against it.";
        }
        if (kind.OnlyInBiome && kind.Biome == Heightmap.Biome.AshLands)
        {
            return "Grows only in the Ashlands.";
        }
        if (kind.OnlyInBiome && kind.Biome == Heightmap.Biome.DeepNorth)
        {
            return "Grows only in the Deep North.";
        }
        return "Grows on open ground in any land biome, no tilling needed; in the Ashlands, the Mountains and the Deep "
               + "North only inside a shield.";
    }

    // Fake peer and player ids: never 0 (0 = everybody / nobody), never ours.
    private const long OtherPeer = 0x4D43_5465_7374_31L;
    private const long OtherPlayer = 0x4D43_5465_7374_32L;

    // Root ZDO owned by another peer (true) or by us again (false). Drain RPC to that peer go nowhere (no such peer):
    // our copy keep its level, like a drain still on its way. Only around one synchronous call: ZDOMan would claim
    // it back within 2 s, and ZNetScene.Destroy keep the ZDO of an object we do not own.
    private static void LendRoot(ResourceRoot root, bool toOther)
    {
        var nview = root.m_nview;
        if (nview == null || !nview.IsValid())
        {
            return;
        }
        if (toOther)
        {
            nview.GetZDO().SetOwner(OtherPeer == ZDOMan.GetSessionID() ? OtherPeer + 1 : OtherPeer);
        }
        else
        {
            nview.ClaimOwnership();
        }
    }

    // Our patch method on original, under this Harmony id? declaring = our patch class.
    private static bool HasPatch(IEnumerable<Patch> patches, Type declaring, string owner) =>
        patches != null && patches.Any(p => p.PatchMethod != null && p.PatchMethod.DeclaringType == declaring && p.owner == owner);

    // Pixels the sprout paint changed (painted vs plain produce icon, row 0 = bottom): in the upper-right quarter, and
    // below 45% of the height (stack text band along the slot bottom, old lower-right place). False = no copy.
    private static bool SproutSpread(Sprite plain, Sprite painted, out int upperRight, out int low, out string size)
    {
        upperRight = 0;
        low = 0;
        var a = IconPainter.CopyPixels(plain, null, out var w, out var h);
        var b = IconPainter.CopyPixels(painted, null, out var w2, out var h2);
        size = $"{w}x{h} / {w2}x{h2}";
        if (a == null || b == null || w != w2 || h != h2 || a.Length != b.Length)
        {
            return false;
        }
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = y * w + x;
                if (!Differs(a[i], b[i]))
                {
                    continue;
                }
                if (y < h * 0.45f)
                {
                    low++;
                }
                else if (x >= w / 2 && y >= h / 2)
                {
                    upperRight++;
                }
            }
        }
        return true;
    }

    // GPU round trip may move a byte by 1-2: real paint change much more.
    private static bool Differs(Color32 p, Color32 q) =>
        Math.Abs(p.r - q.r) > 24 || Math.Abs(p.g - q.g) > 24 || Math.Abs(p.b - q.b) > 24 || Math.Abs(p.a - q.a) > 24;

    // ---------- replant.network ----------

    private static IEnumerator RunNetwork()
    {
        var c = new Checks(NetworkName);

        var own = new CultivatorRules
        {
            BlackMetalLevel = "BlackMetal:3",
            EitrLevel = "Eitr:7,Sap:2",
            FlametalLevel = "FlametalNew:1",
            BloodgoldLevel = "Gold:9",
            GrowTimeMultiplier = 0.5f,
            RootSapCost = 12,
            RootRange = 9f,
        };
        var pkg = new ZPackage();
        own.Write(pkg);
        pkg.SetPos(0);
        c.Check(CultivatorRules.TryRead(pkg, out var back, out var clamped) && !clamped, "round trip read");
        if (back != null)
        {
            c.Check(back.Describe() == own.Describe(), $"round trip same rules ({back.Describe()})");
            c.Check(!back.IsPending, "rules from the wire are never pending");
        }

        var wild = new ZPackage();
        wild.Write(CultivatorRules.Layout);
        wild.Write(new string('x', CultivatorRules.TextMax + 50));
        wild.Write("Eitr:15");
        wild.Write("FlametalNew:5");
        wild.Write("Gold:5");
        wild.Write(float.NaN);
        wild.Write(99);
        wild.Write(0.5f);
        wild.SetPos(0);
        c.Check(CultivatorRules.TryRead(wild, out var w, out var wc) && wc, "wild values read and flagged");
        if (w != null)
        {
            c.Check(w.BlackMetalLevel.Length == CultivatorRules.TextMax, "long cost cut");
            c.Check(Near(w.GrowTimeMultiplier, CultivatorRules.Default.GrowTimeMultiplier), "NaN grow time = default");
            c.Check(w.RootSapCost == CultivatorRules.RootSapCostMax, $"sap cost clamped to {CultivatorRules.RootSapCostMax} ({w.RootSapCost})");
            c.Check(Near(w.RootRange, CultivatorRules.RootRangeMin), $"root range clamped to {F(CultivatorRules.RootRangeMin)} ({F(w.RootRange)})");
        }

        // Root range minimum (C8): at the old 2 m no Yggdrasil transplant ever had room to grow. 3 m now pulled up.
        var yggKind = PlantCatalog.ByKey("YggaShoot");
        c.Check(yggKind != null && CultivatorRules.RootRangeMin - yggKind.GrowRadius >= 1f,
            $"root range minimum {F(CultivatorRules.RootRangeMin)} m leaves room past the Yggdrasil grow radius {(yggKind != null ? F(yggKind.GrowRadius) : "?")} m");
        var near = new ZPackage();
        new CultivatorRules { RootRange = 3f }.Write(near);
        near.SetPos(0);
        c.Check(CultivatorRules.TryRead(near, out var nr, out var nrc) && nrc && nr != null && Near(nr.RootRange, CultivatorRules.RootRangeMin),
            $"root range 3 from the wire pulled up to {F(CultivatorRules.RootRangeMin)} ({(nr != null ? F(nr.RootRange) : "-")})");

        var other = new ZPackage();
        other.Write(CultivatorRules.Layout + 1);
        other.SetPos(0);
        c.Check(!CultivatorRules.TryRead(other, out _, out _), "unknown layout refused");
        var cut = new ZPackage();
        cut.Write(CultivatorRules.Layout);
        cut.Write("BlackMetal:5");
        cut.SetPos(0);
        c.Check(!CultivatorRules.TryRead(cut, out _, out _), "cut-off package refused");

        var d = CultivatorRules.Default;
        c.Check(d.CostOf(4) == "BlackMetal:5,LinenThread:10" && d.CostOf(5) == "Eitr:15" && d.CostOf(6) == "FlametalNew:5"
                && d.CostOf(7) == "Gold:5", "default cost per level 4-7");
        c.Check(d.CostOf(3) == "" && d.CostOf(8) == "" && d.CostOf(1) == "", "no cost outside levels 4-7");
        c.Check(Near(d.GrowTimeMultiplier, 1f) && d.RootSapCost == 20 && Near(d.RootRange, 6f), "default numbers");

        var mine = CultivatorRules.Default;
        var server = new CultivatorRules { RootSapCost = 5 };
        c.Check(ReferenceEquals(ServerRules.Select(false, server, mine), mine), "not client = own");
        c.Check(ReferenceEquals(ServerRules.Select(true, server, mine), server), "client = server's");
        c.Check(ServerRules.Select(true, null, mine).IsPending, "client without server rules = pending");
        var p = CultivatorRules.Pending;
        c.Check(p.IsPending && p.CostOf(4) == "" && p.CostOf(7) == "", "pending has no costs");
        c.Check(ServerRules.Settled(10f, 10f - ServerRules.PushDelay) && !ServerRules.Settled(10f, 9.9f), "push debounce");

        // Test overrides win, pending over test rules.
        try
        {
            var forced = new CultivatorRules { RootSapCost = 33 };
            ServerRules.TestRules = forced;
            c.Check(ReferenceEquals(ServerRules.Current, forced), "test rules in force");
            ServerRules.TestPending = true;
            c.Check(ServerRules.Current.IsPending && ServerRules.IsPending, "test pending wins");
        }
        finally
        {
            ClearOverrides();
        }
        c.Check(!ServerRules.Current.IsPending, "single player = own rules, not pending");
        c.Note("rules in force: " + ServerRules.Current.Describe());

        c.Check(PlayerCheck.Decide(true, true, true, false, true, false) == JoinVerdict.Compatible, "join: compatible");
        c.Check(PlayerCheck.Decide(true, true, true, false, false, false) == JoinVerdict.Refuse, "join: refuse");
        c.Check(PlayerCheck.Decide(true, true, true, false, false, true) == JoinVerdict.Allowed, "join: allowed");
        c.Check(PlayerCheck.Decide(false, true, true, false, false, false) == JoinVerdict.Skip, "join: not server");
        c.Check(PlayerCheck.Decide(true, true, true, true, false, false) == JoinVerdict.Skip, "join: being kicked");
        c.Check(PlayerCheck.Decide(true, false, true, false, false, false) == JoinVerdict.Skip, "join: gone");

        c.Check(Replant.TierNeededText(4) == "Needs a black metal cultivator (level 4)"
                && Replant.TierNeededText(5) == "Needs an eitr cultivator (level 5)", "tier needed text");
        c.Report();
        yield break;
    }

    // ---------- replant.content ----------

    private static IEnumerator RunContent()
    {
        var c = new Checks(ContentName);
        var db = ObjectDB.instance;
        var scene = ZNetScene.instance;
        if (db == null || scene == null || Player.m_localPlayer == null)
        {
            SelfTest.Fail(ContentName, "no item database, network list or player");
            yield break;
        }
        // Rig: saplings spawned for the grow time checks go away again (Restore also clear overrides and rebuild).
        var rig = new Rig(Player.m_localPlayer, ContentName);
        try
        {
            UseDefaultRules();
            c.Check(Plugin.FeatureActive, "feature active");
            c.Check(TransplantContent.ItemsBuilt && TransplantContent.SaplingsBuilt, "items and saplings built");
            var table = TransplantContent.CultivatorTable;
            var cultivator = db.GetItemPrefab(PlantCatalog.CultivatorPrefab);
            c.Check(table != null && cultivator != null
                    && ReferenceEquals(cultivator.GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces, table),
                "cultivator piece table found");
            var rootPrefab = scene.GetPrefab(PlantCatalog.RootPrefab);
            var rules = ServerRules.Current;

            foreach (var kind in PlantCatalog.All)
            {
                var k = kind.Key;
                var item = TransplantContent.ItemPrefab(kind);
                var sapling = TransplantContent.SaplingPrefab(kind);
                if (!c.Check(item != null && sapling != null, $"{k}: item and sapling built"))
                {
                    continue;
                }
                c.Check(item.name == kind.ItemName && sapling.name == kind.SaplingName, $"{k}: prefab names");
                c.Check(ReferenceEquals(db.GetItemPrefab(kind.ItemName), item)
                        && db.m_itemByHash.TryGetValue(kind.ItemHash, out var byHash) && ReferenceEquals(byHash, item),
                    $"{k}: item in the item database");
                c.Check(db.m_items.Count(i => ReferenceEquals(i, item)) == 1, $"{k}: item listed once");
                c.Check(ReferenceEquals(scene.GetPrefab(kind.ItemHash), item), $"{k}: item in the network list");
                c.Check(ReferenceEquals(scene.GetPrefab(kind.SaplingHash), sapling), $"{k}: sapling in the network list");
                c.Check(table != null && table.m_pieces.Count(p => ReferenceEquals(p, sapling)) == 1,
                    $"{k}: sapling once in the cultivator table");

                var drop = item.GetComponent<ItemDrop>();
                var s = drop.m_itemData.m_shared;
                c.Check(db.m_itemByData.TryGetValue(s, out var byData) && ReferenceEquals(byData, item), $"{k}: known by its data");
                c.Check(s.m_name == kind.ItemDisplayName, $"{k}: name '{s.m_name}'");
                var where = WhereText(kind);
                c.Check(s.m_description != null && s.m_description.Contains(where), $"{k}: text says '{where}' ('{s.m_description}')");
                c.Check(s.m_itemType == ItemDrop.ItemData.ItemType.Material && s.m_food == 0f && s.m_foodStamina == 0f
                        && s.m_foodEitr == 0f && s.m_consumeStatusEffect == null, $"{k}: material, no food");
                c.Check(s.m_maxStackSize == 20 && Near(s.m_weight, 0.5f) && s.m_teleportable && s.m_value == 0,
                    $"{k}: stack 20, weight 0.5, teleportable, no value");
                c.Check(s.m_icons != null && s.m_icons.Length == 1 && s.m_icons[0] != null, $"{k}: one icon");
                c.Check(ReferenceEquals(drop.m_itemData.m_dropPrefab, item), $"{k}: drop prefab");
                c.Check(ReferenceEquals(TransplantContent.KindOfItem(drop.m_itemData), kind), $"{k}: KindOfItem");
                var produce = db.GetItemPrefab(kind.ProduceItem);
                var ps = produce != null ? produce.GetComponent<ItemDrop>().m_itemData.m_shared : null;
                c.Check(ps != null && !ReferenceEquals(ps, s) && ps.m_name != s.m_name && ps.m_name.StartsWith("$"),
                    $"{k}: vanilla {kind.ProduceItem} untouched ({(ps != null ? ps.m_name : "missing")})");

                var plant = sapling.GetComponent<Plant>();
                var piece = sapling.GetComponent<Piece>();
                c.Check(plant.m_name == kind.ItemDisplayName && piece.m_name == kind.ItemDisplayName, $"{k}: sapling names");
                c.Check(piece.m_description == s.m_description, $"{k}: piece text = item text");
                // Pending hold (C7): our sapling in the hold set, the wild plant never.
                c.Check(RootGate.IsTransplantSapling(kind.SaplingHash) && kind.WildHashes.All(h => !RootGate.IsTransplantSapling(h)),
                    $"{k}: sapling in the pending hold set, wild plant not");
                var grown = plant.m_grownPrefabs.Select(g => g != null ? g.name : "null").ToArray();
                c.Check(grown.SequenceEqual(kind.GrownPrefabs), $"{k}: grows into {string.Join("/", grown)}");
                c.Check(plant.m_biome == kind.Biome && plant.m_tolerateHeat == kind.TolerateHeat
                        && plant.m_tolerateCold == kind.TolerateCold, $"{k}: biome {plant.m_biome}, heat {plant.m_tolerateHeat}, cold {plant.m_tolerateCold}");
                c.Check(!plant.m_needCultivatedGround && !plant.m_destroyIfCantGrow, $"{k}: no cultivated ground, never destroyed");
                c.Check(Near(plant.m_minScale, kind.MinScale) && Near(plant.m_maxScale, kind.MaxScale)
                        && Near(plant.m_growRadius, kind.GrowRadius), $"{k}: scale and grow radius");
                var seconds = kind.GrowMinutes * 60f;
                c.Check(Near(plant.m_growTime, seconds * 0.9f, 0.5f) && Near(plant.m_growTimeMax, seconds * 1.1f, 0.5f),
                    $"{k}: grow time {F(plant.m_growTime)}-{F(plant.m_growTimeMax)} s (want {F(seconds * 0.9f)}-{F(seconds * 1.1f)})");
                c.Check(plant.m_healthyGrown == null && plant.m_unhealthyGrown == null && plant.m_healthy != null,
                    $"{k}: sapling look only");
                c.Check(piece.m_resources.Length == 1 && ReferenceEquals(piece.m_resources[0].m_resItem, drop)
                        && piece.m_resources[0].m_amount == 1 && !piece.m_resources[0].m_recover, $"{k}: costs one transplant, not recovered");
                c.Check(piece.m_icon != null && ReferenceEquals(piece.m_icon, s.m_icons[0]), $"{k}: piece icon = item icon");
                c.Check(piece.m_groundOnly && !piece.m_cultivatedGroundOnly && !piece.m_canBeRemoved
                        && !piece.m_primaryTarget && !piece.m_randomTarget, $"{k}: ground only, not removable, not targeted");
                c.Check(piece.m_onlyInBiome == (kind.OnlyInBiome ? kind.Biome : Heightmap.Biome.None)
                        && piece.m_vegetationGroundOnly == kind.VegetationGroundOnly
                        && piece.m_allowedInDeepSnow == kind.AllowedInDeepSnow, $"{k}: placement biome {piece.m_onlyInBiome}");
                c.Check(piece.m_enabled, $"{k}: piece on");
                if (kind.NeedsRoot)
                {
                    c.Check(piece.m_mustConnectTo != null && rootPrefab != null
                            && ReferenceEquals(piece.m_mustConnectTo, rootPrefab.GetComponent<ZNetView>()),
                        $"{k}: must connect to {PlantCatalog.RootPrefab}");
                    c.Check(Near(piece.m_connectRadius, rules.RootRange), $"{k}: connect radius {F(piece.m_connectRadius)}");
                    c.Check(sapling.GetComponent<RootDrawer>() != null, $"{k}: root drawer on the prefab");
                }
                else
                {
                    c.Check(piece.m_mustConnectTo == null, $"{k}: no root needed");
                }

                c.Check(PlantCatalog.TryFind(kind.SaplingHash, out var sk, out var isSap) && ReferenceEquals(sk, kind) && isSap,
                    $"{k}: sapling found as own sapling");
                var notes = new List<string>();
                foreach (var wildName in kind.WildPrefabs)
                {
                    var wildPrefab = scene.GetPrefab(wildName);
                    c.Check(wildPrefab != null, $"{k}: wild {wildName} exists");
                    c.Check(PlantCatalog.TryFind(wildName.GetStableHashCode(), out var wk, out var wSap) && ReferenceEquals(wk, kind) && !wSap,
                        $"{k}: wild {wildName} found");
                    if (wildPrefab == null)
                    {
                        continue;
                    }
                    var pick = wildPrefab.GetComponent<Pickable>();
                    var dest = wildPrefab.GetComponent<Destructible>();
                    var tree = wildPrefab.GetComponent<TreeBase>();
                    var n = wildName + ":";
                    if (pick != null)
                    {
                        n += $" pick {(pick.m_itemPrefab != null ? pick.m_itemPrefab.name : "-")} x{pick.m_amount} respawn {F(pick.m_respawnTimeMinutes)} min hide {(pick.m_hideWhenPicked != null ? pick.m_hideWhenPicked.name : "-")} syncScale {wildPrefab.GetComponent<ZNetView>().m_syncInitialScale}";
                        // D5: grow time = regrow time.
                        c.Check(Near(pick.m_respawnTimeMinutes, kind.GrowMinutes), $"{k}: grow time = {wildName} regrow time ({F(pick.m_respawnTimeMinutes)} min)");
                        c.Check(pick.m_itemPrefab != null && pick.m_itemPrefab.name == kind.ProduceItem, $"{k}: {wildName} yields {kind.ProduceItem}");
                    }
                    if (dest != null)
                    {
                        n += $" destructible {F(dest.m_health)} hp tier {dest.m_minToolTier} type {dest.m_destructibleType}";
                    }
                    if (tree != null)
                    {
                        n += $" tree {F(tree.m_health)} hp tier {tree.m_minToolTier}";
                    }
                    notes.Add(n);
                }
                c.Note(string.Join(" | ", notes.ToArray()));
            }

            if (rootPrefab != null)
            {
                var root = rootPrefab.GetComponent<ResourceRoot>();
                c.Check(root != null, "root has a ResourceRoot");
                if (root != null)
                {
                    c.Note($"{PlantCatalog.RootPrefab}: max {F(root.m_maxLevel)} regen {root.m_regenPerSec.ToString("0.#####", CultureInfo.InvariantCulture)}/s ({F(root.m_regenPerSec * 3600f)}/h) high {F(root.m_highThreshold)} empty {F(root.m_emptyTreshold)}; layer {LayerMask.LayerToName(rootPrefab.GetComponentInChildren<Collider>().gameObject.layer)}");
                }

                // Root search (C12): only the root's own collider layers, so pieces and items never fill the hit buffer.
                var prefabMask = 0;
                foreach (var col in rootPrefab.GetComponentsInChildren<Collider>(true))
                {
                    prefabMask |= 1 << col.gameObject.layer;
                }
                var mask = RootGate.RootMask;
                var names = Enumerable.Range(0, 32).Where(l => (mask & (1 << l)) != 0).Select(l => LayerMask.LayerToName(l)).ToArray();
                c.Note($"root search layers: {string.Join(", ", names)} (mask {mask})");
                var solid = LayerMask.NameToLayer("static_solid");
                c.Check(solid >= 0 && (mask & (1 << solid)) != 0, "root search mask has static_solid");
                c.Check(prefabMask == 0 || mask == prefabMask, $"root search mask = root collider layers ({prefabMask})");
                c.Check((mask & LayerMask.GetMask("piece", "piece_nonsolid", "item", "character")) == 0,
                    "root search skips pieces, items and creatures");
            }
            var sapCollector = scene.GetPrefab("piece_sapcollector");
            if (sapCollector != null)
            {
                var pc = sapCollector.GetComponent<Piece>();
                c.Note($"piece_sapcollector: Piece.m_mustConnectTo {(pc != null && pc.m_mustConnectTo != null ? pc.m_mustConnectTo.name : "null")} radius {(pc != null ? F(pc.m_connectRadius) : "-")}");
            }

            c.Check(!RootGate.IsTransplantSapling("Beech_Sapling".GetStableHashCode()), "vanilla sapling not in the pending hold set");

            // Toggle: pending, feature off, back. Pending flip rebuild at once (no 0.5 s wait, no Rebuild call here).
            var pieces = PlantCatalog.All.Select(k => TransplantContent.SaplingPrefab(k)).Where(g => g != null)
                .Select(g => g.GetComponent<Piece>()).ToList();
            ServerRules.TestPending = true;
            c.Check(pieces.All(p => !p.m_enabled) && !TransplantContent.RebuildPending, "pending rules: pieces off at once");
            ServerRules.TestPending = false;
            c.Check(pieces.All(p => p.m_enabled) && !TransplantContent.RebuildPending, "rules back: pieces on at once");
            Plugin.TestInactive = true;
            TransplantContent.Rebuild();
            c.Check(pieces.All(p => !p.m_enabled), "feature off: pieces off");
            c.Check(TransplantContent.ItemPrefab(PlantCatalog.All[0]) != null
                    && db.GetItemPrefab(PlantCatalog.All[0].ItemName) != null, "feature off: items stay registered");
            Plugin.TestInactive = false;
            TransplantContent.Rebuild();
            c.Check(pieces.All(p => p.m_enabled), "feature on: pieces on");

            // Rules reach prefabs, and saplings already in the world (C7): planted one keep no stale grow time.
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var player = Player.m_localPlayer;
            var taken = new List<Vector3>();
            Vector3 SpotOr(float d)
            {
                if (FindSpot(player.transform.position, player.transform.forward, new[] { d, d + 2f, d + 4f }, 1.4f, taken, out var spot))
                {
                    return spot;
                }
                var at = player.transform.position + player.transform.forward * d;
                at.y = ZoneSystem.instance.GetGroundHeight(at);
                return at;
            }
            bool HasTimes(Plant pl, float multiplier)
            {
                TransplantContent.GrowTimes(rasp, multiplier, out var lo, out var hi);
                return pl != null && Near(pl.m_growTime, lo, 0.5f) && Near(pl.m_growTimeMax, hi, 0.5f);
            }
            string Times(Plant pl) => pl != null ? $"{F(pl.m_growTime)}-{F(pl.m_growTimeMax)} s" : "none";
            var earlyGo = rig.Spawn(rasp.SaplingName, SpotOr(5f), Quaternion.identity);
            var early = earlyGo != null ? earlyGo.GetComponent<Plant>() : null;
            c.Check(HasTimes(early, 1f), $"planted sapling: default grow time ({Times(early)})");

            ServerRules.TestRules = new CultivatorRules { GrowTimeMultiplier = 2f, RootRange = 10f };
            TransplantContent.Rebuild();
            var raspPlant = TransplantContent.SaplingPrefab(rasp).GetComponent<Plant>();
            c.Check(Near(raspPlant.m_growTime, 300f * 60f * 2f * 0.9f, 0.5f), $"grow time x2 ({F(raspPlant.m_growTime)} s)");
            c.Check(HasTimes(early, 2f) && TransplantContent.LoadedSaplingsUpdated >= 1,
                $"planted sapling: grow time x2 too ({Times(early)}, {TransplantContent.LoadedSaplingsUpdated} updated)");
            var ygg = TransplantContent.SaplingPrefab(PlantCatalog.ByKey("YggaShoot")).GetComponent<Piece>();
            c.Check(Near(ygg.m_connectRadius, 10f), $"root range 10 ({F(ygg.m_connectRadius)})");

            // Joined while pending: sapling loaded with the old time (prefab copy), server rules come = new time at
            // once, before any grow try.
            ServerRules.TestPending = true;
            var lateGo = rig.Spawn(rasp.SaplingName, SpotOr(7f), Quaternion.identity);
            var late = lateGo != null ? lateGo.GetComponent<Plant>() : null;
            c.Check(HasTimes(late, 2f), $"sapling loaded while pending: prefab time ({Times(late)})");
            ServerRules.TestRules = new CultivatorRules { GrowTimeMultiplier = 0.5f };
            ServerRules.TestPending = false;
            c.Check(!TransplantContent.RebuildPending && HasTimes(late, 0.5f) && HasTimes(early, 0.5f),
                $"rules came after pending: both saplings x0.5 at once ({Times(late)}, {Times(early)})");

            UseDefaultRules();
            c.Check(Near(raspPlant.m_growTime, 300f * 60f * 0.9f, 0.5f) && Near(ygg.m_connectRadius, 6f), "defaults back");
            c.Check(HasTimes(early, 1f) && HasTimes(late, 1f), "planted saplings: default grow time back");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
        yield break;
    }

    // ---------- replant.tiers ----------

    private static readonly string[] TierItems = { "RoundLog", "Bronze", "BlackMetal", "LinenThread", "Eitr", "FlametalNew", "Gold" };

    // Expected GetAmount per quality (index 0 = quality 2) for TierItems.
    private static readonly int[][] TierAmounts =
    {
        new[] { 1, 1, 0, 0, 0, 0, 0 },
        new[] { 2, 2, 0, 0, 0, 0, 0 },
        new[] { 0, 0, 5, 10, 0, 0, 0 },
        new[] { 0, 0, 0, 0, 15, 0, 0 },
        new[] { 0, 0, 0, 0, 0, 5, 0 },
        new[] { 0, 0, 0, 0, 0, 0, 5 },
    };

    private static IEnumerator RunTiers()
    {
        var c = new Checks(TiersName);
        var player = Player.m_localPlayer;
        var gui = InventoryGui.instance;
        var recipe = CultivatorTiers.CultivatorRecipe;
        var drop = CultivatorTiers.CultivatorDrop;
        if (player == null || gui == null || recipe == null || drop == null)
        {
            SelfTest.Fail(TiersName, "no player, inventory window, cultivator or cultivator recipe captured");
            yield break;
        }
        var rig = new Rig(player, TiersName);
        try
        {
            var shared = drop.m_itemData.m_shared;

            // Vanilla first (feature off): rows and max quality as the game ship them.
            Plugin.TestInactive = true;
            TransplantContent.Rebuild();
            c.Check(!CultivatorTiers.InForce && shared.m_maxQuality == 3, $"feature off: max quality {shared.m_maxQuality}");
            var vanillaRows = recipe.m_resources;
            c.Note($"Recipe_Cultivator ({recipe.name}): station {(recipe.m_craftingStation != null ? recipe.m_craftingStation.name : "-")} min level {recipe.m_minStationLevel}, rows "
                   + string.Join(", ", vanillaRows.Select(r => $"{(r.m_resItem != null ? r.m_resItem.name : "-")} x{r.m_amount} +{r.m_amountPerLevel}/lvl{(r.m_upgraderResource ? " upgrader" : "")}{(r.m_recover ? " recover" : "")}").ToArray())
                   + $"; cultivator durability {F(shared.m_maxDurability)} +{F(shared.m_durabilityPerLevel)}/lvl, attack stamina {F(shared.m_attack.m_attackStamina)}");
            c.Check(vanillaRows.Length == 3, $"feature off: 3 vanilla rows ({vanillaRows.Length})");
            var bronzeVanilla = Row(recipe, "Bronze");
            c.Check(bronzeVanilla != null && bronzeVanilla.GetAmount(4) == 4, "feature off: vanilla GetAmount(4) for Bronze = 4");
            // Idol row (Forge of Potential): its vanilla amounts per quality 1-7, to compare with later.
            var idol = vanillaRows.FirstOrDefault(r => r != null && r.m_upgraderResource);
            var idolVanilla = idol != null ? Enumerable.Range(1, 7).Select(q => idol.GetAmount(q)).ToArray() : null;
            string Amounts(IEnumerable<int> amounts) => string.Join("/", amounts.Select(a => a.ToString()).ToArray());

            // Heal: a cultivator given while on carry 7, feature off bring it back to 3.
            Plugin.TestInactive = false;
            UseDefaultRules();
            c.Check(CultivatorTiers.InForce && shared.m_maxQuality == 7, $"feature on: max quality {shared.m_maxQuality}");
            var stale = rig.Give(PlantCatalog.CultivatorPrefab, 1, 3);
            c.Check(stale != null && stale.m_shared.m_maxQuality == 7 && !ReferenceEquals(stale.m_shared, shared),
                "new cultivator copy has max quality 7 (own data copy)");
            Plugin.TestInactive = true;
            TransplantContent.Rebuild();
            c.Check(stale != null && stale.m_shared.m_maxQuality == 3, "feature off: copy in inventory healed to 3");
            c.Check(idol == null || Enumerable.Range(1, 7).All(q => idol.GetAmount(q) == idolVanilla[q - 1]),
                "feature off: idol row amounts vanilla again");
            Plugin.TestInactive = false;
            TransplantContent.Rebuild();
            c.Check(stale != null && stale.m_shared.m_maxQuality == 7, "feature on: copy healed to 7");

            // Always-on heal (C4): a copy that kept the other max quality (chest, ground) heal when the crafting list is
            // built, also with the feature off. Show build that list now (SetupCrafting -> UpdateRecipeList).
            var recipeList = AccessTools.Method(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipeList));
            c.Check(HasPatch(Harmony.GetPatchInfo(recipeList)?.Prefixes, typeof(HealPatches), ModInfo.Guid + ".alwayson"),
                "heal prefix on the always-on patch list (stays when the mod is turned off)");
            // Own data copy only: writing the prefab's would make me take 7 as another mod's vanilla value.
            if (stale != null && !ReferenceEquals(stale.m_shared, shared))
            {
                Plugin.TestInactive = true;
                TransplantContent.Rebuild();
                stale.m_shared.m_maxQuality = 7;
                gui.Show(null);
                var healedOff = stale.m_shared.m_maxQuality;
                gui.Hide();
                Plugin.TestInactive = false;
                TransplantContent.Rebuild();
                stale.m_shared.m_maxQuality = 3;
                gui.Show(null);
                var healedOn = stale.m_shared.m_maxQuality;
                gui.Hide();
                c.Check(healedOff == 3, $"feature off: crafting list heal a copy with 7 to 3 ({healedOff})");
                c.Check(healedOn == 7, $"feature on: crafting list heal a copy with 3 to 7 ({healedOn})");
                yield return new WaitForSecondsRealtime(0.3f);
            }
            ServerRules.TestPending = true;
            TransplantContent.Rebuild();
            c.Check(!CultivatorTiers.InForce && shared.m_maxQuality == 3 && recipe.m_resources.Length == 3,
                "pending rules: vanilla levels and rows");
            ServerRules.TestPending = false;
            UseDefaultRules();

            // Rows and amounts per level.
            var rows = TierItems.Select(n => Row(recipe, n)).ToArray();
            c.Check(recipe.m_resources.Length == 8, $"8 rows ({recipe.m_resources.Length})");
            c.Check(rows.All(r => r != null), "rows: " + string.Join(", ", TierItems.Where((n, i) => rows[i] == null).ToArray()) + " missing");
            c.Note("tier materials: " + string.Join(", ", rows.Skip(2).Where(r => r != null)
                .Select(r => $"{r.m_resItem.name} = {L(r.m_resItem.m_itemData.m_shared.m_name)} (m_amount {r.m_amount}, +{r.m_amountPerLevel}/lvl, recover {r.m_recover})").ToArray()));
            if (rows.All(r => r != null))
            {
                for (var q = 2; q <= 7; q++)
                {
                    var got = rows.Select(r => r.GetAmount(q)).ToArray();
                    var want = TierAmounts[q - 2];
                    c.Check(got.SequenceEqual(want), $"level {q} amounts {string.Join("/", got.Select(a => a.ToString()).ToArray())} (want {string.Join("/", want.Select(a => a.ToString()).ToArray())})");
                    c.Check(recipe.GetRequiredStationLevel(q) == q, $"level {q} needs Forge level {recipe.GetRequiredStationLevel(q)}");
                }
                c.Check(rows.Skip(2).All(r => r.m_amount == 0 && r.m_amountPerLevel == 1 && !r.m_recover && !r.m_upgraderResource),
                    "tier rows: amount 0, +1 per level, not recovered");
            }
            // Idol row (C3): vanilla at level 1, 0 for every upgrade while tiers in force (no page or mod offer an idol
            // upgrade of the cultivator).
            var idolRow = recipe.m_resources.FirstOrDefault(r => r != null && r.m_upgraderResource);
            c.Check(idolRow != null && ReferenceEquals(idolRow, idol), "idol row kept (same object)");
            if (idolRow != null && idolVanilla != null)
            {
                var idolNow = Enumerable.Range(1, 7).Select(q => idolRow.GetAmount(q)).ToArray();
                c.Note($"idol row {(idolRow.m_resItem != null ? idolRow.m_resItem.name : "-")}: vanilla q1-7 {Amounts(idolVanilla)}, tiers on {Amounts(idolNow)}");
                c.Check(idolNow[0] == idolVanilla[0] && idolNow.Skip(1).All(a => a == 0),
                    $"idol row: vanilla {idolVanilla[0]} at level 1, 0 for levels 2-7 ({Amounts(idolNow)})");
            }

            // Another mod swap the recipe array (C5, recipe config mod, server sync): its rows the base for levels 1-3,
            // tier rows on top again, same tier row objects. Its Bronze row here carry one more at level 1.
            var baseRows = CultivatorTiers.BaseResources;
            var ourRows = CultivatorTiers.InstalledResources;
            if (c.Check(baseRows != null && ourRows != null && ReferenceEquals(recipe.m_resources, ourRows)
                        && ourRows.Length > baseRows.Length, "our array in the recipe"))
            {
                var tierRows = ourRows.Skip(baseRows.Length).ToArray();
                var resyncs = CultivatorTiers.ResyncCount;
                var foreign = (Piece.Requirement[])baseRows.Clone();
                var bronzeAt = Array.FindIndex(foreign, r => r != null && r.m_resItem != null && r.m_resItem.name == "Bronze");
                Piece.Requirement theirBronze = null;
                if (bronzeAt >= 0)
                {
                    var b = foreign[bronzeAt];
                    theirBronze = new Piece.Requirement
                    {
                        m_resItem = b.m_resItem,
                        m_amount = b.m_amount + 1,
                        m_extraAmountOnlyOneIngredient = b.m_extraAmountOnlyOneIngredient,
                        m_amountPerLevel = b.m_amountPerLevel,
                        m_upgraderResource = b.m_upgraderResource,
                        m_recover = b.m_recover,
                    };
                    foreign[bronzeAt] = theirBronze;
                }
                try
                {
                    recipe.m_resources = foreign;
                    c.Check(CultivatorTiers.NeedsResync, "swapped array seen");
                    CultivatorTiers.Resync();
                    var now = recipe.m_resources;
                    c.Check(ReferenceEquals(now, CultivatorTiers.InstalledResources) && !ReferenceEquals(now, foreign)
                            && now.Length == foreign.Length + tierRows.Length && !CultivatorTiers.NeedsResync,
                        $"resync: tier rows on top of the other mod's rows ({now.Length} rows)");
                    c.Check(ReferenceEquals(CultivatorTiers.BaseResources, foreign) && CultivatorTiers.ResyncCount == resyncs + 1,
                        $"resync: other mod's array is the base now, counted once ({CultivatorTiers.ResyncCount - resyncs})");
                    c.Check(now.Skip(foreign.Length).SequenceEqual(tierRows), "resync: same tier row objects");
                    if (theirBronze != null)
                    {
                        c.Check(ReferenceEquals(Row(recipe, "Bronze"), theirBronze) && theirBronze.GetAmount(1) == theirBronze.m_amount
                                && theirBronze.GetAmount(4) == 0,
                            $"resync: their Bronze row {theirBronze.GetAmount(1)} at level 1, 0 at level 4");
                    }
                    var swapped = TierItems.Select(n => Row(recipe, n)).ToArray();
                    if (c.Check(swapped.All(r => r != null), "resync: every row there"))
                    {
                        for (var q = 2; q <= 7; q++)
                        {
                            var got = swapped.Select(r => r.GetAmount(q)).ToArray();
                            c.Check(got.SequenceEqual(TierAmounts[q - 2]), $"resync: level {q} amounts {Amounts(got)} (want {Amounts(TierAmounts[q - 2])})");
                        }
                    }
                    // Same array put in again (other mod apply again): our array back, nothing rebuilt or counted.
                    var installedNow = CultivatorTiers.InstalledResources;
                    recipe.m_resources = foreign;
                    CultivatorTiers.Resync();
                    c.Check(ReferenceEquals(recipe.m_resources, installedNow) && CultivatorTiers.ResyncCount == resyncs + 1,
                        "same array again: our array back, no rebuild");
                    // Per frame path: vanilla array back in the recipe, ZNet.Update postfix put tier rows on top again.
                    recipe.m_resources = baseRows;
                    yield return null;
                    yield return null;
                    c.Check(!CultivatorTiers.NeedsResync && ReferenceEquals(CultivatorTiers.BaseResources, baseRows)
                            && ReferenceEquals(recipe.m_resources, CultivatorTiers.InstalledResources),
                        "next frame: vanilla rows the base again, tier rows on top (ZNet.Update)");
                }
                finally
                {
                    // Never leave the test array as base: vanilla rows back for good.
                    if (!ReferenceEquals(CultivatorTiers.BaseResources, baseRows)
                        || !ReferenceEquals(recipe.m_resources, CultivatorTiers.InstalledResources))
                    {
                        recipe.m_resources = baseRows;
                        CultivatorTiers.Apply();
                    }
                }
            }

            // Another mod's own cultivator recipe: its upgrade rows to level 4+ hidden (vanilla formula, no tier
            // material), craft row and upgrades to 2 and 3 stay. Items = copies of the level 3 cultivator, never in an
            // inventory.
            var otherRecipe = ScriptableObject.CreateInstance<Recipe>();
            try
            {
                otherRecipe.name = "MC_SelfTest_OtherCultivatorRecipe";
                otherRecipe.m_item = drop;
                var at2 = stale != null ? stale.Clone() : null;
                var at3 = stale != null ? stale.Clone() : null;
                if (c.Check(at2 != null && at3 != null, "cultivator copies for the other recipe check"))
                {
                    at2.m_quality = 2;
                    at3.m_quality = 3;
                    c.Check(CultivatorTiers.HideForeignUpgrade(otherRecipe, at3), "other cultivator recipe: upgrade 3 -> 4 hidden");
                    c.Check(!CultivatorTiers.HideForeignUpgrade(otherRecipe, at2) && !CultivatorTiers.HideForeignUpgrade(otherRecipe, null)
                            && !CultivatorTiers.HideForeignUpgrade(recipe, at3),
                        "other cultivator recipe: upgrade 2 -> 3 and craft row stay; our recipe never hidden");
                }
            }
            finally
            {
                Object.Destroy(otherRecipe);
            }

            // Tooltip.
            var tip = ItemDrop.ItemData.GetTooltip(stale, 5, false, Game.m_worldLevel);
            c.Check(tip.Contains("Tier: <color=orange>Eitr cultivator</color>") && tip.Contains("Yggdrasil shoot")
                    && !tip.Contains("Fiddlehead"), "tooltip level 5: eitr tier, Yggdrasil, no fiddlehead");
            var craftTip = ItemDrop.ItemData.GetTooltip(drop.m_itemData, 4, true, Game.m_worldLevel);
            c.Check(craftTip.Contains("New: <color=orange>Cloudberry bush</color>"), "upgrade preview level 4: new cloudberry bush");
            c.Check(!ItemDrop.ItemData.GetTooltip(drop.m_itemData, 3, true, Game.m_worldLevel).Contains("New:"), "upgrade preview level 3: nothing new");

            // A cost with no valid material: cultivator stop below that level.
            ServerRules.TestRules = new CultivatorRules { EitrLevel = "NoSuchItem:3" };
            TransplantContent.Rebuild();
            c.Check(shared.m_maxQuality == 4, $"eitr cost without valid material: max quality 4 ({shared.m_maxQuality})");
            ServerRules.TestRules = new CultivatorRules { BlackMetalLevel = "" };
            TransplantContent.Rebuild();
            c.Check(shared.m_maxQuality == 3 && recipe.m_resources.Length == 3, $"black metal cost empty: max quality 3 ({shared.m_maxQuality}), no tier rows");
            UseDefaultRules();
            c.Check(shared.m_maxQuality == 7, "defaults back: 7");

            // Upgrade 3 -> 4 in the real Forge Upgrade tab: Forge + 3 extensions = level 4.
            var taken = new List<Vector3>();
            var fwd = player.transform.forward;
            // Spawn stones fill the ring close by: look farther too (station use distance raised below).
            if (!FindSpot(player.transform.position, fwd, new[] { 4f, 5f, 6f, 8f, 10f, 12f, 14f }, 3f, taken,
                    out var forgeSpot))
            {
                c.Check(false, "no free spot for a Forge near the player");
                c.Report();
                yield break;
            }
            var toPlayer = player.transform.position - forgeSpot;
            toPlayer.y = 0f;
            var forgeGo = rig.Spawn("forge", forgeSpot, Quaternion.LookRotation(toPlayer.normalized));
            var side = Vector3.Cross(Vector3.up, toPlayer.normalized);
            var extDir = new[] { side, -side, -toPlayer.normalized };
            for (var i = 0; i < 3; i++)
            {
                // Each extension count only inside its own reach (forge_ext1: 2 m, the others 5 m).
                var extPrefab = ZNetScene.instance.GetPrefab("forge_ext" + (i + 1));
                var ext = extPrefab != null ? extPrefab.GetComponent<StationExtension>() : null;
                var reach = ext != null ? Mathf.Min(2.5f, ext.m_maxStationDistance * 0.75f) : 1.5f;
                var p = forgeSpot + extDir[i] * reach;
                p.y = ZoneSystem.instance.GetGroundHeight(p);
                rig.Spawn("forge_ext" + (i + 1), p, Quaternion.LookRotation(toPlayer.normalized));
            }
            var station = forgeGo != null ? forgeGo.GetComponentInChildren<CraftingStation>() : null;
            if (!c.Check(station != null, "Forge spawned"))
            {
                c.Report();
                yield break;
            }
            station.m_useDistance = 50f;
            var until = Time.time + 8f;
            while (station.GetLevel() < 4 && Time.time < until)
            {
                yield return new WaitForSeconds(0.25f);
            }
            c.Check(station.GetLevel() == 4, $"Forge level {station.GetLevel()} with 3 extensions");
            c.Note($"Forge extensions in the game: {ZNetScene.instance.m_prefabs.Count(go => go != null && go.GetComponent<StationExtension>() is StationExtension se && se.m_craftingStation != null && se.m_craftingStation.name == "forge")}");

            // Materials: exactly the level 4 cost, plus Bronze and Corewood that must stay.
            int Have(string prefab) => CountByName(rig.Inv, SharedName(prefab));
            var before = TierItems.ToDictionary(n => n, Have);
            c.Check(!player.HaveRequirementItems(recipe, false, 4) || before["BlackMetal"] >= 5,
                "level 4 not possible without black metal and linen thread");
            rig.Give("BlackMetal", 5);
            rig.Give("LinenThread", 9);
            c.Check(!player.HaveRequirementItems(recipe, false, 4) || before["LinenThread"] > 0, "9 linen thread is not enough");
            rig.Give("LinenThread", 1);
            rig.Give("Bronze", 5);
            rig.Give("RoundLog", 5);
            c.Check(player.HaveRequirementItems(recipe, false, 4), "5 black metal + 10 linen thread are enough");
            c.Check(!player.HaveRequirementItems(recipe, false, 5) || before["Eitr"] >= 15, "level 5 needs eitr");

            rig.Know(recipe);
            rig.TakeControls();
            rig.Noon();
            player.SetCraftingStation(station);
            gui.Show(null, 3);
            yield return new WaitForSecondsRealtime(1f);
            gui.OnTabUpgradePressed();
            yield return null;
            yield return null;
            var row = -1;
            for (var i = 0; i < gui.m_availableRecipes.Count; i++)
            {
                if (ReferenceEquals(gui.m_availableRecipes[i].ItemData, stale))
                {
                    row = i;
                }
            }
            c.Check(row >= 0, $"Upgrade tab lists the level 3 cultivator ({gui.m_availableRecipes.Count} rows)");
            if (row >= 0)
            {
                c.Check(gui.m_availableRecipes[row].CanCraft, "level 3 -> 4 can be made");
                gui.SetRecipe(row, false);
                yield return null;
                yield return null;
                var desc = gui.m_recipeDecription != null ? gui.m_recipeDecription.text : "";
                c.Check(desc.Contains("Black metal cultivator") && desc.Contains("Cloudberry bush"),
                    "upgrade panel: black metal tier, new cloudberry bush");
                SelfTest.Screenshot(TiersName, "forge-upgrade");
                yield return null;
                yield return null;
                var gridPos = stale.m_gridPos;
                gui.OnCraftPressed();
                yield return null;
                if (gui.m_craftTimer >= 0f)
                {
                    gui.m_craftTimer = 1000f;
                }
                yield return Frames(3);
                var upgraded = rig.Inv.GetItemAt(gridPos.x, gridPos.y);
                c.Check(!rig.Inv.ContainsItem(stale), "old level 3 cultivator gone");
                c.Check(upgraded != null && CultivatorTiers.IsCultivator(upgraded) && upgraded.m_quality == 4,
                    $"level 4 cultivator in the same slot (quality {(upgraded != null ? upgraded.m_quality : 0)})");
                if (upgraded != null)
                {
                    c.Check(Near(upgraded.m_durability, 800f, 0.5f) && CultivatorTiers.TierOf(upgraded) == 4,
                        $"level 4: durability {F(upgraded.m_durability)}, tier {CultivatorTiers.TierOf(upgraded)}");
                }
                c.Check(Have("BlackMetal") == before["BlackMetal"] && Have("LinenThread") == before["LinenThread"],
                    $"black metal and linen thread spent ({Have("BlackMetal")}, {Have("LinenThread")} left)");
                c.Check(Have("Bronze") == before["Bronze"] + 5 && Have("RoundLog") == before["RoundLog"] + 5,
                    "bronze and corewood not spent from level 4");
            }
            gui.Hide();
            player.SetCraftingStation(null);
            yield return new WaitForSecondsRealtime(0.5f);

            // Forge of Potential: no cultivator row (an idol would skip a tier's cost).
            var level3 = rig.Give(PlantCatalog.CultivatorPrefab, 1, 3);
            if (FindSpot(player.transform.position, -fwd, new[] { 4f, 5f, 6f, 7f, 9f, 11f, 13f }, 2.5f, taken,
                    out var fopSpot))
            {
                var away = player.transform.position - fopSpot;
                away.y = 0f;
                var fop = rig.Spawn("UpgradeStation", fopSpot, Quaternion.LookRotation(away.normalized));
                yield return new WaitForSeconds(0.5f);
                var up = fop != null ? fop.GetComponentInChildren<CraftingStation>() : null;
                if (c.Check(up != null && up.m_upgrader, "Forge of Potential spawned (upgrader)"))
                {
                    up.m_useDistance = 50f;
                    player.SetCraftingStation(up);
                    c.Check(CultivatorTiers.HideAtUpgrader(null, recipe), "cultivator hidden at the Forge of Potential");
                    gui.Show(null, 3);
                    yield return new WaitForSecondsRealtime(1f);
                    gui.OnTabUpgradePressed();
                    yield return null;
                    yield return null;
                    var listed = gui.m_availableRecipes.Any(r => ReferenceEquals(r.ItemData, level3) || (r.ItemData != null && CultivatorTiers.IsCultivator(r.ItemData)));
                    c.Check(!listed, $"Forge of Potential Upgrade tab has no cultivator row ({gui.m_availableRecipes.Count} rows)");
                    SelfTest.Screenshot(TiersName, "forge-of-potential");
                    yield return null;
                    yield return null;
                    gui.Hide();
                    player.SetCraftingStation(null);
                }
            }
            else
            {
                c.Note("no free spot for a Forge of Potential: only the pure check ran");
            }
            player.m_currentStation = null;
            c.Check(!CultivatorTiers.HideAtUpgrader(null, recipe), "no station: cultivator not hidden");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- replant.action ----------

    private static IEnumerator RunAction()
    {
        var c = new Checks(ActionName);
        var player = Player.m_localPlayer;
        if (player == null || ZNetScene.instance == null || Hud.instance == null)
        {
            SelfTest.Fail(ActionName, "no player, network list or HUD");
            yield break;
        }
        var rig = new Rig(player, ActionName);
        var taken = new List<Vector3>();
        try
        {
            UseDefaultRules();
            rig.Noon();
            rig.NoAutoPickup();
            rig.TakeControls();
            // PlantEasily (tester's PC) throw in its ghost grid when this test take the selected transplant away.
            LiftPlantEasily(rig);
            var inv = rig.Inv;
            var tool1 = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            var tool3 = rig.Give(PlantCatalog.CultivatorPrefab, 1, 3);
            var tool4 = rig.Give(PlantCatalog.CultivatorPrefab, 1, 4);
            if (!c.Check(tool1 != null && tool3 != null && tool4 != null, "cultivators level 1, 3, 4 given"))
            {
                c.Report();
                yield break;
            }
            var origin = player.transform.position;
            var fwd = player.transform.forward;
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var cloud = PlantCatalog.ByKey("CloudberryBush");
            var blue = PlantCatalog.ByKey("BlueberryBush");
            var dand = PlantCatalog.ByKey("Dandelion");
            var thistle = PlantCatalog.ByKey("Thistle");

            // Keys Replant read (C2: keyboard Use, gamepad X without the alt keys; X also live under a layout name).
            var zin = ZInput.instance;
            var alias = Replant.PadAlias;
            c.Check(zin != null && zin.GetButtonDef(Replant.KeyboardButton) != null && zin.GetButtonDef(Replant.PadButtonX) != null
                    && zin.GetButtonDef(alias) != null && zin.GetButtonDef("JoyAltKeys") != null,
                $"buttons Replant reads exist ({Replant.KeyboardButton}, {Replant.PadButtonX}, {alias}, JoyAltKeys)");
            if (Localization.instance != null && zin != null)
            {
                c.Note($"layout {ZInput.InputLayout}: X glyph '{Localization.instance.GetBoundKeyString(Replant.PadButtonX, true)}', "
                       + $"{alias} glyph '{Localization.instance.GetBoundKeyString(alias, true)}', Use key '{Localization.instance.GetBoundKeyString(Replant.KeyboardButton)}'");
            }

            // 1. Raspberry, level 1: hint, then Replant.
            var near = FindSpot(origin, fwd, new[] { 3f, 3.5f }, 1.2f, taken, out var spotA);
            if (!near && !FindSpot(origin, fwd, new[] { 5f, 7f, 9f }, 1.2f, taken, out spotA))
            {
                c.Check(false, "no free spot near the player");
                c.Report();
                yield break;
            }
            var bush = rig.Spawn("RaspberryBush", spotA, Quaternion.identity);
            yield return Settle();
            var bushView = bush.GetComponent<ZNetView>();
            c.Check(PlantCatalog.TryFind(bushView.GetZDO().GetPrefab(), out var found, out var isSap) && ReferenceEquals(found, rasp) && !isSap,
                "spawned raspberry bush found in the plant table");
            var pick = bush.GetComponent<Pickable>();
            c.Check(pick != null && pick.CanBePicked(), "wild bush ripe");

            player.EquipItem(tool1, false);
            yield return Frames(3);
            c.Check(player.InPlaceMode() && ReferenceEquals(player.GetRightItem(), tool1), "cultivator in hand, build mode");
            if (near)
            {
                yield return rig.AimAt(AimPoint(bush));
                yield return Frames(3);
                var t = Replant.Target;
                c.Check(t.IsFresh && ReferenceEquals(t.Kind, rasp) && !t.IsSapling && t.Allowed && ReferenceEquals(t.View, bushView),
                    $"crosshair target = raspberry bush, allowed (fresh {t.IsFresh}, kind {(t.Kind != null ? t.Kind.Key : "none")})");
                var text = Hud.instance.m_hoverName.text ?? "";
                c.Check(text.Contains("Replant"), $"hint says Replant ('{text.Replace("\n", " / ")}')");
                c.Note("hint over raspberry, level 1: " + text.Replace("\n", " / "));
                SelfTest.Screenshot(ActionName, "hint-replant");
                yield return Frames(2);
            }
            else
            {
                c.Note("no free spot within reach: crosshair hint not checked");
            }

            var dur0 = tool1.m_durability;
            var berries = SharedName("Raspberry");
            c.Check(Replant.TryReplant(player, tool1, bushView, rasp, false, out var refusal), $"level 1 replants a raspberry bush ({refusal})");
            c.Check(!Alive(bushView), "bush gone");
            c.Check(CountByName(inv, rasp.ItemDisplayName) == 1, "one raspberry transplant in the inventory");
            var given = inv.GetAllItems().FirstOrDefault(i => i.m_shared.m_name == rasp.ItemDisplayName);
            c.Check(given != null && ReferenceEquals(TransplantContent.KindOfItem(given), rasp), "inventory transplant known as raspberry");
            c.Check(tool1.m_durability < dur0, $"tool wear {F(dur0)} -> {F(tool1.m_durability)}");
            yield return new WaitForSeconds(0.5f);
            c.Check(DropsNear(spotA, berries).Count >= 1, "ripe bush picked first: raspberries on the ground (E1)");
            yield return Frames(2);
            c.Check(!Replant.Target.IsFresh || Replant.Target.View == null || !Replant.Target.View.IsValid() || !ReferenceEquals(Replant.Target.View, bushView), "target cleared");

            // 2. Cloudberry: level 3 refused, level 4 ok.
            if (FindSpot(origin, fwd, near ? new[] { 3f, 3.5f } : new[] { 5f, 7f, 9f }, 1.2f, taken, out var spotB)
                || FindSpot(origin, fwd, new[] { 5f, 7f, 9f, 11f }, 1.2f, taken, out spotB))
            {
                var cb = rig.Spawn("CloudberryBush", spotB, Quaternion.identity);
                yield return Settle();
                var cbView = cb.GetComponent<ZNetView>();
                if (near && (spotB - origin).magnitude < 4f)
                {
                    yield return rig.AimAt(AimPoint(cb));
                    yield return Frames(3);
                    var t = Replant.Target;
                    c.Check(t.IsFresh && ReferenceEquals(t.Kind, cloud) && !t.Allowed, "crosshair target = cloudberry, not allowed at level 1");
                    var text = Hud.instance.m_hoverName.text ?? "";
                    c.Check(text.Contains("Needs a black metal cultivator (level 4)"), $"hint says the level needed ('{text.Replace("\n", " / ")}')");
                    SelfTest.Screenshot(ActionName, "hint-needs-tier");
                    yield return Frames(2);
                }
                c.Check(!Replant.TryReplant(player, tool3, cbView, cloud, false, out refusal) && refusal == Replant.TierNeededText(4),
                    $"level 3 refused on cloudberry ({refusal})");
                c.Check(Alive(cbView), "cloudberry still there after refusal");
                c.Check(Replant.TryReplant(player, tool4, cbView, cloud, false, out refusal), $"level 4 replants cloudberry ({refusal})");
                c.Check(!Alive(cbView) && CountByName(inv, cloud.ItemDisplayName) == 1, "cloudberry gone, transplant given");
                yield return new WaitForSeconds(0.5f);
                c.Check(DropsNear(spotB, SharedName("Cloudberry")).Count >= 1, "cloudberries on the ground");
            }
            else
            {
                c.Check(false, "no free spot for the cloudberry bush");
            }

            // Yggdrasil transplant picked in the build menu, nothing under the crosshair: hint give both limits (C20:
            // grow radius 2, RootRange 6). A transplant in the bag make the piece known.
            var ygg = PlantCatalog.ByKey("YggaShoot");
            var yggSapling = TransplantContent.SaplingPrefab(ygg);
            var yggPiece = yggSapling != null ? yggSapling.GetComponent<Piece>() : null;
            if (c.Check(yggPiece != null && rig.Give(ygg.ItemName) != null, "Yggdrasil transplant given"))
            {
                player.UpdateKnownRecipesList();
                player.UpdateAvailablePiecesList();
                var before = player.GetSelectedPiece();
                if (c.Check(player.SetSelectedPiece(yggPiece), "Yggdrasil transplant picked in the build menu"))
                {
                    rig.Look(player.m_lookYaw.eulerAngles.y, -70f);
                    yield return Frames(4);
                    var want = "Plant " + F1(ygg.GrowRadius) + " to " + F1(ServerRules.Current.RootRange) + " m from an Ancient Root";
                    var text = Hud.instance.m_hoverName.text ?? "";
                    c.Check(want == "Plant 2 to 6 m from an Ancient Root" && text == want, $"placing hint '{text}' (want '{want}')");
                    SelfTest.Screenshot(ActionName, "hint-yggdrasil");
                    yield return Frames(2);
                }
                if (before != null)
                {
                    player.SetSelectedPiece(before);
                }
            }
            player.UnequipItem(tool1, false);

            // 3. Forage: dandelion ripe, thistle already picked.
            if (FindSpot(origin, fwd, new[] { 5f, 7f, 9f, 11f }, 0.8f, taken, out var spotC))
            {
                var dd = rig.Spawn("Pickable_Dandelion", spotC, Quaternion.identity);
                yield return Settle();
                var ddView = dd.GetComponent<ZNetView>();
                c.Check(Replant.TryReplant(player, tool1, ddView, dand, false, out refusal), $"dandelion replanted ({refusal})");
                c.Check(!Alive(ddView) && CountByName(inv, dand.ItemDisplayName) == 1, "dandelion gone, transplant given");
                yield return new WaitForSeconds(0.5f);
                c.Check(DropsNear(spotC, SharedName("Dandelion")).Count == 1, "one dandelion picked first");
            }
            else
            {
                c.Check(false, "no free spot for the dandelion");
            }
            if (FindSpot(origin, fwd, new[] { 5f, 7f, 9f, 11f }, 0.8f, taken, out var spotD))
            {
                var th = rig.Spawn("Pickable_Thistle", spotD, Quaternion.identity);
                yield return Settle();
                var thView = th.GetComponent<ZNetView>();
                var thPick = th.GetComponent<Pickable>();
                thPick.Interact(player, false, false);
                yield return new WaitForSeconds(0.3f);
                c.Check(thPick.GetPicked() && !thPick.CanBePicked(), "thistle picked by hand first");
                c.Check(Replant.TryReplant(player, tool1, thView, thistle, false, out refusal), $"picked thistle replanted ({refusal})");
                c.Check(!Alive(thView) && CountByName(inv, thistle.ItemDisplayName) == 1, "thistle gone, transplant given");
                yield return new WaitForSeconds(0.3f);
                c.Check(DropsNear(spotD, SharedName("Thistle")).Count == 1, "no second thistle from a picked plant");
            }
            else
            {
                c.Check(false, "no free spot for the thistle");
            }

            // 4. Transplant sapling not grown yet (E2), planted by another player: gives its transplant back.
            if (FindSpot(origin, fwd, new[] { 5f, 7f, 9f, 11f }, 1.2f, taken, out var spotE))
            {
                var sap = rig.Spawn(rasp.SaplingName, spotE, Quaternion.identity);
                yield return Settle();
                var sapView = sap != null ? sap.GetComponent<ZNetView>() : null;
                if (c.Check(Alive(sapView), "raspberry transplant sapling spawned"))
                {
                    c.Check(PlantCatalog.TryFind(sapView.GetZDO().GetPrefab(), out var sk, out var sIs) && ReferenceEquals(sk, rasp) && sIs,
                        "sapling found as own sapling");
                    var sapPiece = sap.GetComponent<Piece>();
                    if (sapPiece != null)
                    {
                        sapPiece.m_creator = OtherPlayer;
                        sapView.GetZDO().Set(ZDOVars.s_creator, OtherPlayer);
                    }
                    c.Check(sapPiece != null && !sapPiece.IsCreator(), "sapling planted by another player");
                    c.Check(Replant.TryReplant(player, tool1, sapView, rasp, true, out refusal), $"another player's sapling dug up ({refusal})");
                    c.Check(!Alive(sapView) && CountByName(inv, rasp.ItemDisplayName) == 2, "sapling gone, transplant back (2 now)");
                    yield return new WaitForSeconds(0.3f);
                    c.Check(DropsNear(spotE, berries).Count == 0, "no berries from a sapling");
                }
            }
            else
            {
                c.Check(false, "no free spot for the sapling");
            }

            // 5. Full inventory: transplant dropped at the plant.
            if (FindSpot(origin, fwd, new[] { 5f, 7f, 9f, 11f }, 1.2f, taken, out var spotF))
            {
                var bb = rig.Spawn("BlueberryBush", spotF, Quaternion.identity);
                yield return Settle();
                var bbView = bb.GetComponent<ZNetView>();
                var fill = new List<ItemDrop.ItemData>();
                var empty = inv.GetEmptySlots();
                for (var i = 0; i < empty; i++)
                {
                    var club = rig.Give("Club");
                    if (club == null)
                    {
                        break;
                    }
                    fill.Add(club);
                }
                c.Check(inv.GetEmptySlots() == 0, $"inventory full ({fill.Count} clubs added)");
                c.Check(Replant.TryReplant(player, tool1, bbView, blue, false, out refusal), $"blueberry replanted with a full inventory ({refusal})");
                c.Check(CountByName(inv, blue.ItemDisplayName) == 0, "no blueberry transplant in the full inventory");
                yield return new WaitForSeconds(0.3f);
                c.Check(DropsNear(spotF, blue.ItemDisplayName).Count == 1, "blueberry transplant dropped at the plant");
                foreach (var club in fill)
                {
                    inv.RemoveItem(club);
                }
            }
            else
            {
                c.Check(false, "no free spot for the blueberry bush");
            }

            // 6. Pending rules: refused, quiet.
            if (FindSpot(origin, fwd, new[] { 5f, 7f, 9f, 11f, 13f }, 1.2f, taken, out var spotG))
            {
                var pb = rig.Spawn("RaspberryBush", spotG, Quaternion.identity);
                yield return Settle();
                var pbView = pb.GetComponent<ZNetView>();
                ServerRules.TestPending = true;
                c.Check(!Replant.TryReplant(player, tool1, pbView, rasp, false, out refusal) && Alive(pbView),
                    $"pending rules: refused ({refusal})");
                ServerRules.TestPending = false;

                // 7. Ward (not ours): refused; ward gone: allowed.
                var wardPos = spotG + Vector3.right * 2f;
                wardPos.y = ZoneSystem.instance.GetGroundHeight(wardPos);
                var ward = rig.Spawn("guard_stone", wardPos, Quaternion.identity);
                yield return Settle();
                var area = ward != null ? ward.GetComponent<PrivateArea>() : null;
                if (c.Check(area != null, "ward spawned"))
                {
                    area.m_nview.GetZDO().Set(ZDOVars.s_enabled, true);
                    c.Check(!PrivateArea.CheckAccess(spotG, 0f, false), "ward active, not ours");
                    c.Check(!Replant.TryReplant(player, tool1, pbView, rasp, false, out refusal) && refusal == "a ward protects it" && Alive(pbView),
                        $"ward refuses ({refusal})");
                    rig.Destroy(ward);
                    yield return Frames(2);
                    c.Check(Replant.TryReplant(player, tool1, pbView, rasp, false, out refusal) && !Alive(pbView),
                        $"ward gone: replanted ({refusal})");
                }
            }
            else
            {
                c.Check(false, "no free spot for the pending / ward checks");
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- replant.grow ----------

    private static IEnumerator RunGrow()
    {
        var c = new Checks(GrowName);
        var player = Player.m_localPlayer;
        if (player == null || ZNetScene.instance == null)
        {
            SelfTest.Fail(GrowName, "no player or network list");
            yield break;
        }
        var rig = new Rig(player, GrowName);
        var taken = new List<Vector3>();
        try
        {
            UseDefaultRules();
            rig.Noon();
            rig.TakeControls();
            var origin = player.transform.position;
            var biomeHere = Heightmap.FindBiome(origin);
            c.Note($"player biome {biomeHere}");
            foreach (var kind in PlantCatalog.All)
            {
                if (kind.NeedsRoot)
                {
                    continue; // replant.root
                }
                var k = kind.Key;
                if (!FindSpot(origin, player.transform.forward, new[] { 4f, 6f, 8f, 10f, 12f, 14f }, kind.GrowRadius + 0.4f, taken, out var spot))
                {
                    c.Check(false, $"{k}: no free spot");
                    continue;
                }
                var sapGo = rig.Spawn(kind.SaplingName, spot, Quaternion.Euler(0f, 30f, 0f));
                yield return Settle();
                var view = sapGo != null ? sapGo.GetComponent<ZNetView>() : null;
                if (!c.Check(Alive(view), $"{k}: sapling spawned"))
                {
                    continue;
                }
                var plant = sapGo.GetComponent<Plant>();
                c.Check(plant.GetStatus() == Plant.Status.Healthy, $"{k}: new sapling healthy");
                var biome = Heightmap.FindBiome(spot);
                plant.UpdateHealth(100.0);
                var status = plant.GetStatus();
                var growsHere = (biome & kind.Biome) != 0;
                if (k == "RaspberryBush")
                {
                    yield return rig.AimAt(spot + Vector3.up * 0.4f);
                    yield return Frames(5);
                    c.Note("raspberry sapling hover: " + plant.GetHoverText());
                    SelfTest.Screenshot(GrowName, "sapling-raspberry");
                    yield return Frames(2);
                }
                if (growsHere)
                {
                    c.Check(status == Plant.Status.Healthy, $"{k}: healthy in {biome} ({status})");
                    // Rules pending (client waiting for the server, C7): no sapling of ours grow, any kind.
                    ServerRules.TestPending = true;
                    var held = plant.Grow();
                    rig.Track(held);
                    ServerRules.TestPending = false;
                    c.Check(held == null && Alive(view) && plant.GetStatus() == Plant.Status.Healthy, $"{k}: pending rules: healthy sapling waits");
                    var grown = plant.Grow();
                    rig.Track(grown);
                    if (!c.Check(grown != null, $"{k}: Grow made a plant"))
                    {
                        continue;
                    }
                    var hash = PrefabHash(grown);
                    c.Check(kind.GrownPrefabs.Any(n => n.GetStableHashCode() == hash), $"{k}: grew into {Utils.GetPrefabName(grown)}");
                    c.Check(!Alive(view), $"{k}: sapling gone");
                    var scale = grown.transform.localScale.x;
                    c.Check(scale >= kind.MinScale - 0.001f && scale <= kind.MaxScale + 0.001f, $"{k}: scale {F(scale)} in {F(kind.MinScale)}-{F(kind.MaxScale)}");
                    var gp = grown.GetComponent<Pickable>();
                    c.Check(gp != null && !gp.GetPicked() && gp.CanBePicked(), $"{k}: grown plant ripe");
                    c.Check(PlantCatalog.TryFind(hash, out var again, out var againSap) && ReferenceEquals(again, kind) && !againSap,
                        $"{k}: grown plant can be dug up again");
                    if (k == "RaspberryBush")
                    {
                        yield return Frames(5);
                        SelfTest.Screenshot(GrowName, "grown-raspberry");
                        yield return Frames(2);
                    }
                    rig.Destroy(grown);
                }
                else
                {
                    c.Check(status == Plant.Status.WrongBiome, $"{k}: wrong biome in {biome} ({status})");
                    var none = plant.Grow();
                    rig.Track(none);
                    c.Check(none == null && Alive(view), $"{k}: does not grow, waits (not destroyed)");
                    c.Note($"{k} hover in {biome}: {plant.GetHoverText()}");
                    rig.Destroy(sapGo);
                }
                yield return Settle();
            }

            // Pending hold is ours only: a vanilla sapling still go to vanilla Grow (no tree grown here).
            if (FindSpot(origin, player.transform.forward, new[] { 4f, 6f, 8f, 10f, 12f, 14f, 16f }, 1.5f, taken, out var beechSpot))
            {
                var beech = rig.Spawn("Beech_Sapling", beechSpot, Quaternion.identity);
                var beechPlant = beech != null ? beech.GetComponent<Plant>() : null;
                if (c.Check(beechPlant != null && Alive(beechPlant.m_nview), "vanilla Beech_Sapling spawned"))
                {
                    ServerRules.TestPending = true;
                    GameObject none = null;
                    var runs = RootGate.BeforeGrow(beechPlant, ref none);
                    ServerRules.TestPending = false;
                    c.Check(runs && none == null, "pending rules: vanilla sapling not held");
                }
                rig.Destroy(beech);
            }
            else
            {
                c.Note("no free spot for a vanilla sapling: hold check on vanilla skipped");
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- replant.root ----------

    private static IEnumerator RunRoot()
    {
        var c = new Checks(RootName);
        var player = Player.m_localPlayer;
        if (player == null || ZNetScene.instance == null)
        {
            SelfTest.Fail(RootName, "no player or network list");
            yield break;
        }
        var kind = PlantCatalog.ByKey("YggaShoot");
        var rig = new Rig(player, RootName);
        var taken = new List<Vector3>();
        try
        {
            UseDefaultRules();
            RootGate.ClearLedger();
            rig.Noon();
            rig.TakeControls();
            var rules = ServerRules.Current;
            var origin = player.transform.position;
            if (!FindSpot(origin, player.transform.forward, new[] { 16f, 20f, 24f, 28f, 12f }, 3f, taken, out var rootSpot))
            {
                c.Check(false, "no free spot for an Ancient Root");
                c.Report();
                yield break;
            }
            var rootGo = rig.Spawn(PlantCatalog.RootPrefab, rootSpot, Quaternion.Euler(0f, 20f, 0f));
            yield return Settle();
            var root = rootGo != null ? rootGo.GetComponent<ResourceRoot>() : null;
            if (!c.Check(root != null && root.m_nview.IsValid(), "root spawned"))
            {
                c.Report();
                yield break;
            }
            c.Note($"root level {F(root.GetLevel())} / {F(root.m_maxLevel)}, collider bounds {F(rootGo.GetComponentInChildren<Collider>().bounds.size)}");
            c.Check(Near(root.GetLevel(), root.m_maxLevel), "new root full");

            var reach = rules.RootRange + RootGate.LookupSlack;
            var sapTaken = new List<Vector3>();
            if (!FindSpot(rootSpot, rootSpot - origin, new[] { 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f, 12f }, kind.GrowRadius + 0.3f, sapTaken,
                    out var sapSpot, p => RootGate.FindRoot(p, reach) == root))
            {
                c.Check(false, "no free spot within reach of the root");
                c.Report();
                yield break;
            }
            c.Note($"sapling {F(Vector3.Distance(sapSpot, rootSpot))} m from the root position");

            // a. Hover + drain at grow.
            var sapGo = rig.Spawn(kind.SaplingName, sapSpot, Quaternion.identity);
            yield return Settle();
            var plant = sapGo.GetComponent<Plant>();
            var drawer = sapGo.GetComponent<RootDrawer>();
            c.Check(drawer != null, "sapling has its root drawer");
            plant.UpdateHealth(100.0);
            c.Check(plant.GetStatus() == Plant.Status.Healthy, $"sapling healthy near the root ({plant.GetStatus()})");
            var hover = RootGate.HoverLines(plant) ?? "(null)";
            var full = $"(root: {Mathf.FloorToInt(root.GetLevel())} / {Mathf.RoundToInt(root.m_maxLevel)})";
            c.Check(hover.Contains("Draws 20 sap") && hover.Contains(full), $"hover: '{hover.Trim()}'");
            c.Note("hover text: " + plant.GetHoverText().Replace("\n", " / "));
            drawer.NextTry = 0f;
            var tree = plant.Grow();
            rig.Track(tree);
            c.Check(tree != null && kind.GrownPrefabs.Any(n => n.GetStableHashCode() == PrefabHash(tree)),
                $"grew into {(tree != null ? Utils.GetPrefabName(tree) : "nothing")}");
            c.Check(Near(root.GetLevel(), root.m_maxLevel - 20f, 0.01f), $"root drained 20 (level {F(root.GetLevel())})");
            c.Check(RootGate.LedgerCount == 0, "own root: drain on our copy at once, nothing in the ledger");
            if (tree != null)
            {
                var scale = tree.transform.localScale.x;
                c.Check(scale >= kind.MinScale - 0.001f && scale <= kind.MaxScale + 0.001f, $"tree scale {F(scale)}");
                yield return rig.AimAt(tree.transform.position + Vector3.up * 2f);
                yield return new WaitForSeconds(1.5f);
                SelfTest.Screenshot(RootName, "grown-shoot");
                yield return Frames(2);
                rig.Destroy(tree);
            }
            yield return Settle();

            // b. Root low: waits, no drain; throttle.
            root.m_nview.GetZDO().Set(ZDOVars.s_level, 15f);
            sapGo = rig.Spawn(kind.SaplingName, sapSpot, Quaternion.identity);
            yield return Settle();
            plant = sapGo.GetComponent<Plant>();
            drawer = sapGo.GetComponent<RootDrawer>();
            var view = sapGo.GetComponent<ZNetView>();
            plant.UpdateHealth(100.0);
            c.Check(plant.GetStatus() == Plant.Status.Healthy, $"second sapling healthy ({plant.GetStatus()})");
            drawer.NextTry = 0f;
            var none = plant.Grow();
            rig.Track(none);
            c.Check(none == null && Alive(view) && drawer.LastState == RootState.Waiting && Near(root.GetLevel(), 15f, 0.1f),
                $"root at 15: waits ({drawer.LastState}, level {F(root.GetLevel())})");
            c.Check((RootGate.HoverLines(plant) ?? "").Contains("waiting for it to refill"), "hover says waiting for the root");
            drawer.LastState = RootState.Unknown;
            none = plant.Grow();
            rig.Track(none);
            c.Check(none == null && drawer.LastState == RootState.Unknown && drawer.NextTry > Time.time + RootGate.TryInterval - 1f,
                "second try within 10 s skipped (throttle)");
            root.m_nview.GetZDO().Set(ZDOVars.s_level, 20f);
            drawer.NextTry = 0f;
            none = plant.Grow();
            rig.Track(none);
            c.Check(none == null && drawer.LastState == RootState.Waiting, "root at exactly 20 (cost 20): waits");

            // c. Pending rules: waits, no drain.
            root.m_nview.GetZDO().Set(ZDOVars.s_level, 50f);
            ServerRules.TestPending = true;
            drawer.NextTry = 0f;
            none = plant.Grow();
            rig.Track(none);
            c.Check(none == null && drawer.LastState == RootState.Pending && Near(root.GetLevel(), 50f, 0.1f), $"pending rules: waits ({drawer.LastState})");
            ServerRules.TestPending = false;

            // c2. Root of another peer (C11): drain RPC go to that peer, our copy show it only later. Ledger count the
            // drains this peer sent: at 50 with cost 20, two saplings take one drain each, the third waits.
            root.m_nview.GetZDO().Set(ZDOVars.s_level, 50f);
            RootGate.ClearLedger();
            var expected = new[] { 30f, 10f };
            for (var i = 0; i < 3; i++)
            {
                if (i > 0)
                {
                    sapGo = rig.Spawn(kind.SaplingName, sapSpot, Quaternion.identity);
                    yield return Settle();
                    plant = sapGo.GetComponent<Plant>();
                    drawer = sapGo.GetComponent<RootDrawer>();
                    view = sapGo.GetComponent<ZNetView>();
                    plant.UpdateHealth(100.0);
                    c.Check(plant.GetStatus() == Plant.Status.Healthy, $"remote root, sapling {i + 1} healthy ({plant.GetStatus()})");
                }
                drawer.NextTry = 0f;
                GameObject grew;
                LendRoot(root, true);
                try
                {
                    grew = plant.Grow();
                }
                finally
                {
                    LendRoot(root, false);
                }
                rig.Track(grew);
                var raw = root.GetLevel();
                var effective = RootGate.EffectiveLevel(root);
                if (i < 2)
                {
                    c.Check(grew != null && Near(raw, 50f, 0.1f) && RootGate.LedgerCount == 1 && Near(effective, expected[i], 0.1f),
                        $"remote root, sapling {i + 1}: grew, our copy {F(raw)}, counted as {F(effective)} (want {F(expected[i])}), ledger {RootGate.LedgerCount}");
                    rig.Destroy(grew);
                    yield return Settle();
                }
                else
                {
                    c.Check(grew == null && Alive(view) && drawer.LastState == RootState.Waiting && Near(raw, 50f, 0.1f) && Near(effective, 10f, 0.1f),
                        $"remote root, sapling 3: waits ({drawer.LastState}), our copy {F(raw)}, counted as {F(effective)}");
                    var waitHover = RootGate.HoverLines(plant) ?? "";
                    c.Check(waitHover.Contains($"(root: 10 / {Mathf.RoundToInt(root.m_maxLevel)})") && waitHover.Contains("waiting for it to refill"),
                        $"remote root hover counts the sent drains: '{waitHover.Trim()}'");
                }
            }
            // Drains landed (our copy now low): note gone, our copy again.
            root.m_nview.GetZDO().Set(ZDOVars.s_level, 10f);
            c.Check(Near(RootGate.EffectiveLevel(root), 10f, 0.1f) && RootGate.LedgerCount == 0, "drains seen on our copy: ledger empty");

            // d. Root gone: waits.
            rig.Destroy(rootGo);
            yield return Settle();
            drawer.NextTry = 0f;
            plant.UpdateHealth(100.0);
            none = plant.Grow();
            rig.Track(none);
            c.Check(none == null && Alive(view) && drawer.LastState == RootState.NoRoot, $"no root: waits ({drawer.LastState})");
            hover = RootGate.HoverLines(plant) ?? "";
            c.Check(hover.Contains("Needs an Ancient Root within 6 m"), $"hover without root: '{hover.Trim()}'");
            c.Report();
        }
        finally
        {
            RootGate.ClearLedger();
            rig.Restore();
        }
    }

    // ---------- replant.icons ----------

    private static IEnumerator RunIcons()
    {
        var c = new Checks(IconsName);
        var player = Player.m_localPlayer;
        var gui = InventoryGui.instance;
        var db = ObjectDB.instance;
        if (player == null || gui == null || db == null)
        {
            SelfTest.Fail(IconsName, "no player, inventory window or item database");
            yield break;
        }
        var rig = new Rig(player, IconsName);
        try
        {
            UseDefaultRules();
            c.Check(IconPainter.HasGraphics, "graphics device");
            foreach (var kind in PlantCatalog.All)
            {
                var item = TransplantContent.ItemPrefab(kind);
                var shared = item != null ? item.GetComponent<ItemDrop>().m_itemData.m_shared : null;
                var icon = shared != null && shared.m_icons.Length > 0 ? shared.m_icons[0] : null;
                c.Check(icon != null && IconPainter.IsPainted(icon), $"{kind.Key}: transplant icon painted ({(icon != null ? icon.name : "none")})");
                var piece = TransplantContent.SaplingPrefab(kind);
                c.Check(piece != null && ReferenceEquals(piece.GetComponent<Piece>().m_icon, icon), $"{kind.Key}: sapling piece shows it");

                // Sprout place (C19): upper-right corner; nothing painted in the stack text band along the slot bottom.
                var produce = db.GetItemPrefab(kind.ProduceItem);
                var produceIcons = produce != null ? produce.GetComponent<ItemDrop>().m_itemData.m_shared.m_icons : null;
                var plain = produceIcons != null && produceIcons.Length > 0 ? produceIcons[0] : null;
                if (icon != null && plain != null && IconPainter.IsPainted(icon))
                {
                    var copied = SproutSpread(plain, icon, out var upper, out var low, out var size);
                    c.Check(copied && upper >= 20, $"{kind.Key}: sprout painted in the upper-right corner ({upper} px changed, {size})");
                    c.Check(copied && low == 0, $"{kind.Key}: lower part untouched, stack text band free ({low} px changed)");
                }
            }
            var drop = CultivatorTiers.CultivatorDrop;
            var vanilla = drop != null ? drop.m_itemData.m_shared.m_icons[0] : null;
            c.Check(vanilla != null, "vanilla cultivator icon");
            if (vanilla != null)
            {
                c.Note($"cultivator icon {vanilla.name} rect {F(vanilla.rect.width)}x{F(vanilla.rect.height)} packed {vanilla.packed} texture {vanilla.texture.name} {vanilla.texture.width}x{vanilla.texture.height} {vanilla.texture.format}");
                c.Check(TierIcons.CultivatorIcon(vanilla, 3) == null && TierIcons.CultivatorIcon(vanilla, 1) == null, "no gem below level 4");
                var gems = new List<Sprite>();
                for (var q = 4; q <= 7; q++)
                {
                    var gem = TierIcons.CultivatorIcon(vanilla, q);
                    c.Check(gem != null && IconPainter.IsPainted(gem), $"level {q}: gem icon painted");
                    gems.Add(gem);
                }
                c.Check(gems.Distinct().Count() == 4, "four different gems");
                c.Check(ReferenceEquals(TierIcons.CultivatorIcon(vanilla, 9), gems[3]), "level above 7 = bloodgold gem");
                c.Check(ReferenceEquals(TierIcons.CultivatorIcon(vanilla, 5), gems[1]), "cached: same sprite again");
            }

            // Export (package icon and the marks) next to the screenshots.
            var probe = SelfTest.ShotPath(IconsName, "probe");
            if (probe != null)
            {
                var folder = Path.Combine(Path.GetDirectoryName(probe), "replant-icons");
                TierIcons.ExportPngs(folder);
                var want = new List<string> { "icon-256.png", "cultivator-4-black-metal.png", "cultivator-5-eitr.png", "cultivator-6-flametal.png", "cultivator-7-bloodgold.png" };
                want.AddRange(PlantCatalog.All.Select(k => "transplant-" + k.Key + ".png"));
                var missing = want.Where(f => !File.Exists(Path.Combine(folder, f))).ToArray();
                c.Check(missing.Length == 0, "exported " + (missing.Length == 0 ? $"{want.Count} PNG files" : "but missing " + string.Join(", ", missing)));
                c.Note("icons exported to " + folder);
            }
            else
            {
                c.Note("no screenshot folder: icons not exported");
            }

            // Inventory: every transplant, cultivators level 3-7 (level 5 half worn).
            var free = rig.Inv.GetEmptySlots();
            c.Check(free >= PlantCatalog.All.Count + 5, $"room in the inventory ({free} free slots)");
            var tools = new List<ItemDrop.ItemData>();
            for (var q = 3; q <= 7; q++)
            {
                var t = rig.Give(PlantCatalog.CultivatorPrefab, 1, q);
                if (t != null)
                {
                    tools.Add(t);
                }
            }
            var i5 = tools.FirstOrDefault(t => t.m_quality == 5);
            if (i5 != null)
            {
                i5.m_durability = i5.GetMaxDurability() * 0.5f;
            }
            var n = 0;
            foreach (var kind in PlantCatalog.All)
            {
                rig.Give(kind.ItemName, n % 2 == 0 ? 7 : 1);
                n++;
            }
            foreach (var t in tools)
            {
                var icon = t.GetIcon();
                if (t.m_quality >= 4 && vanilla != null)
                {
                    c.Check(ReferenceEquals(icon, TierIcons.CultivatorIcon(vanilla, t.m_quality)), $"level {t.m_quality} item shows its gem");
                }
                else
                {
                    c.Check(ReferenceEquals(icon, vanilla), $"level {t.m_quality} item shows the vanilla icon");
                }
            }
            rig.Noon();
            gui.Show(null);
            yield return new WaitForSecondsRealtime(1.5f);
            c.Check(InventoryGui.IsVisible(), "inventory open");
            SelfTest.Screenshot(IconsName, "inventory");
            yield return null;
            yield return null;

            // Quality number (C18): levels 4-7 show the gem instead of the game's level number, level 3 keep it.
            var grid = gui.m_playerGrid;
            var width = rig.Inv.GetWidth();
            string Slot(ItemDrop.ItemData t)
            {
                var el = grid.GetElement(t.m_gridPos.x, t.m_gridPos.y, width);
                return el == null ? "no slot" : $"number {(el.m_quality.enabled ? "'" + el.m_quality.text + "'" : "hidden")}, gem {IconPainter.IsPainted(el.m_icon.sprite)}";
            }
            bool NumberShown(ItemDrop.ItemData t)
            {
                var el = grid.GetElement(t.m_gridPos.x, t.m_gridPos.y, width);
                return el != null && el.m_quality.enabled && el.m_quality.text == t.m_quality.ToString(CultureInfo.InvariantCulture);
            }
            bool GemOnly(ItemDrop.ItemData t)
            {
                var el = grid.GetElement(t.m_gridPos.x, t.m_gridPos.y, width);
                return el != null && !el.m_quality.enabled && vanilla != null
                       && ReferenceEquals(el.m_icon.sprite, TierIcons.CultivatorIcon(vanilla, t.m_quality));
            }
            if (c.Check(grid != null, "inventory grid"))
            {
                grid.UpdateInventory(rig.Inv, player, null);
                foreach (var t in tools)
                {
                    if (t.m_quality >= PlantCatalog.FirstNewTier)
                    {
                        c.Check(GemOnly(t), $"level {t.m_quality} slot: gem, no level number ({Slot(t)})");
                    }
                    else
                    {
                        c.Check(NumberShown(t), $"level {t.m_quality} slot: level number shown ({Slot(t)})");
                    }
                }

                // Mod turned off: the framework take the grid postfix away (feature patch, not always on). Plugin.
                // TestInactive alone leave patches on, so me take this one away like the framework, then back.
                var gridMethod = AccessTools.Method(typeof(InventoryGrid), nameof(InventoryGrid.UpdateGui));
                var gridPostfix = AccessTools.Method(typeof(GridPatches), "UpdateGui_Postfix");
                c.Check(gridPostfix != null && HasPatch(Harmony.GetPatchInfo(gridMethod)?.Postfixes, typeof(GridPatches), ModInfo.Guid)
                        && !typeof(GridPatches).IsDefined(typeof(AlwaysOnPatchAttribute), false),
                    "grid postfix is a feature patch (gone when the mod is turned off)");
                if (gridPostfix != null)
                {
                    try
                    {
                        new Harmony(ModInfo.Guid).Unpatch(gridMethod, gridPostfix);
                        Plugin.TestInactive = true;
                        TransplantContent.Rebuild();
                        grid.UpdateInventory(rig.Inv, player, null);
                        var off = tools.Where(t => t.m_quality >= PlantCatalog.FirstNewTier).ToList();
                        c.Check(off.Count > 0 && off.All(NumberShown),
                            $"mod off: level numbers back ({string.Join("; ", off.Select(t => t.m_quality + ": " + Slot(t)).ToArray())})");
                    }
                    finally
                    {
                        Plugin.TestInactive = false;
                        TransplantContent.Rebuild();
                        RestoreGridPatch(gridMethod);
                    }
                    grid.UpdateInventory(rig.Inv, player, null);
                    c.Check(tools.Where(t => t.m_quality >= PlantCatalog.FirstNewTier).All(GemOnly),
                        "mod on again: gem, no level number");
                }
            }
            yield return new WaitForSecondsRealtime(0.5f);
            gui.Hide();
            yield return new WaitForSecondsRealtime(0.5f);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // Grid postfix back under the feature's Harmony id (framework UnpatchSelf take it away again at real turn off). Only
    // while the feature is really on and the postfix is not there yet.
    private static void RestoreGridPatch(MethodBase gridMethod)
    {
        try
        {
            if (!Plugin.FeatureActive || HasPatch(Harmony.GetPatchInfo(gridMethod)?.Postfixes, typeof(GridPatches), ModInfo.Guid))
            {
                return;
            }
            new Harmony(ModInfo.Guid).CreateClassProcessor(typeof(GridPatches)).Patch();
        }
        catch (Exception e)
        {
            SelfTest.Note(IconsName, $"restore grid postfix failed: {e.Message}");
        }
    }
#endif
}
