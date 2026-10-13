using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;
#endif

namespace MC.Combat.CrossbowStaysLoadedMod;

// Me = self tests (Debug build only; Release build has three empty methods and no test code). Run by
// tools/Test-InWorld.ps1 (single-player world) and tools/Test-Multiplayer.ps1 (dedicated server + this game).
// One block per open TESTING.md item:
//   SelfTests.Weapons.cs   W01 to W05, Weapons settings (single player: forced in memory; multiplayer: real settings)
//     crossbow.defaults / crossbow.mp.defaults            W01 crossbows + hook keep load, Dundr reload and pay eitr
//     crossbow.extra-items / crossbow.mp.extra-items      W02 Dundr added live
//     crossbow.mp.cfg-file                                W02 the same setting through an edit of the cfg file
//     crossbow.excluded / crossbow.mp.excluded            W03 one crossbow excluded, then not, then again while loaded
//     crossbow.crossbows-off / crossbow.mp.crossbows-off  W04 Crossbows = false, hook still keep; again while loaded
//     crossbow.debug-list / crossbow.mp.debug-list        W05 [reload weapon] log lines
//     crossbow.input.hotbar                               W01 swap with real hotbar key presses
//   SelfTests.Handoff.cs   M02 to M04, the half one game can check
//     crossbow.handoff          M02 mark travel in the data of a dropped crossbow and of a chest
//     crossbow.relog.chest      M02 chest read again from the world save after a real log out
//     crossbow.disabled.stamped M03 game started with me off reload a marked crossbow
//     crossbow.vanilla-shot     M04 shot fired by game code without me, crossbow come back: reload, also after repair
//   SelfTests.Mp.cs        M01, M05
//     crossbow.mp.remote-flag     M01 loaded flag other players read, through swap and shot
//     crossbow.mp.vanilla-server  M05 server without me: me active, load kept
// Every swap go through Humanoid.UseItem (inventory right click; hotbar key call it too) or a real key, every load
// through the game's own reload. Rig put back the player (hands, items, ammo, skills, eitr, place) and destroy what
// a test spawn. Single-player test never set a ConfigEntry.
internal static partial class SelfTests
{
    // BindConfig (every start, also while me is off): log watch for the [reload weapon] lines (written at first spawn),
    // the multiplayer test for a server without me, and the test that run in a game started with me off.
    [Conditional("DEBUG")]
    internal static void RegisterAlways()
    {
#if DEBUG
        try
        {
            InstallDumpWatch();
            SelfTest.RegisterMultiplayer(MpVanillaServerName, VanillaServerScenario, () => Run(MpVanillaServerName, 2, VanillaServerBody));
            SelfTest.RegisterDisabledStart(DisabledName, DisabledLeave, DisabledCheck);
        }
        catch (Exception e)
        {
            // Test plumbing must never stop the mod from starting.
            Log.Warning($"Self test set-up failed: {e}");
        }
#endif
    }

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        SelfTest.Register(DefaultsName, () => Run(DefaultsName, 5, (rig, c) => DefaultsBody(rig, c, false)));
        SelfTest.Register(ExtraName, () => Run(ExtraName, 2, (rig, c) => ExtraBody(rig, c, false)));
        SelfTest.Register(ExcludedName, () => Run(ExcludedName, 3, (rig, c) => ExcludedBody(rig, c, false)));
        SelfTest.Register(CrossbowsOffName, () => Run(CrossbowsOffName, 3, (rig, c) => CrossbowsOffBody(rig, c, false)));
        SelfTest.Register(DebugListName, () => RunDebugList(DebugListName, false));
        SelfTest.Register(InputName, RunInput);
        SelfTest.Register(HandoffName, () => Run(HandoffName, 2, HandoffBody));
        SelfTest.Register(VanillaShotName, () => Run(VanillaShotName, 3, VanillaShotBody));
        SelfTest.RegisterRelog(RelogChestName, RelogBefore, null, RelogAfter);
        SelfTest.RegisterMultiplayer(MpDefaultsName, SelfTest.Modded, () => Run(MpDefaultsName, 5, (rig, c) => DefaultsBody(rig, c, true)));
        SelfTest.RegisterMultiplayer(MpExtraName, SelfTest.Modded, () => Run(MpExtraName, 2, (rig, c) => ExtraBody(rig, c, true)));
        SelfTest.RegisterMultiplayer(MpCfgFileName, SelfTest.Modded, RunMpCfgFile);
        SelfTest.RegisterMultiplayer(MpExcludedName, SelfTest.Modded, () => Run(MpExcludedName, 3, (rig, c) => ExcludedBody(rig, c, true)));
        SelfTest.RegisterMultiplayer(MpCrossbowsOffName, SelfTest.Modded, () => Run(MpCrossbowsOffName, 3, (rig, c) => CrossbowsOffBody(rig, c, true)));
        SelfTest.RegisterMultiplayer(MpDebugListName, SelfTest.Modded, () => RunDebugList(MpDebugListName, true));
        SelfTest.RegisterMultiplayer(MpRemoteFlagName, SelfTest.Modded, () => Run(MpRemoteFlagName, 3, RemoteFlagBody));
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        SelfTest.Unregister(DefaultsName);
        SelfTest.Unregister(ExtraName);
        SelfTest.Unregister(ExcludedName);
        SelfTest.Unregister(CrossbowsOffName);
        SelfTest.Unregister(DebugListName);
        SelfTest.Unregister(InputName);
        SelfTest.Unregister(HandoffName);
        SelfTest.Unregister(VanillaShotName);
        SelfTest.UnregisterRelog(RelogChestName);
        SelfTest.UnregisterMultiplayer(MpDefaultsName);
        SelfTest.UnregisterMultiplayer(MpExtraName);
        SelfTest.UnregisterMultiplayer(MpCfgFileName);
        SelfTest.UnregisterMultiplayer(MpExcludedName);
        SelfTest.UnregisterMultiplayer(MpCrossbowsOffName);
        SelfTest.UnregisterMultiplayer(MpDebugListName);
        SelfTest.UnregisterMultiplayer(MpRemoteFlagName);
#endif
    }

#if DEBUG
    private const string DefaultsName = "crossbow.defaults";
    private const string ExtraName = "crossbow.extra-items";
    private const string ExcludedName = "crossbow.excluded";
    private const string CrossbowsOffName = "crossbow.crossbows-off";
    private const string DebugListName = "crossbow.debug-list";
    private const string InputName = "crossbow.input.hotbar";
    private const string HandoffName = "crossbow.handoff";
    private const string VanillaShotName = "crossbow.vanilla-shot";
    private const string RelogChestName = "crossbow.relog.chest";
    private const string DisabledName = "crossbow.disabled.stamped";
    private const string MpDefaultsName = "crossbow.mp.defaults";
    private const string MpExtraName = "crossbow.mp.extra-items";
    private const string MpCfgFileName = "crossbow.mp.cfg-file";
    private const string MpExcludedName = "crossbow.mp.excluded";
    private const string MpCrossbowsOffName = "crossbow.mp.crossbows-off";
    private const string MpDebugListName = "crossbow.mp.debug-list";
    private const string MpRemoteFlagName = "crossbow.mp.remote-flag";
    private const string MpVanillaServerName = "crossbow.mp.vanilla-server";
    private const string VanillaServerScenario = "vanilla-server";

    // Vanilla names me use. CrossbowArbalest, BoltBone, Bow, piece_chest_wood: used by passing self tests of other MC
    // mods on 1.0.17. GrapplingHook, StaffLightning (Dundr): seen in my own [reload weapon] log lines (TESTING.md T22).
    // CrossbowRipper: asset name (docs/game/combat.md), checked at run time; any other usable crossbow do too.
    private const string ArbalestName = "CrossbowArbalest";
    private const string RipperName = "CrossbowRipper";
    private const string HookName = "GrapplingHook";
    private const string DundrName = "StaffLightning";
    private const string BowName = "Bow";
    private const string BoltName = "BoltBone";
    private const string ChestName = "piece_chest_wood";
    private const string DumpMark = "[reload weapon]";

    private static readonly WaitForFixedUpdate Fixed = new WaitForFixedUpdate();

    // ---------- result of one test ----------

    private sealed class Checks
    {
        private readonly string _name;
        private readonly List<string> _failures = new List<string>();
        private int _count;

        // What the test showed, for the PASS line.
        internal string Summary = "";

        internal Checks(string name) => _name = name;

        // problem = what is wrong when ok is false (only then shown).
        internal bool Check(bool ok, string problem)
        {
            _count++;
            if (!ok)
            {
                _failures.Add(problem);
            }
            return ok;
        }

        internal void Note(string detail) => SelfTest.Note(_name, detail);

        internal void Report()
        {
            if (_failures.Count == 0 && _count > 0)
            {
                SelfTest.Pass(_name, $"{_count} checks OK" + (Summary.Length > 0 ? ": " + Summary : ""));
            }
            else if (_failures.Count == 0)
            {
                SelfTest.Fail(_name, "no check ran");
            }
            else
            {
                SelfTest.Fail(_name, $"{_failures.Count} of {_count} checks failed: {string.Join("; ", _failures.ToArray())}");
            }
        }
    }

    // One test: rig, body, rig put back (also when body throw or time out), then the verdict.
    private static IEnumerator Run(string name, int freeSlots, Func<Rig, Checks, IEnumerator> body)
    {
        var rig = Rig.Create(name, freeSlots);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(name);
        try
        {
            yield return body(rig, c);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- Weapons settings for one test ----------

    // Single-player run: forced in memory (WeaponFilter.TestCrossbows, and the two name lists rebuilt from test text),
    // cfg of the player never written. Multiplayer run: the real settings (the run has its own config folder), so the
    // change go through SettingChanged like one made in ConfigurationManager.
    private sealed class WeaponSettings
    {
        private readonly bool _real;
        private readonly bool _crossbows;
        private readonly string _extra;
        private readonly string _excluded;

        internal WeaponSettings(bool real)
        {
            _real = real;
            _crossbows = Plugin.KeepCrossbows.Value;
            _extra = Plugin.ExtraItems.Value;
            _excluded = Plugin.ExcludedItems.Value;
        }

        internal static bool DefaultCrossbows => (bool)Plugin.KeepCrossbows.DefaultValue;

        internal static string DefaultExtra => (string)Plugin.ExtraItems.DefaultValue;

        internal static string DefaultExcluded => (string)Plugin.ExcludedItems.DefaultValue;

        // Settings as a fresh install has them.
        internal static bool AtDefaults =>
            Plugin.KeepCrossbows.Value == DefaultCrossbows
            && string.Equals(Plugin.ExtraItems.Value, DefaultExtra, StringComparison.Ordinal)
            && string.Equals(Plugin.ExcludedItems.Value, DefaultExcluded, StringComparison.Ordinal);

        internal static string Current =>
            $"Crossbows = {Plugin.KeepCrossbows.Value}, ExtraItems = '{Plugin.ExtraItems.Value}', ExcludedItems = '{Plugin.ExcludedItems.Value}'";

        internal string How => _real ? "set in the settings" : "forced in memory, the cfg file is not touched";

        internal void Set(bool crossbows, string extra, string excluded)
        {
            if (_real)
            {
                Plugin.KeepCrossbows.Value = crossbows;
                Plugin.ExtraItems.Value = extra;
                Plugin.ExcludedItems.Value = excluded;
            }
            else
            {
                WeaponFilter.TestCrossbows = crossbows;
                WeaponFilter.Rebuild(extra, excluded);
            }
        }

        internal void Defaults() => Set(DefaultCrossbows, DefaultExtra, DefaultExcluded);

        internal void Restore()
        {
            if (_real)
            {
                Plugin.KeepCrossbows.Value = _crossbows;
                Plugin.ExtraItems.Value = _extra;
                Plugin.ExcludedItems.Value = _excluded;
            }
            else
            {
                WeaponFilter.TestCrossbows = null;
                WeaponFilter.Rebuild(Plugin.ExtraItems.Value, Plugin.ExcludedItems.Value);
            }
        }
    }

    // ---------- what one test change on the player and in the world ----------

    // Restore (finally, no yield) put everything back.
    private sealed class Rig
    {
        // Eitr the test character get for Dundr (it has none of its own: no eitr food eaten).
        private const float TestEitr = 200f;

        internal readonly string Test;
        internal readonly Player P;
        internal readonly Inventory Inv;
        private readonly ItemDrop.ItemData _right;
        private readonly ItemDrop.ItemData _left;
        private readonly ItemDrop.ItemData _ammo;
        private readonly Vector3 _position;
        private readonly Quaternion _rotation;
        private readonly float _eitr;
        private readonly float _maxEitr;
        private readonly float _foodTimer;
        private readonly Dictionary<Skills.SkillType, Vector2> _skills = new Dictionary<Skills.SkillType, Vector2>();
        private readonly List<ItemDrop.ItemData> _items = new List<ItemDrop.ItemData>();
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly Dictionary<string, int> _ammoBefore = new Dictionary<string, int>();
        private WeaponSettings _settings;
        private bool _eitrOn;
        private bool _moved;

        private Rig(string test, Player player)
        {
            Test = test;
            P = player;
            Inv = player.GetInventory();
            _right = player.m_rightItem;
            _left = player.m_leftItem;
            _ammo = player.m_ammoItem;
            _position = player.transform.position;
            _rotation = player.transform.rotation;
            _eitr = player.m_eitr;
            _maxEitr = player.m_maxEitr;
            _foodTimer = player.m_foodUpdateTimer;
            foreach (var pair in player.m_skills.m_skillData)
            {
                _skills[pair.Key] = new Vector2(pair.Value.m_level, pair.Value.m_accumulator);
            }
        }

        internal static Rig Create(string test, int freeSlots)
        {
            var player = Player.m_localPlayer;
            if (player == null || ObjectDB.instance == null || ZNetScene.instance == null || ZNet.instance == null)
            {
                SelfTest.Fail(test, "no local player or no world");
                return null;
            }
            // Earlier test stopped half way (time-out) with my patches off: on again, else every later test fail too.
            try
            {
                var plugin = Self();
                if (plugin != null && plugin.TestOff)
                {
                    plugin.TestTurnOn();
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Self test set-up (patches back on) failed: {e}");
            }
            // Build mode is no stop: EmptyHands put the tool away.
            if (player.IsDead() || player.IsTeleporting() || player.IsSwimming() || player.IsAttached())
            {
                SelfTest.Fail(test, $"the player cannot swap weapons now (dead {player.IsDead()}, teleporting {player.IsTeleporting()}, "
                                    + $"swimming {player.IsSwimming()}, sitting or attached {player.IsAttached()})");
                return null;
            }
            var free = player.GetInventory().GetEmptySlots();
            if (free < freeSlots)
            {
                SelfTest.Fail(test, $"the inventory has {free} free slot(s), the test needs {freeSlots}");
                return null;
            }
            return new Rig(test, player);
        }

        // One item from its prefab into a free slot (never dropped on the ground when the inventory is full).
        internal ItemDrop.ItemData Give(string prefab) => GiveAt(prefab, -1);

        // Same, into hotbar slot x (row 0); x < 0 = any free slot.
        internal ItemDrop.ItemData GiveAt(string prefab, int x)
        {
            var item = Inv.AddItem(prefab, 1, 1, 0, 0L, "", new Vector2i(x, x < 0 ? -1 : 0), false, false, false);
            if (item != null && Inv.ContainsItem(item))
            {
                _items.Add(item);
                return item;
            }
            return null;
        }

        // Ammo stack (may join a stack the player own): what is left of it is taken back at the end by count.
        internal bool GiveAmmo(string prefab, int count)
        {
            var shared = SharedOf(prefab);
            if (shared == null)
            {
                return false;
            }
            if (!_ammoBefore.ContainsKey(shared.m_name))
            {
                _ammoBefore[shared.m_name] = Inv.CountItems(shared.m_name);
            }
            var before = Inv.CountItems(shared.m_name);
            Inv.AddItem(prefab, count, 1, 0, 0L, "", new Vector2i(-1, -1), false, false, false);
            return Inv.CountItems(shared.m_name) == before + count;
        }

        // Item that came to the inventory during the test (picked up, taken from a chest): removed at the end.
        internal void Own(ItemDrop.ItemData item)
        {
            if (item != null && !_items.Contains(item))
            {
                _items.Add(item);
            }
        }

        // Item the test leave in the inventory on purpose (for the game started with me off).
        internal void Keep(ItemDrop.ItemData item) => _items.Remove(item);

        // Thing the test made in the world: destroyed at the end if still there.
        internal void Track(GameObject go)
        {
            if (go != null && !_spawned.Contains(go))
            {
                _spawned.Add(go);
            }
        }

        // Thing the test leave in the world on purpose (chest that must live through the log out).
        internal void Untrack(GameObject go) => _spawned.Remove(go);

        internal WeaponSettings UseSettings(bool real)
        {
            if (_settings == null)
            {
                _settings = new WeaponSettings(real);
            }
            return _settings;
        }

        // Hands empty before the first swap (what the player held come back at the end).
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

        // Eitr for the test: max held up by Tick (vanilla set max eitr from eaten food once a second: timer held at 0
        // meanwhile, so no food is eaten, no stat and no tutorial touched). Eitr itself only filled, never held: the
        // game's own use of it stays measurable.
        internal void EitrOn()
        {
            _eitrOn = true;
            Tick();
            Refill();
        }

        internal void Refill()
        {
            if (_eitrOn)
            {
                P.m_eitr = P.m_maxEitr;
            }
        }

        // Called from every wait loop.
        internal void Tick()
        {
            if (!_eitrOn)
            {
                return;
            }
            P.m_foodUpdateTimer = 0f;
            if (P.m_maxEitr < TestEitr)
            {
                P.m_maxEitr = TestEitr;
            }
        }

        // Test turn or push the player (a shot does): place and facing put back at the end.
        internal void Moves() => _moved = true;

        internal void Restore()
        {
            Safe("settings", () =>
            {
                if (_settings != null)
                {
                    _settings.Restore();
                }
            });
            Safe("items", () =>
            {
                foreach (var item in _items)
                {
                    if (item == null)
                    {
                        continue;
                    }
                    P.RemoveEquipAction(item);
                    if (P.IsItemEquiped(item))
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
            Safe("patches", () =>
            {
                var plugin = Self();
                if (plugin != null && plugin.TestOff)
                {
                    plugin.TestTurnOn();
                }
            });
            Safe("ammo", () =>
            {
                if (P.m_ammoItem != null && !ReferenceEquals(P.m_ammoItem, _ammo))
                {
                    P.UnequipItem(P.m_ammoItem, false);
                }
                foreach (var pair in _ammoBefore)
                {
                    var surplus = Inv.CountItems(pair.Key) - pair.Value;
                    if (surplus > 0)
                    {
                        Inv.RemoveItem(pair.Key, surplus);
                    }
                }
                _ammoBefore.Clear();
            });
            Safe("equipment", () =>
            {
                foreach (var item in new[] { _right, _left, _ammo })
                {
                    if (item != null && Inv.ContainsItem(item) && !P.IsItemEquiped(item))
                    {
                        P.EquipItem(item, false);
                    }
                }
            });
            Safe("spawned", () =>
            {
                foreach (var go in _spawned)
                {
                    DestroyObject(go);
                }
                _spawned.Clear();
            });
            Safe("skills", () =>
            {
                var data = P.m_skills.m_skillData;
                foreach (var type in new List<Skills.SkillType>(data.Keys))
                {
                    if (_skills.TryGetValue(type, out var was))
                    {
                        data[type].m_level = was.x;
                        data[type].m_accumulator = was.y;
                    }
                    else
                    {
                        data.Remove(type); // skill the test trained from nothing
                    }
                }
            });
            Safe("body", () =>
            {
                if (_eitrOn)
                {
                    P.m_maxEitr = _maxEitr;
                    P.m_eitr = Mathf.Min(_eitr, _maxEitr);
                    P.m_foodUpdateTimer = _foodTimer;
                }
                if (_moved)
                {
                    P.transform.position = _position;
                    P.m_body.position = _position;
                    P.m_body.linearVelocity = Vector3.zero;
                    P.transform.rotation = _rotation;
                    P.m_body.rotation = _rotation;
                }
            });
        }
    }

    private static void Safe(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Log.Warning($"Self test clean-up ({what}) failed: {e}");
        }
    }

    // ---------- small helpers ----------

    private static Plugin Self() =>
        Chainloader.PluginInfos.TryGetValue(ModInfo.Guid, out var info) ? info.Instance as Plugin : null;

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string PrefabOf(ItemDrop.ItemData item) => WeaponFilter.PrefabName(item) ?? "?";

    private static string MarkOf(ItemDrop.ItemData item) =>
        item != null && item.m_customData != null && item.m_customData.TryGetValue(LoadedState.Key, out var value) ? value : null;

    private static ItemDrop.ItemData.SharedData SharedOf(string prefab)
    {
        var go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
        var drop = go != null ? go.GetComponent<ItemDrop>() : null;
        return drop != null && drop.m_itemData != null ? drop.m_itemData.m_shared : null;
    }

    // Every item of the game whose main attack must be reloaded (same walk as the Debug [reload weapon] dump).
    private static Dictionary<string, ItemDrop.ItemData.SharedData> ReloadWeapons()
    {
        var found = new Dictionary<string, ItemDrop.ItemData.SharedData>(StringComparer.Ordinal);
        foreach (var prefab in ObjectDB.instance.m_items)
        {
            if (prefab == null)
            {
                continue;
            }
            var drop = prefab.GetComponent<ItemDrop>();
            var shared = drop != null && drop.m_itemData != null ? drop.m_itemData.m_shared : null;
            if (shared != null && shared.m_attack != null && shared.m_attack.m_requiresReload)
            {
                found[prefab.name] = shared;
            }
        }
        return found;
    }

    // Crossbow a player can hold: Crossbows skill, reload, has an icon, no DLC lock, no crafting half-product.
    private static bool UsableCrossbow(string name, ItemDrop.ItemData.SharedData shared) =>
        shared != null && shared.m_skillType == Skills.SkillType.Crossbows && shared.m_attack != null
        && shared.m_attack.m_requiresReload && !shared.m_attack.m_consumeItem && shared.m_icons != null
        && shared.m_icons.Length > 0 && string.IsNullOrEmpty(shared.m_dlc)
        && name.IndexOf("Uncooked", StringComparison.OrdinalIgnoreCase) < 0;

    // Second crossbow next to the Arbalest: the Ripper when the game has it, else the first other usable one.
    private static string SecondCrossbow()
    {
        if (UsableCrossbow(RipperName, SharedOf(RipperName)))
        {
            return RipperName;
        }
        foreach (var pair in ReloadWeapons())
        {
            if (pair.Key != ArbalestName && UsableCrossbow(pair.Key, pair.Value))
            {
                return pair.Key;
            }
        }
        return null;
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

    // Wait until condition holds or (real) seconds run out.
    private static IEnumerator Until(Func<bool> condition, float seconds, Rig rig = null)
    {
        var begin = Time.realtimeSinceStartup;
        while (!condition() && Time.realtimeSinceStartup - begin < seconds)
        {
            if (rig != null)
            {
                rig.Tick();
            }
            yield return null;
        }
    }

    // ---------- swap and load ----------

    // What the game did with one weapon that came into the hand.
    private sealed class Swap
    {
        internal bool InHand;       // weapon reached the hand
        internal bool LoadedAtOnce; // loaded on every physics tick right after (and through the watch time)
        internal bool ReloadQueued; // game queued a reload for it
        internal bool Loaded;       // loaded at the end of the wait
        internal float Seconds;     // game seconds from "in hand" to "loaded" (or to the end of the wait)
        internal float EitrUsed;    // most eitr missing while me watched, against the level before the swap

        internal void Clear()
        {
            InHand = false;
            LoadedAtOnce = false;
            ReloadQueued = false;
            Loaded = false;
            Seconds = 0f;
            EitrUsed = 0f;
        }

        public override string ToString() =>
            $"in hand {InHand}, loaded at once {LoadedAtOnce}, reload started {ReloadQueued}, loaded {Loaded} after {F(Seconds)} s, eitr used {F(EitrUsed)}";
    }

    // Weapon into the hand the way a right click in the inventory do it (Humanoid.UseItem; the hotbar key call the
    // same method after it looked up the slot), then watch what the game do with its load. Already in hand = only watch.
    // loadWait: real seconds me wait for a reload to end. watch: game seconds me keep looking after it is loaded.
    private static IEnumerator Take(Rig rig, ItemDrop.ItemData item, Swap s, float loadWait, float watch = 0f)
    {
        s.Clear();
        var p = rig.P;
        var begin = Time.realtimeSinceStartup;
        while ((p.InAttack() || p.InDodge()) && Time.realtimeSinceStartup - begin < 4f)
        {
            rig.Tick();
            yield return null;
        }
        var eitr = p.m_eitr;
        if (!ReferenceEquals(p.GetCurrentWeapon(), item))
        {
            p.UseItem(null, item, true);
        }
        yield return Arrive(rig, item, s, eitr, loadWait, watch);
    }

    // Wait for the weapon in the hand (however it got there), then watch its load. Physics ticks: vanilla decide about
    // the reload in Player.FixedUpdate (UpdateWeaponLoading), my prefix sit there too.
    private static IEnumerator Arrive(Rig rig, ItemDrop.ItemData item, Swap s, float eitrBefore, float loadWait, float watch)
    {
        var p = rig.P;
        var begin = Time.realtimeSinceStartup;
        while (!ReferenceEquals(p.GetCurrentWeapon(), item) && Time.realtimeSinceStartup - begin < 6f)
        {
            rig.Tick();
            yield return Fixed;
        }
        if (!ReferenceEquals(p.GetCurrentWeapon(), item))
        {
            yield break;
        }
        s.InHand = true;
        var since = Time.time;
        var low = Mathf.Min(eitrBefore, p.m_eitr);

        // Two whole physics ticks with the weapon in hand first (a weapon with no equip time is equipped outside the
        // physics tick), then three samples.
        for (var i = 0; i < 2; i++)
        {
            rig.Tick();
            yield return Fixed;
            low = Mathf.Min(low, p.m_eitr);
        }
        s.LoadedAtOnce = true;
        for (var i = 0; i < 3; i++)
        {
            if (!ReferenceEquals(p.m_weaponLoaded, item))
            {
                s.LoadedAtOnce = false;
            }
            if (p.IsReloadActionQueued())
            {
                s.ReloadQueued = true;
            }
            rig.Tick();
            yield return Fixed;
            low = Mathf.Min(low, p.m_eitr);
        }

        begin = Time.realtimeSinceStartup;
        while (!ReferenceEquals(p.m_weaponLoaded, item) && Time.realtimeSinceStartup - begin < loadWait)
        {
            rig.Tick();
            yield return Fixed;
            low = Mathf.Min(low, p.m_eitr);
        }
        s.Loaded = ReferenceEquals(p.m_weaponLoaded, item);
        s.Seconds = Time.time - since;

        var until = Time.time + watch;
        while (watch > 0f && Time.time < until)
        {
            if (!ReferenceEquals(p.m_weaponLoaded, item))
            {
                s.LoadedAtOnce = false;
            }
            if (p.IsReloadActionQueued())
            {
                s.ReloadQueued = true;
            }
            rig.Tick();
            yield return Fixed;
            low = Mathf.Min(low, p.m_eitr);
        }
        s.EitrUsed = Mathf.Max(0f, eitrBefore - low);
    }

    // Weapon out of the hand the way its hotbar key (pressed again) or a right click do it.
    private static IEnumerator PutAway(Rig rig, ItemDrop.ItemData item)
    {
        var p = rig.P;
        if (p.IsItemEquiped(item))
        {
            p.UseItem(null, item, true);
        }
        yield return Until(() => !p.IsItemEquiped(item), 5f, rig);
    }

    // Eitr one reload of this weapon cost: Player.UpdateActionQueue drain m_reloadEitrDrain per second for the whole
    // reload time. 0 for crossbows and the hook.
    private static float ReloadEitr(ItemDrop.ItemData weapon) =>
        weapon.m_shared.m_attack.m_reloadEitrDrain * weapon.GetWeaponLoadingTime() * Game.m_eitrRate;

    // One weapon through a swap: in hand and loaded (game's own reload when needed), other weapon in hand, back in
    // hand. keep = load must be there at once, no reload, no eitr. Else: game must reload it (and take eitr again when
    // its reload cost eitr). A step that cannot even be done end the cycle with a failed check.
    private static IEnumerator Cycle(Rig rig, Checks c, ItemDrop.ItemData weapon, ItemDrop.ItemData other, bool keep, string label)
    {
        var p = rig.P;
        var s = new Swap();
        var name = PrefabOf(weapon);
        var loadTime = weapon.GetWeaponLoadingTime();
        var eitrCost = ReloadEitr(weapon);

        // 1. In hand, loaded.
        rig.Refill();
        yield return Take(rig, weapon, s, loadTime + 4f);
        if (!c.Check(s.InHand && s.Loaded, $"{label}: {name} did not get into the hand and loaded ({s}; eitr {F(p.m_eitr)} of {F(p.m_maxEitr)})"))
        {
            yield break;
        }
        c.Check(LoadedState.IsEligible(weapon) == keep,
            $"{label}: the mod's list says 'keeps its load' = {LoadedState.IsEligible(weapon)} for {name}, expected {keep}");

        // 2. Other weapon in hand: game forget the load (vanilla), mark stay on the item only when it is on the list.
        yield return Take(rig, other, s, 0f);
        if (!c.Check(s.InHand && !p.IsItemEquiped(weapon) && p.m_weaponLoaded == null,
                $"{label}: swapping to {PrefabOf(other)} did not put {name} away (other weapon in hand {s.InHand}, {name} still equipped {p.IsItemEquiped(weapon)})"))
        {
            yield break;
        }
        c.Check(LoadedState.IsLoaded(weapon) == keep, keep
            ? $"{label}: {name} was put away loaded but carries no valid Loaded mark (mark '{MarkOf(weapon)}', durability {F(weapon.m_durability)})"
            : $"{label}: {name} is not on the list but carries a valid Loaded mark ('{MarkOf(weapon)}')");

        // 3. Back in hand.
        rig.Refill();
        yield return Take(rig, weapon, s, loadTime + 4f, keep ? 1f : 0f);
        if (!c.Check(s.InHand, $"{label}: {name} did not come back into the hand"))
        {
            yield break;
        }
        if (keep)
        {
            c.Check(s.LoadedAtOnce && !s.ReloadQueued, $"{label}: {name} was not loaded at once after swapping back ({s})");
            if (eitrCost > 0f)
            {
                c.Check(s.EitrUsed < 0.5f, $"{label}: {name} kept its load but {F(s.EitrUsed)} eitr was used after swapping back (a reload costs {F(eitrCost)})");
            }
        }
        else
        {
            c.Check(!s.LoadedAtOnce && s.ReloadQueued, $"{label}: {name} did not start a reload after swapping back ({s})");
            c.Check(s.Loaded && s.Seconds >= 0.8f * loadTime,
                $"{label}: the reload of {name} after swapping back took {F(s.Seconds)} s (loaded {s.Loaded}), a full reload takes {F(loadTime)} s");
            if (eitrCost > 0f)
            {
                c.Check(s.EitrUsed >= 0.5f * eitrCost,
                    $"{label}: the reload of {name} after swapping back used {F(s.EitrUsed)} eitr, a reload costs about {F(eitrCost)}");
            }
        }
        c.Note($"{label}: {name} back in hand: {s} (full reload {F(loadTime)} s, reload eitr {F(eitrCost)})");
    }

    // ---------- shot, ground, chest, repair ----------

    private sealed class Shot
    {
        internal bool Started;    // game took the attack
        internal bool Left;       // the load is gone (the animation fired it)
        internal bool Projectile; // a projectile came out
    }

    // One real shot: Humanoid.StartAttack (what the attack button call), the animation fire it. Bolt destroyed as
    // soon as it is out: it must hit nothing and leave nothing.
    private static IEnumerator Fire(Rig rig, ItemDrop.ItemData weapon, Shot shot)
    {
        var p = rig.P;
        shot.Started = false;
        shot.Left = false;
        shot.Projectile = false;
        rig.Moves();
        yield return Until(() => !p.InAttack() && !p.InDodge() && !p.IsStaggering(), 4f, rig);
        weapon.m_lastProjectile = null;
        var begin = Time.realtimeSinceStartup;
        while (!shot.Started && Time.realtimeSinceStartup - begin < 3f)
        {
            // Refused while the equip animation still play (Attack.Start: InMinorAction): try again next frame.
            shot.Started = p.StartAttack(null, false);
            if (!shot.Started)
            {
                rig.Tick();
                yield return null;
            }
        }
        if (!shot.Started)
        {
            yield break;
        }
        yield return Until(() => !ReferenceEquals(p.m_weaponLoaded, weapon), 4f, rig);
        shot.Left = !ReferenceEquals(p.m_weaponLoaded, weapon);
        if (shot.Left)
        {
            yield return Until(() => weapon.m_lastProjectile != null, 1f, rig);
        }
        if (weapon.m_lastProjectile != null)
        {
            shot.Projectile = true;
            DestroyObject(weapon.m_lastProjectile);
            weapon.m_lastProjectile = null;
        }
        yield return Until(() => !p.InAttack(), 4f, rig);
    }

    // On the ground the way the inventory screen do it (Humanoid.DropItem). Null = game refused.
    private static ItemDrop Drop(Rig rig, ItemDrop.ItemData item)
    {
        var before = new HashSet<ItemDrop>(ItemDrop.s_instances);
        var prefab = item.m_dropPrefab;
        if (!rig.P.DropItem(null, item, item.m_stack))
        {
            return null;
        }
        foreach (var drop in ItemDrop.s_instances)
        {
            if (drop != null && !before.Contains(drop) && drop.m_itemData != null && ReferenceEquals(drop.m_itemData.m_dropPrefab, prefab))
            {
                rig.Track(drop.gameObject);
                return drop;
            }
        }
        return null;
    }

    // E on the item on the ground (ItemDrop.Interact). Game refuse for half a second after the drop: me wait for it.
    // Hands must be full, else the game equip the weapon by itself.
    private static IEnumerator PickUp(Rig rig, ItemDrop drop, bool[] ok)
    {
        ok[0] = false;
        var item = drop.m_itemData;
        var begin = Time.realtimeSinceStartup;
        while (drop != null && !rig.Inv.ContainsItem(item) && Time.realtimeSinceStartup - begin < 5f)
        {
            if (drop.CanPickup())
            {
                drop.Interact(rig.P, false, false);
            }
            if (rig.Inv.ContainsItem(item))
            {
                break;
            }
            rig.Tick();
            yield return null;
        }
        ok[0] = rig.Inv.ContainsItem(item);
        if (ok[0])
        {
            rig.Own(item);
        }
    }

    // What another game would build from the data of the object on the ground: fresh item, filled by the game's own
    // reader from the ZDO.
    private static ItemDrop.ItemData FromGroundData(ItemDrop drop)
    {
        var view = drop.GetComponent<ZNetView>();
        if (view == null || !view.IsValid())
        {
            return null;
        }
        var copy = drop.m_itemData.Clone();
        copy.m_customData = new Dictionary<string, string>();
        copy.m_durability = -1f;
        ItemDrop.LoadFromZDO(copy, view.GetZDO());
        return copy;
    }

    // Wood chest on the ground two steps ahead. lasting = it must live through a log out: wear left on (it stand on
    // the ground like one a player built); else wear off, so nothing can break it during the test.
    private static GameObject SpawnChest(Rig rig, bool lasting)
    {
        var prefab = ZNetScene.instance.GetPrefab(ChestName);
        if (prefab == null || prefab.GetComponent<Container>() == null)
        {
            return null;
        }
        var p = rig.P;
        var forward = p.transform.forward;
        forward.y = 0f;
        var pos = p.transform.position + (forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward) * 2f;
        pos.y = ZoneSystem.instance.GetGroundHeight(pos);
        var go = Object.Instantiate(prefab, pos, Quaternion.identity);
        rig.Track(go);
        var wear = go.GetComponent<WearNTear>();
        if (!lasting && wear != null)
        {
            wear.enabled = false;
        }
        return go;
    }

    // Same three calls as Ctrl + click on an item with a chest open (InventoryGui.OnSelectedItem, Move).
    private static void IntoChest(Player p, Container chest, ItemDrop.ItemData item)
    {
        p.RemoveEquipAction(item);
        p.UnequipItem(item);
        chest.GetInventory().MoveItemToThis(p.GetInventory(), item);
    }

    private static void OutOfChest(Player p, Container chest, ItemDrop.ItemData item) =>
        p.GetInventory().MoveItemToThis(chest.GetInventory(), item);

    private static ItemDrop.ItemData FindIn(Inventory inventory, string prefab)
    {
        foreach (var item in inventory.GetAllItems())
        {
            if (PrefabOf(item) == prefab)
            {
                return item;
            }
        }
        return null;
    }

    // What another game would build from the chest's data: the game's own reader on the bytes in the chest's ZDO.
    private static ItemDrop.ItemData FromChestData(Container chest, string prefab)
    {
        var view = chest.GetComponent<ZNetView>();
        var bytes = view != null && view.IsValid() ? view.GetZDO().GetByteArray(ZDOVars.s_items) : null;
        if (bytes == null)
        {
            return null;
        }
        var read = new Inventory("selftest", null, chest.GetInventory().GetWidth(), chest.GetInventory().GetHeight());
        read.Load(new ZPackage(bytes));
        return FindIn(read, prefab);
    }

    // Game's own repair (the repair button of a station call InventoryGui.RepairOneItem, one worn item per call). No
    // station here: the no-cost flag stand in for it (CanRepair then say yes). Other worn items of the player get
    // their wear and their mark back after.
    private static bool Repair(Rig rig, ItemDrop.ItemData item)
    {
        var gui = InventoryGui.instance;
        if (gui == null)
        {
            return false;
        }
        var p = rig.P;
        var others = new Dictionary<ItemDrop.ItemData, KeyValuePair<float, string>>();
        foreach (var other in rig.Inv.GetAllItems())
        {
            if (!ReferenceEquals(other, item))
            {
                others[other] = new KeyValuePair<float, string>(other.m_durability, MarkOf(other));
            }
        }
        var noCost = p.m_noPlacementCost;
        try
        {
            p.m_noPlacementCost = true;
            for (var i = 0; i < 80 && item.m_durability < item.GetMaxDurability(); i++)
            {
                gui.RepairOneItem();
            }
        }
        finally
        {
            p.m_noPlacementCost = noCost;
            foreach (var pair in others)
            {
                pair.Key.m_durability = pair.Value.Key;
                if (pair.Value.Value != null && pair.Key.m_customData != null)
                {
                    pair.Key.m_customData[LoadedState.Key] = pair.Value.Value;
                }
                else
                {
                    LoadedState.Clear(pair.Key);
                }
            }
        }
        return item.m_durability >= item.GetMaxDurability();
    }

    // ---------- [reload weapon] log lines ----------

    private static DumpWatch _dump;

    private static void InstallDumpWatch()
    {
        if (_dump != null)
        {
            return;
        }
        _dump = new DumpWatch();
        BepInEx.Logging.Logger.Listeners.Add(_dump);
    }

    // Lines of Patches/DebugReloadDump.cs as they go to the log (written once, at the first spawn of the game
    // process). Kept from the game start. Any thread may log.
    private sealed class DumpWatch : ILogListener
    {
        private readonly object _gate = new object();
        private readonly List<string> _lines = new List<string>();

        internal List<string> Lines()
        {
            lock (_gate)
            {
                return new List<string>(_lines);
            }
        }

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            try
            {
                if (eventArgs == null || eventArgs.Source == null || eventArgs.Source.SourceName != ModInfo.Name)
                {
                    return;
                }
                var text = eventArgs.Data as string ?? eventArgs.Data?.ToString();
                if (text == null || !text.StartsWith(DumpMark, StringComparison.Ordinal))
                {
                    return;
                }
                lock (_gate)
                {
                    _lines.Add(text);
                }
            }
            catch (Exception)
            {
                // Listener must never throw into the logger.
            }
        }

        public void Dispose()
        {
        }
    }
#endif
}
