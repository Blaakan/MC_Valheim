#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;
#endif

namespace MC.UX.ContainerSortMod;

// Debug build only (calls vanish in Release). In-world self tests, run by the world probe (tools/Test-InWorld.ps1
// -Mod Container.Sort) and, for the sortchest.mp.* ones, by tools/Test-Multiplayer.ps1 (scenario modded: the dedicated
// server never load this mod, it is a Client mod).
//   sortchest.name         by name: order, empty slots last, one move sound, saved, same after reopen; second click
//                          change nothing, save nothing, no sound
//   sortchest.type         by type: weapons (axe before club), food, materials, trophy; tools, food, mead, other
//   sortchest.biome        by biome: 12 items Meadows..Deep North; biome index built once, its log lines, its time
//   sortchest.criterion    sort order button: cycle, label and tooltip follow, never sort, never touch the chest
//   sortchest.merge        partial stacks merge (weight same) or not (setting off); different items never merge
//   sortchest.drag         Sort cancel a drag from the chest and from the player inventory, nothing lost
//   sortchest.kinds        reinforced / personal chest, barrel, cart, karve, longship: buttons shown, no overlap, sort
//   sortchest.tombstone    tombstone panel: no buttons, sort refused; next chest: buttons back
//   sortchest.obliterator  sort inside, then the lever still destroy the items
//   sortchest.gamepad      View/Select sort only with the chest grid focused, Sort glyph, left stick moves never
//                          sort, game's own pad buttons still fire
//   sortchest.layout       tooltips; chest, karve, black metal chest, drakkar and 2 more inventory rows: no overlap
//   sortchest.toggle       feature off with a chest open: buttons gone, chest untouched; on: back, sort work
//   sortchest.owner        chest in use for others; chest no longer ours: click refused; after close others get it
//   sortchest.owner-panel  chest no longer ours: panel and buttons hide (game rule); panel kept open by MultiUserChest: Sort do nothing
//   sortchest.stale        old local copy: Sort refused with the center message, saved data safe; reopen fix it
//   sortchest.pad-late     panel come late (owner change): controller parts still on the chest grid group
//   sortchest.pad-late-slow  same with a very late panel (longer than the mod wait)
//   sortchest.crossbow     loaded-crossbow stamp of Crossbow Stays Loaded survive the sort, on the same item
//   sortchest.split        split dialog open: Sort do nothing; after cancel it work
//   sortchest.pointer      a real mouse ray on each button land on it; click through the ray work
//   sortchest.search-field Crafting Search and Sort field has the keyboard: our controller keys do nothing
//   sortchest.log          no error line that name this mod since game start
// Known wrong today, each alone so the tests above stay green (they fail until the mod or its TESTING.md is fixed):
//   sortchest.bug.noop-stale      Sort after a Sort that moved nothing is refused as "changed elsewhere"
//   sortchest.bug.tankard-group   Tankard sort with Tools and light (torch animation), not last in Other
//   sortchest.bug.pad-order-key   left stick click is the game's Take all key: sort order button has no controller key
//   sortchest.bug.pad-right-stick right stick click is the game's Place stacks key: it does something to the chest
//   sortchest.bug.layout-warning  buttons sit right of the panel: mod's own check warn "Sort buttons stick out"
//   sortchest.mp.server-copy  dedicated server keep and give back the sorted, merged content
//   sortchest.mp.settings  sort order button write the real config file; file edit and setting change move the label
//   sortchest.mp.toggle    Enabled off / on through the config file with a chest open, and with inventory closed
// Me force settings only with Plugin.TestSortBy / TestMergeStacks (never config) in single player; only the
// sortchest.mp.* tests touch config (throwaway files there). Every test destroy what it spawn and close the panel.
internal static class SelfTests
{
    [System.Diagnostics.Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        InstallWatch();
        foreach (var t in SinglePlayer())
        {
            SelfTest.Register(t.Key, t.Value);
        }
        foreach (var t in Multiplayer())
        {
            SelfTest.RegisterMultiplayer(t.Key, SelfTest.Modded, t.Value);
        }
#endif
    }

    [System.Diagnostics.Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        foreach (var t in SinglePlayer())
        {
            SelfTest.Unregister(t.Key);
        }
        foreach (var t in Multiplayer())
        {
            SelfTest.UnregisterMultiplayer(t.Key);
        }
        ClearOverrides();
#endif
    }

#if DEBUG
    private const string NameTest = "sortchest.name";
    private const string TypeTest = "sortchest.type";
    private const string BiomeTest = "sortchest.biome";
    private const string CriterionTest = "sortchest.criterion";
    private const string MergeTest = "sortchest.merge";
    private const string DragTest = "sortchest.drag";
    private const string KindsTest = "sortchest.kinds";
    private const string TombstoneTest = "sortchest.tombstone";
    private const string ObliteratorTest = "sortchest.obliterator";
    private const string GamepadTest = "sortchest.gamepad";
    private const string LayoutTest = "sortchest.layout";
    private const string ToggleTest = "sortchest.toggle";
    private const string OwnerTest = "sortchest.owner";
    private const string StaleTest = "sortchest.stale";
    private const string PadLateTest = "sortchest.pad-late";
    private const string PadLateSlowTest = "sortchest.pad-late-slow";
    private const string CrossbowTest = "sortchest.crossbow";
    private const string SplitTest = "sortchest.split";
    private const string OwnerPanelTest = "sortchest.owner-panel";
    private const string PointerTest = "sortchest.pointer";
    private const string SearchFieldTest = "sortchest.search-field";
    private const string LogTest = "sortchest.log";
    private const string NoopStaleTest = "sortchest.bug.noop-stale";
    private const string BugTankardTest = "sortchest.bug.tankard-group";
    private const string BugPadOrderKeyTest = "sortchest.bug.pad-order-key";
    private const string BugPadRightStickTest = "sortchest.bug.pad-right-stick";
    private const string BugLayoutTest = "sortchest.bug.layout-warning";
    private const string MpServerCopyTest = "sortchest.mp.server-copy";
    private const string MpSettingsTest = "sortchest.mp.settings";
    private const string MpToggleTest = "sortchest.mp.toggle";

    private const string Chest = "piece_chest_wood";
    private const string SortTopic = "Sort";
    private const string SortTipStart = "Rearrange this container from top left to bottom right, ";
    private const string CriterionTopic = "Sort order";
    private const string CriterionTip = "Click to change how Sort orders items: by name, by type (weapons, armour, food, "
                                        + "materials...) or by biome (Meadows to Deep North).";

    // T01 / T02 items, and their order by English name (TESTING.md T01).
    private static readonly string[] BasicItems = { "Wood", "Stone", "Resin", "Flint", "AxeFlint", "Club", "Raspberry", "TrophyBoar" };
    private static readonly string[] BasicByEnglishName = { "TrophyBoar", "Club", "Flint", "AxeFlint", "Raspberry", "Resin", "Stone", "Wood" };

    private static readonly CompareInfo NameCompare = CultureInfo.InvariantCulture.CompareInfo;

    private static LogWatch _watch;

    // Log test last: it look at every line the other tests made.
    private static List<KeyValuePair<string, Func<IEnumerator>>> SinglePlayer() => new List<KeyValuePair<string, Func<IEnumerator>>>
    {
        Test(NameTest, RunName),
        Test(TypeTest, RunType),
        Test(BiomeTest, RunBiome),
        Test(CriterionTest, RunCriterion),
        Test(MergeTest, RunMerge),
        Test(DragTest, RunDrag),
        Test(KindsTest, RunKinds),
        Test(TombstoneTest, RunTombstone),
        Test(ObliteratorTest, RunObliterator),
        Test(GamepadTest, RunGamepad),
        Test(LayoutTest, RunLayout),
        Test(ToggleTest, RunToggle),
        Test(OwnerTest, RunOwner),
        Test(OwnerPanelTest, RunOwnerPanel),
        Test(StaleTest, RunStale),
        Test(PadLateTest, () => RunPadLate(PadLateTest, 0.7f, true)),
        Test(PadLateSlowTest, () => RunPadLate(PadLateSlowTest, 2.8f, false)),
        Test(CrossbowTest, RunCrossbow),
        Test(SplitTest, RunSplit),
        Test(PointerTest, RunPointer),
        Test(SearchFieldTest, RunSearchField),
        Test(NoopStaleTest, RunNoopStale),
        Test(BugTankardTest, RunBugTankard),
        Test(BugPadOrderKeyTest, RunBugPadOrderKey),
        Test(BugPadRightStickTest, RunBugPadRightStick),
        Test(BugLayoutTest, RunBugLayoutWarning), // after every other opening: it count the warnings since game start
        Test(LogTest, RunLog),
    };

    private static List<KeyValuePair<string, Func<IEnumerator>>> Multiplayer() => new List<KeyValuePair<string, Func<IEnumerator>>>
    {
        Test(MpServerCopyTest, RunMpServerCopy),
        Test(MpSettingsTest, RunMpSettings),
        Test(MpToggleTest, RunMpToggle),
    };

    private static KeyValuePair<string, Func<IEnumerator>> Test(string name, Func<IEnumerator> run) =>
        new KeyValuePair<string, Func<IEnumerator>>(name, run);

    private static void ClearOverrides()
    {
        Plugin.TestSortBy = null;
        Plugin.TestMergeStacks = null;
    }

    // ================================================================ helpers

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

    // What one Sort click (mouse or controller key) did.
    private sealed class Clicked
    {
        internal bool Sent;
        internal string Why = "";
        internal int Ran;
        internal ContainerSorter.SortOutcome Outcome;
        internal bool Written;
        internal int Moved;
        internal int Merged;
        internal uint RevBefore;
        internal uint RevAfter;
        internal int Sounds;
        internal int SoundKinds;

        internal bool Sorted => Outcome == ContainerSorter.SortOutcome.Done && Written;

        internal bool Saved => RevAfter != RevBefore;

        public override string ToString() =>
            Sent ? $"{Outcome}, written {Written}, {Moved} moved, {Merged} merged, saved {Saved}, {Sounds} move sound(s)" : $"click not sent ({Why})";
    }

    // Me keep log lines from game start: every line of this mod, and the problem lines T17 name. Any thread may log.
    private sealed class LogWatch : ILogListener
    {
        private readonly object _gate = new object();
        private readonly List<KeyValuePair<LogLevel, string>> _mine = new List<KeyValuePair<LogLevel, string>>();
        private readonly List<string> _errors = new List<string>();
        private readonly List<string> _layoutWarnings = new List<string>();

        internal int MineCount
        {
            get
            {
                lock (_gate)
                {
                    return _mine.Count;
                }
            }
        }

        internal List<KeyValuePair<LogLevel, string>> MineSince(int index)
        {
            lock (_gate)
            {
                return _mine.Skip(Math.Max(0, index)).ToList();
            }
        }

        // Error lines that come from this mod or name it (T17 first half).
        internal List<string> Errors()
        {
            lock (_gate)
            {
                return _errors.ToList();
            }
        }

        // The mod's own "Sort buttons ..." layout warnings (T13, T17 second half).
        internal List<string> LayoutWarnings()
        {
            lock (_gate)
            {
                return _layoutWarnings.ToList();
            }
        }

        public void LogEvent(object sender, LogEventArgs e)
        {
            try
            {
                if (e == null)
                {
                    return;
                }
                var text = e.Data as string ?? e.Data?.ToString();
                if (text == null || text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal))
                {
                    return;
                }
                var source = e.Source != null ? e.Source.SourceName : "";
                var mine = source == ModInfo.Name;
                var error = (e.Level & (LogLevel.Error | LogLevel.Fatal)) != 0;
                var named = error && (mine || text.IndexOf(ModInfo.Name, StringComparison.Ordinal) >= 0
                                           || text.IndexOf("MC.UX", StringComparison.Ordinal) >= 0);
                var layout = !error && mine && (e.Level & LogLevel.Warning) != 0 && text.IndexOf("Sort buttons", StringComparison.Ordinal) >= 0;
                lock (_gate)
                {
                    if (mine)
                    {
                        _mine.Add(new KeyValuePair<LogLevel, string>(e.Level, text));
                    }
                    if (named || layout)
                    {
                        var cut = text.IndexOf('\n');
                        (named ? _errors : _layoutWarnings).Add($"[{e.Level}: {source}] {(cut > 0 ? text.Substring(0, cut).TrimEnd() : text)}");
                    }
                }
            }
            catch
            {
                // Me never throw inside the logger.
            }
        }

        public void Dispose()
        {
        }
    }

    // Once per game run, never removed: toggles must not lose lines.
    private static void InstallWatch()
    {
        if (_watch != null)
        {
            return;
        }
        _watch = new LogWatch();
        BepInEx.Logging.Logger.Listeners.Add(_watch);
    }

    private static List<string> Warnings(int mineIndex, string containing)
    {
        var found = new List<string>();
        if (_watch == null)
        {
            return found;
        }
        foreach (var line in _watch.MineSince(mineIndex))
        {
            if ((line.Key & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) != 0
                && line.Value.IndexOf(containing, StringComparison.Ordinal) >= 0)
            {
                found.Add(line.Value);
            }
        }
        return found;
    }

    private enum Place
    {
        Near,    // on the ground, 3 m to the right
        Away,    // on the ground, 9 m ahead (obliterator lightning)
        Vehicle, // in the air 18 m ahead, frozen (cart, ships: no rolling, no push on the player)
    }

    // Me hold what a test change, put it back in Done (finally, no yield).
    private sealed class Rig
    {
        internal readonly Player P;
        internal readonly InventoryGui Gui;
        private readonly Vector3 _fwd;
        private readonly Vector3 _right;
        private readonly float _autoClose;
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<Action> _undo = new List<Action>();
        private int _near;

        internal Rig()
        {
            P = Player.m_localPlayer;
            Gui = InventoryGui.instance;
            _fwd = Flat(P.transform.forward, Vector3.forward);
            _right = Flat(P.transform.right, Vector3.right);
            // Test containers stand far (ships): the panel must not close by distance.
            _autoClose = Gui.m_autoCloseDistance;
            Gui.m_autoCloseDistance = 100000f;
            if (Gui.m_animator.GetBool("visible"))
            {
                Gui.Hide();
            }
            ClearOverrides();
            ContainerSorter.TestReset();
        }

        private static Vector3 Flat(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-4f ? v.normalized : fallback;
        }

        internal void Undo(Action action) => _undo.Add(action);

        internal GameObject SpawnObject(GameObject prefab, Place place)
        {
            if (prefab == null)
            {
                return null;
            }
            var pos = P.transform.position;
            var rot = Quaternion.identity;
            switch (place)
            {
                case Place.Vehicle:
                    pos += _fwd * 18f;
                    pos.y = ZoneSystem.instance.GetGroundHeight(pos) + 4f;
                    rot = Quaternion.LookRotation(_right);
                    break;
                case Place.Away:
                    pos += _fwd * 9f;
                    pos.y = ZoneSystem.instance.GetGroundHeight(pos);
                    break;
                default:
                    pos += _right * 3f + _fwd * (1.5f * _near++);
                    pos.y = ZoneSystem.instance.GetGroundHeight(pos);
                    break;
            }
            var go = Object.Instantiate(prefab, pos, rot);
            _spawned.Add(go);
            // Support off: never break and spill.
            foreach (var wnt in go.GetComponentsInChildren<WearNTear>(true))
            {
                wnt.enabled = false;
            }
            if (place == Place.Vehicle)
            {
                foreach (var ship in go.GetComponentsInChildren<Ship>(true))
                {
                    ship.enabled = false;
                }
                foreach (var body in go.GetComponentsInChildren<Rigidbody>(true))
                {
                    body.isKinematic = true;
                }
            }
            return go;
        }

        internal Container Spawn(string prefab, Place place = Place.Near)
        {
            var go = SpawnObject(ZNetScene.instance.GetPrefab(prefab), place);
            return go != null ? go.GetComponentInChildren<Container>(true) : null;
        }

        // Destroy now (root object of the container).
        internal void Destroy(Container container)
        {
            if (container == null)
            {
                return;
            }
            var root = container.transform.root.gameObject;
            _spawned.Remove(root);
            Kill(root);
        }

        private static void Kill(GameObject go)
        {
            if (go == null)
            {
                return;
            }
            var nview = go.GetComponent<ZNetView>();
            var zdo = nview != null ? nview.GetZDO() : null;
            if (zdo != null && !zdo.IsOwner())
            {
                zdo.SetOwner(ZDOMan.GetSessionID()); // only the owner destroy the network object
            }
            ZNetScene.instance.Destroy(go);
        }

        // Two more (or other) inventory rows like console "inventorysize", but nothing saved on the character.
        internal void SetRows(int rows)
        {
            var inventory = P.GetInventory();
            var before = inventory.GetHeight();
            if (before == rows)
            {
                return;
            }
            inventory.SetHeight(rows);
            Gui.SetInventorySize(rows);
            Undo(() =>
            {
                inventory.SetHeight(before);
                Gui.SetInventorySize(before);
            });
        }

        // Controller is the input device until Done (glyphs show only then). Keyboard and mouse ignored meanwhile.
        internal void ForceGamepad()
        {
            var mode = ZInput.s_inputSwitchingMode;
            var source = ZInput.m_inputSource;
            ZInput.SetInputSwitchingMode(ZInput.InputSource.GamepadOnly);
            Undo(() =>
            {
                if (source == ZInput.InputSource.KeyboardMouse)
                {
                    ZInput.SetInputSwitchingMode(ZInput.InputSource.KeyboardMouseOnly);
                }
                ZInput.SetInputSwitchingMode(mode);
            });
        }

        internal void Done()
        {
            Try(ClearOverrides);
            Try(() =>
            {
                UITooltip.HideTooltip();
                if (Gui != null)
                {
                    Gui.Hide();
                }
            });
            for (var i = _undo.Count - 1; i >= 0; i--)
            {
                Try(_undo[i]);
            }
            _undo.Clear();
            Try(() =>
            {
                if (Gui != null)
                {
                    Gui.m_autoCloseDistance = _autoClose;
                }
            });
            foreach (var go in _spawned)
            {
                Try(() => Kill(go));
            }
            _spawned.Clear();
        }

        private static void Try(Action step)
        {
            try
            {
                step();
            }
            catch (Exception e)
            {
                Log.Warning($"Self-test clean-up step failed: {e}");
            }
        }
    }

    private static bool Ready(string name)
    {
        if (Player.m_localPlayer == null || InventoryGui.instance == null || ZNetScene.instance == null || ObjectDB.instance == null
            || ZoneSystem.instance == null || Localization.instance == null || ZDOMan.instance == null)
        {
            SelfTest.Fail(name, "no local player, inventory screen or world data");
            return false;
        }
        return true;
    }

    private static Plugin Self() =>
        Chainloader.PluginInfos.TryGetValue(ModInfo.Guid, out var info) ? info.Instance as Plugin : null;

    private static bool English =>
        string.Equals(Localization.instance.GetSelectedLanguage(), "English", StringComparison.OrdinalIgnoreCase);

    private static ItemDrop.ItemData.SharedData Shared(string prefab)
    {
        var go = ObjectDB.instance.GetItemPrefab(prefab);
        var drop = go != null ? go.GetComponent<ItemDrop>() : null;
        return drop != null && drop.m_itemData != null ? drop.m_itemData.m_shared : null;
    }

    private static string LocalName(string prefab)
    {
        var shared = Shared(prefab);
        return shared != null ? Localization.instance.Localize(shared.m_name) : prefab;
    }

    // Prefabs in the order of their names in the game language (the "By name" rule, done again here).
    private static string[] ByName(params string[] prefabs)
    {
        var list = prefabs.ToList();
        list.Sort((a, b) =>
        {
            var c = NameCompare.Compare(LocalName(a), LocalName(b), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
            return c != 0 ? c : string.CompareOrdinal(a, b);
        });
        return list.ToArray();
    }

    private static string TypeRank(string prefab)
    {
        var shared = Shared(prefab);
        if (shared == null)
        {
            return "no such item";
        }
        TypeGroups.Rank(shared, out var type, out var sub);
        return type + "." + sub;
    }

    // Real item like the game make it (own prefab instance), never in a player inventory. Null = no such item.
    private static ItemDrop.ItemData Make(string prefab, int stack = 1)
    {
        var shared = Shared(prefab);
        if (shared == null)
        {
            return null;
        }
        var scratch = new Inventory("MC_SortChest_SelfTest", null, 1, 1);
        var amount = Mathf.Clamp(stack, 1, Mathf.Max(1, shared.m_maxStackSize));
        return scratch.AddItem(prefab, amount, 1, 0, 0L, "", new Vector2i(0, 0), false, true, false);
    }

    private static int Cap(Inventory inventory) => inventory.GetWidth() * inventory.GetHeight();

    // Replace the content: each item at its slot (reading order index), list order = given order, one save.
    private static List<ItemDrop.ItemData> Fill(Checks c, Container container, params (int Slot, string Prefab, int Stack)[] specs)
    {
        var inventory = container.GetInventory();
        var width = inventory.GetWidth();
        var cap = Cap(inventory);
        var list = inventory.GetAllItems();
        list.Clear();
        var made = new List<ItemDrop.ItemData>();
        foreach (var spec in specs)
        {
            var item = spec.Slot >= 0 && spec.Slot < cap ? Make(spec.Prefab, spec.Stack) : null;
            if (item == null || item.m_stack != spec.Stack)
            {
                c.Check(false, $"setup: could not make {spec.Prefab} x{spec.Stack} for slot {spec.Slot} of a {width}x{inventory.GetHeight()} container"
                               + (item != null ? $" (stack {item.m_stack})" : ""));
                made.Add(null);
                continue;
            }
            item.m_gridPos = new Vector2i(spec.Slot % width, spec.Slot / width);
            list.Add(item);
            made.Add(item);
        }
        inventory.Changed();
        return made;
    }

    // Slots with gaps, from both ends of the grid: last, first, last - 1, second...
    private static (int Slot, string Prefab, int Stack)[] Scatter(int cap, params (string Prefab, int Stack)[] items)
    {
        var specs = new (int Slot, string Prefab, int Stack)[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var slot = i % 2 == 0 ? cap - 1 - i / 2 : i / 2;
            specs[i] = (slot, items[i].Prefab, items[i].Stack);
        }
        return specs;
    }

    private static string Prefab(ItemDrop.ItemData item) =>
        item.m_dropPrefab != null ? item.m_dropPrefab.name : "?" + (item.m_shared != null ? item.m_shared.m_name : "");

    private static string Cell(string prefab, int stack) => stack != 1 ? prefab + "x" + stack : prefab;

    // What each slot hold, in reading order: "Wood", "Woodx20", "" for empty.
    private static string[] Slots(Inventory inventory)
    {
        var width = inventory.GetWidth();
        var slots = new string[Cap(inventory)];
        for (var i = 0; i < slots.Length; i++)
        {
            slots[i] = "";
        }
        var outside = "";
        foreach (var item in inventory.GetAllItems())
        {
            var index = item.m_gridPos.y * width + item.m_gridPos.x;
            var cell = Cell(Prefab(item), item.m_stack);
            if (item.m_gridPos.x < 0 || item.m_gridPos.x >= width || index < 0 || index >= slots.Length)
            {
                outside += "!" + cell;
            }
            else
            {
                slots[index] = slots[index].Length == 0 ? cell : slots[index] + "+" + cell;
            }
        }
        if (outside.Length > 0 && slots.Length > 0)
        {
            slots[slots.Length - 1] += outside;
        }
        return slots;
    }

    private static string[] Expect(Inventory inventory, IEnumerable<string> cells)
    {
        var slots = new string[Cap(inventory)];
        var i = 0;
        foreach (var cell in cells)
        {
            if (i < slots.Length)
            {
                slots[i] = cell;
            }
            i++;
        }
        for (var k = 0; k < slots.Length; k++)
        {
            slots[k] = slots[k] ?? "";
        }
        return slots;
    }

    private static bool Same(string[] a, string[] b) => a != null && b != null && a.SequenceEqual(b);

    private static string Show(string[] slots) => slots == null ? "none" : "[" + string.Join("|", slots).TrimEnd('|') + "]";

    private static string Totals(Inventory inventory)
    {
        var totals = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in inventory.GetAllItems())
        {
            var prefab = Prefab(item);
            totals[prefab] = (totals.TryGetValue(prefab, out var n) ? n : 0) + item.m_stack;
        }
        return string.Join(",", totals.Select(kv => kv.Key + "=" + kv.Value));
    }

    private static ZDO Zdo(Container container) => container.m_nview.GetZDO();

    private static uint Rev(Container container) => Zdo(container).DataRevision;

    private static byte[] StoredBytes(Container container) => Zdo(container).GetByteArray(ZDOVars.s_items);

    // Saved bytes read back the way the game (also one without the mod) read them.
    private static Inventory Decode(Container container, byte[] bytes)
    {
        if (bytes == null)
        {
            return null;
        }
        var inventory = container.GetInventory();
        var scratch = new Inventory("MC_SortChest_SelfTest", null, inventory.GetWidth(), inventory.GetHeight());
        scratch.Load(new ZPackage(bytes));
        return scratch;
    }

    private static bool SameBytes(byte[] a, byte[] b) => a != null && b != null && a.SequenceEqual(b);

    private static byte[] Bytes(Inventory inventory)
    {
        var pkg = new ZPackage();
        inventory.Save(pkg);
        return pkg.GetArray();
    }

    // Swap the first two slots (both filled) and save: sorted chest is unsorted again, list order kept.
    private static bool Unsort(Inventory inventory)
    {
        var a = inventory.GetItemAt(0, 0);
        var b = inventory.GetWidth() > 1 ? inventory.GetItemAt(1, 0) : inventory.GetItemAt(0, 1);
        if (a == null || b == null)
        {
            return false;
        }
        var pos = a.m_gridPos;
        a.m_gridPos = b.m_gridPos;
        b.m_gridPos = pos;
        inventory.Changed();
        return true;
    }

    private static int MoveSoundKinds(InventoryGui gui) =>
        gui.m_moveItemEffects.m_effectPrefabs.Count(e => e != null && e.m_enabled && e.m_prefab != null);

    // Live "item moved" sound objects (the effect the game and the mod make when items move).
    private static HashSet<int> MoveSounds(InventoryGui gui)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var effect in gui.m_moveItemEffects.m_effectPrefabs)
        {
            if (effect != null && effect.m_enabled && effect.m_prefab != null)
            {
                names.Add(effect.m_prefab.name + "(Clone)");
            }
        }
        var ids = new HashSet<int>();
        if (names.Count == 0)
        {
            return ids;
        }
        foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (go != null && names.Contains(go.name))
            {
                ids.Add(go.GetInstanceID());
            }
        }
        return ids;
    }

    // Mouse click the way the event system deliver it (button must be shown and usable).
    private static bool Press(SortChestUi.TestView view, out string why)
    {
        why = "";
        if (view == null || view.Go == null || view.Button == null)
        {
            why = "button missing";
            return false;
        }
        if (!view.Go.activeInHierarchy || !view.Button.IsActive())
        {
            why = "button not shown";
            return false;
        }
        if (!view.Button.IsInteractable())
        {
            why = "button not interactable";
            return false;
        }
        ExecuteEvents.Execute(view.Go, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
        return true;
    }

    private static void Before(Clicked r, Rig rig, Container container, out int clicks, out HashSet<int> sounds)
    {
        r.Sent = false;
        r.Why = "";
        r.SoundKinds = MoveSoundKinds(rig.Gui);
        sounds = MoveSounds(rig.Gui);
        r.RevBefore = Rev(container);
        clicks = ContainerSorter.Clicks;
        ContainerSorter.TestReset();
    }

    private static void After(Clicked r, Rig rig, Container container, int clicks, HashSet<int> sounds)
    {
        r.Ran = ContainerSorter.Clicks - clicks;
        r.Outcome = ContainerSorter.LastOutcome;
        r.Written = ContainerSorter.LastWritten;
        r.Moved = ContainerSorter.LastMoved;
        r.Merged = ContainerSorter.LastMerged;
        r.RevAfter = Rev(container);
        r.Sounds = MoveSounds(rig.Gui).Count(id => !sounds.Contains(id));
    }

    // One mouse click on Sort, and what it did (same frame).
    private static Clicked ClickSort(Rig rig, Container container)
    {
        var r = new Clicked();
        Before(r, rig, container, out var clicks, out var sounds);
        r.Sent = Press(SortChestUi.TestSort, out r.Why);
        After(r, rig, container, clicks, sounds);
        return r;
    }

    // Controller button pressed and let go (ZInput state, like a real press), and what Sort did meanwhile.
    private static IEnumerator PressKey(Rig rig, Container container, string button, Clicked r)
    {
        Before(r, rig, container, out var clicks, out var sounds);
        var def = ZInput.instance != null ? ZInput.instance.GetButtonDef(button) : null;
        if (def == null)
        {
            r.Why = $"the game has no button '{button}'";
            yield break;
        }
        def.Press();
        try
        {
            yield return null;
            yield return null;
            yield return null;
        }
        finally
        {
            // Test stopped in the middle of a press (timeout): the button must never stay held for the next tests.
            def.Release();
        }
        yield return null;
        yield return null;
        r.Sent = true;
        After(r, rig, container, clicks, sounds);
    }

    private static void SetSortBy(SortCriterion criterion)
    {
        Plugin.TestSortBy = criterion;
        SortChestUi.RefreshTexts();
    }

    // Open like the player (E on the container: request, answer, panel), then wait for the panel.
    private static IEnumerator Open(Rig rig, Container container, Box box)
    {
        box.Ok = false;
        box.Detail = "";
        var gui = rig.Gui;
        if (container == null || container.GetInventory() == null)
        {
            box.Detail = "no container";
            yield break;
        }
        if (gui.m_animator.GetBool("visible"))
        {
            gui.Hide();
            yield return null;
            yield return null;
        }
        if (container.m_privacy == Container.PrivacySetting.Public)
        {
            container.Interact(rig.P, false, false);
        }
        if (!ReferenceEquals(gui.m_currentContainer, container))
        {
            // Personal chest nobody built, or a ward: the screen is opened the way the game's answer opens it.
            gui.Show(container);
        }
        var end = Time.realtimeSinceStartup + 5f;
        while (Time.realtimeSinceStartup < end
               && !(ReferenceEquals(gui.m_currentContainer, container) && gui.m_container.gameObject.activeInHierarchy))
        {
            yield return null;
        }
        box.Ok = ReferenceEquals(gui.m_currentContainer, container) && gui.m_container.gameObject.activeInHierarchy;
        if (!box.Ok)
        {
            box.Detail = $"current container set {ReferenceEquals(gui.m_currentContainer, container)}, panel shown "
                         + $"{gui.m_container.gameObject.activeInHierarchy}, owner {container.IsOwner()}";
        }
        yield return null;
        yield return null;
    }

    private static IEnumerator Close(Rig rig)
    {
        rig.Gui.Hide();
        yield return null;
        yield return null;
    }

    private static IEnumerator Focus(Rig rig, UIGroupHandler group)
    {
        rig.Gui.SetActiveGroup(group, false);
        yield return null;
        yield return null;
        yield return null;
    }

    private static UIGamePad PadOf(Button button) => button != null ? button.GetComponent<UIGamePad>() : null;

    // Game buttons of the container panel that listen to this controller key right now: a press would run them
    // (left stick click = the game's Take all: it put the whole chest in the test character). Me never press then.
    private static List<string> GameButtonsOn(InventoryGui gui, string key)
    {
        var found = new List<string>();
        foreach (var (what, button) in new (string, Button)[] { ("Take all", gui.m_takeAllButton), ("Place stacks", gui.m_stackAllButton) })
        {
            var pad = PadOf(button);
            if (pad != null && pad.isActiveAndEnabled && pad.m_zinputKey == key && pad.IsInteractive())
            {
                found.Add(what);
            }
        }
        return found;
    }

    private static string GameKeys(InventoryGui gui)
    {
        var take = PadOf(gui.m_takeAllButton);
        var stack = PadOf(gui.m_stackAllButton);
        return $"Take all '{(take != null ? take.m_zinputKey ?? "none" : "no controller part")}' (keyboard {(take != null ? take.m_keyCode.ToString() : "-")}), "
               + $"Place stacks '{(stack != null ? stack.m_zinputKey ?? "none" : "no controller part")}' (keyboard {(stack != null ? stack.m_keyCode.ToString() : "-")})";
    }

    // Game button's own work swapped for a counter until Done: the press still go the whole way (key, controller
    // part, button), but no item move. Counter[0] = times the button fired.
    private static int[] CountInstead(Rig rig, Button button, UnityAction work)
    {
        var fired = new int[1];
        UnityAction count = () => fired[0]++;
        button.onClick.RemoveListener(work);
        button.onClick.AddListener(count);
        rig.Undo(() =>
        {
            button.onClick.RemoveListener(count);
            button.onClick.RemoveListener(work);
            button.onClick.AddListener(work);
        });
        return fired;
    }

    // Mod that keep the container panel open on a chest this game does not own (game alone hide it). Null = none seen.
    private static string PanelKeeper()
    {
        foreach (var info in Chainloader.PluginInfos)
        {
            var name = info.Value != null && info.Value.Metadata != null ? info.Value.Metadata.Name ?? "" : "";
            if (info.Key.IndexOf("MultiUserChest", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("MultiUserChest", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return name.Length > 0 ? name : info.Key;
            }
        }
        return null;
    }

    // Other mods (not me) with a Harmony patch on one game method. Empty = none.
    private static List<string> PatchOwners(System.Reflection.MethodBase method)
    {
        var owners = new List<string>();
        var info = method != null ? Harmony.GetPatchInfo(method) : null;
        if (info == null)
        {
            return owners;
        }
        foreach (var owner in info.Owners)
        {
            if (owner != ModInfo.Guid && !owners.Contains(owner))
            {
                owners.Add(owner);
            }
        }
        return owners;
    }

    // Other mods that patch the game method which shows / hides the container panel every frame.
    private static List<string> PanelUpdatePatchers() =>
        PatchOwners(AccessTools.Method(typeof(InventoryGui), nameof(InventoryGui.UpdateContainer)));

    // ---------------------------------------------------------------- who moved the container panel (bug.layout-warning notes)

    private static string V(Vector2 v) => $"({v.x:0.#},{v.y:0.#})";

    private static string Placement(RectTransform rt) =>
        $"anchors {V(rt.anchorMin)}-{V(rt.anchorMax)}, pivot {V(rt.pivot)}, position {V(rt.anchoredPosition)}, size {V(rt.sizeDelta)}, "
        + $"scale {V(rt.localScale)}";

    private static bool Near(Vector2 a, Vector2 b) => Mathf.Abs(a.x - b.x) < 0.05f && Mathf.Abs(a.y - b.y) < 0.05f;

    private static bool SamePlacement(RectTransform a, RectTransform b) =>
        Near(a.anchorMin, b.anchorMin) && Near(a.anchorMax, b.anchorMax) && Near(a.pivot, b.pivot)
        && Near(a.anchoredPosition, b.anchoredPosition) && Near(a.sizeDelta, b.sizeDelta) && Near(a.localScale, b.localScale);

    private static string PathUnder(Transform t, Transform root)
    {
        var names = new List<string>();
        for (var p = t; p != null && !ReferenceEquals(p, root); p = p.parent)
        {
            names.Insert(0, p.name);
        }
        return string.Join("/", names.ToArray());
    }

    // The game's own copy of the inventory screen that the shown one was made from (asset, in no scene). Null = not
    // in memory (screen is part of a scene, or asset unloaded).
    private static InventoryGui GuiPrefab(InventoryGui live)
    {
        foreach (var candidate in Resources.FindObjectsOfTypeAll<InventoryGui>())
        {
            if (candidate != null && !ReferenceEquals(candidate, live) && !candidate.gameObject.scene.IsValid()
                && candidate.m_container != null && candidate.m_takeAllButton != null && candidate.m_stackAllButton != null)
            {
                return candidate;
            }
        }
        return null;
    }

    // Component types on this object that come from no game / Unity assembly: "Type (assembly)". Says which mod made it.
    private static string ForeignComponents(Transform t)
    {
        var found = new List<string>();
        foreach (var component in t.GetComponents<Component>())
        {
            if (component == null)
            {
                continue;
            }
            var assembly = component.GetType().Assembly.GetName().Name ?? "";
            if (assembly.StartsWith("assembly_", StringComparison.OrdinalIgnoreCase) || assembly.StartsWith("Unity", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            found.Add($"{component.GetType().Name} ({assembly})");
        }
        return found.Count > 0 ? string.Join(", ", found.ToArray()) : "game components only";
    }

    // Every other mod with a Harmony patch on the game's inventory screen class, with the patched methods.
    private static string ScreenPatchers()
    {
        var byOwner = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var method in Harmony.GetAllPatchedMethods())
        {
            if (method == null || method.DeclaringType != typeof(InventoryGui))
            {
                continue;
            }
            foreach (var owner in PatchOwners(method))
            {
                if (!byOwner.TryGetValue(owner, out var methods))
                {
                    byOwner[owner] = methods = new List<string>();
                }
                if (!methods.Contains(method.Name))
                {
                    methods.Add(method.Name);
                }
            }
        }
        return byOwner.Count == 0 ? "none" : string.Join("; ", byOwner.Select(kv => $"{kv.Key}: {string.Join(", ", kv.Value.ToArray())}").ToArray());
    }

    // NOTE lines for the user: where everything sits, and whether an installed mod changed the container panel.
    // Me only tell facts of this run. Panel must be open with the Sort buttons shown.
    private static void NoteLayoutFacts(Checks c, InventoryGui gui, SortChestUi.TestView sort, SortChestUi.TestView criterion, string label)
    {
        try
        {
            var take = gui.m_takeAllButton != null ? gui.m_takeAllButton.transform as RectTransform : null;
            var stack = gui.m_stackAllButton != null ? gui.m_stackAllButton.transform as RectTransform : null;
            var name = gui.m_containerName != null ? gui.m_containerName.transform as RectTransform : null;
            var grid = gui.ContainerGrid != null ? gui.ContainerGrid.transform as RectTransform : null;
            var panel = WorldRect(gui.m_container);
            var s = WorldRect(sort.Rect);
            var k = WorldRect(criterion.Rect);
            var canvas = gui.m_container.GetComponentInParent<Canvas>();
            var scale = canvas != null && canvas.rootCanvas != null ? canvas.rootCanvas.scaleFactor : 0f;
            var game = global::Version.GetVersionString();
            string R(RectTransform rt) => rt != null && rt.gameObject.activeInHierarchy ? Fmt(WorldRect(rt)) : "not shown";
            c.Note($"{label}, rectangles in screen pixels (x to the right, y up; screen {Screen.width}x{Screen.height}, GUI scale {scale:0.##}, game "
                   + $"{game}): container panel {Fmt(panel)}, Take all {R(take)}, container name {R(name)}, Place stacks {R(stack)}, "
                   + $"grid {R(grid)}, Sort {Fmt(s)}, sort order {Fmt(k)}");
            if (take != null && stack != null)
            {
                var t = WorldRect(take);
                var p = WorldRect(stack);
                c.Note($"{label}: Take all starts {t.xMin - panel.xMin:F0} px from the panel's left edge, Place stacks ends {panel.xMax - p.xMax:F0} px from "
                       + $"its right edge ({p.xMin - t.xMax:F0} px between the two); Sort starts {s.xMin - panel.xMax:F0} px and the sort order button ends "
                       + $"{k.xMax - panel.xMax:F0} px to the right of the panel's right edge");
            }

            // Did something move the game's own parts? The prefab the screen was made from still has the game's values.
            var prefab = GuiPrefab(gui);
            var changed = new List<string>();
            var same = new List<string>();
            var added = new List<string>();
            if (prefab != null)
            {
                var parts = new (string What, RectTransform Live, RectTransform Stock)[]
                {
                    ("container panel", gui.m_container, prefab.m_container),
                    ("Take all", take, prefab.m_takeAllButton.transform as RectTransform),
                    ("Place stacks", stack, prefab.m_stackAllButton.transform as RectTransform),
                    ("container name", name, prefab.m_containerName != null ? prefab.m_containerName.transform as RectTransform : null),
                };
                foreach (var part in parts)
                {
                    if (part.Live == null || part.Stock == null)
                    {
                        changed.Add($"{part.What}: {(part.Live == null ? "missing on the screen" : "missing in the prefab")}");
                        continue;
                    }
                    var livePath = PathUnder(part.Live, gui.transform);
                    var stockPath = PathUnder(part.Stock, prefab.transform);
                    if (SamePlacement(part.Live, part.Stock) && livePath == stockPath)
                    {
                        same.Add(part.What);
                    }
                    else
                    {
                        changed.Add($"{part.What}: now [{Placement(part.Live)}] at '{livePath}', in the game's prefab [{Placement(part.Stock)}] at '{stockPath}'");
                    }
                }
                // Objects put under the buttons' parent after the screen was made (other mods' buttons).
                var liveParent = stack != null ? stack.parent : null;
                var stockParent = prefab.m_stackAllButton.transform.parent;
                if (liveParent != null && stockParent != null)
                {
                    var stockNames = new HashSet<string>();
                    foreach (Transform child in stockParent)
                    {
                        stockNames.Add(child.name);
                    }
                    foreach (Transform child in liveParent)
                    {
                        if (!stockNames.Contains(child.name) && !child.name.StartsWith(SortChestUi.TestNamePrefix, StringComparison.Ordinal))
                        {
                            added.Add($"'{child.name}' ({ForeignComponents(child)}, {(child.gameObject.activeInHierarchy ? "shown" : "hidden")})");
                        }
                    }
                }
                c.Note($"{label}, compared with the game's own prefab of the inventory screen: same place and size as in the prefab: "
                       + $"{(same.Count > 0 ? string.Join(", ", same.ToArray()) : "none")}; changed: {(changed.Count > 0 ? string.Join(" | ", changed.ToArray()) : "none")}; "
                       + $"objects added next to Place stacks by something else than {ModInfo.Name}: {(added.Count > 0 ? string.Join(", ", added.ToArray()) : "none")}");
            }
            else
            {
                c.Note($"{label}: the game's own prefab of the inventory screen is not in memory, so this run cannot compare the panel parts with it; "
                       + $"now: container panel [{Placement(gui.m_container)}], Take all [{(take != null ? Placement(take) : "none")}], "
                       + $"Place stacks [{(stack != null ? Placement(stack) : "none")}]");
            }
            var patchers = ScreenPatchers();
            c.Note($"other mods with a Harmony patch on the game's inventory screen (InventoryGui), with the patched methods: {patchers}");
            if (prefab != null)
            {
                c.Note(changed.Count == 0
                    ? "so in this run no installed mod moved or resized the container panel, Take all, the name or Place stacks: they sit where the game's "
                      + $"own prefab puts them (game {game}), and the two buttons {ModInfo.Name} puts after Place stacks land outside "
                      + "the panel on the game's own layout"
                      + (added.Count > 0 ? " (objects that something else added next to Place stacks are listed above)" : "")
                      + ". A run cannot show whether an older game version placed Take all and Place stacks differently."
                    : "so in this run the container panel is NOT as the game's prefab has it (see 'changed' above): a mod, or game code at run time, moved "
                      + "or resized it; the mods patching the inventory screen are listed above.");
            }
        }
        catch (Exception e)
        {
            c.Note($"{label}: the layout facts could not be gathered ({e.GetType().Name}: {e.Message})");
        }
    }

    private static string TankardFacts()
    {
        var shared = Shared("Tankard");
        return shared == null
            ? "the game has no item 'Tankard'"
            : $"Tankard in this game: item type {shared.m_itemType}, skill {shared.m_skillType}, animation {shared.m_animationState}; "
              + $"kind {ItemKinds.Classify(shared)}, type rank {TypeRank("Tankard")}";
    }

    private static Rect WorldRect(RectTransform rt)
    {
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        var min = corners[0];
        var max = corners[0];
        for (var i = 1; i < 4; i++)
        {
            min = Vector3.Min(min, corners[i]);
            max = Vector3.Max(max, corners[i]);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static bool Overlaps(Rect a, Rect b, float tolerance) =>
        Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) > tolerance
        && Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > tolerance;

    private static string Fmt(Rect r) => $"({r.xMin:F0},{r.yMin:F0})-({r.xMax:F0},{r.yMax:F0})";

    private static bool Overlay(GameObject go)
    {
        var canvas = go.GetComponentInParent<Canvas>();
        return canvas != null && canvas.rootCanvas != null && canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay;
    }

    // Own measure (not the mod's): the two buttons against each other, the panel parts T13 names, and the screen.
    private static string LayoutProblem(InventoryGui gui, SortChestUi.TestView sort, SortChestUi.TestView criterion)
    {
        var s = WorldRect(sort.Rect);
        var k = WorldRect(criterion.Rect);
        if (s.width < 4f || s.height < 4f || k.width < 4f || k.height < 4f)
        {
            return $"a button has no size (Sort {Fmt(s)}, sort order {Fmt(k)})";
        }
        var tolerance = 0.05f * Mathf.Min(s.height, k.height);
        if (Overlaps(s, k, tolerance))
        {
            return $"the two buttons overlap (Sort {Fmt(s)}, sort order {Fmt(k)})";
        }
        var grid = gui.ContainerGrid;
        var parts = new (string What, RectTransform Rect)[]
        {
            ("Take all", gui.m_takeAllButton != null ? gui.m_takeAllButton.transform as RectTransform : null),
            ("Place stacks", gui.m_stackAllButton != null ? gui.m_stackAllButton.transform as RectTransform : null),
            ("the container name", gui.m_containerName != null ? gui.m_containerName.rectTransform : null),
            ("the container weight", gui.m_containerWeight != null ? gui.m_containerWeight.rectTransform : null),
            ("the container grid", grid != null ? grid.transform as RectTransform : null),
            ("the grid scrollbar", grid != null && grid.m_scrollbar != null ? grid.m_scrollbar.transform as RectTransform : null),
        };
        foreach (var part in parts)
        {
            if (part.Rect == null || !part.Rect.gameObject.activeInHierarchy)
            {
                continue;
            }
            var r = WorldRect(part.Rect);
            if (Overlaps(s, r, tolerance))
            {
                return $"Sort {Fmt(s)} overlaps {part.What} {Fmt(r)}";
            }
            if (Overlaps(k, r, tolerance))
            {
                return $"the sort order button {Fmt(k)} overlaps {part.What} {Fmt(r)}";
            }
        }
        if (Overlay(sort.Go))
        {
            if (s.xMin < -1f || s.yMin < -1f || s.xMax > Screen.width + 1f || s.yMax > Screen.height + 1f
                || k.xMin < -1f || k.yMin < -1f || k.xMax > Screen.width + 1f || k.yMax > Screen.height + 1f)
            {
                return $"a button is off screen (Sort {Fmt(s)}, sort order {Fmt(k)}, screen {Screen.width}x{Screen.height})";
            }
        }
        return null;
    }

    // Wait until the mod's own layout check of this opening is over (it runs once per size combination; the test
    // made it forget this one before opening).
    private static IEnumerator WaitLayout(Rig rig, Container container, int stage, Box box)
    {
        var inventory = container.GetInventory();
        var width = inventory.GetWidth();
        var height = inventory.GetHeight();
        var rows = rig.P.GetInventory().GetHeight();
        var end = Time.realtimeSinceStartup + 6f;
        while (Time.realtimeSinceStartup < end && SortChestUi.TestLayoutStage(width, height, rows) < stage)
        {
            yield return null;
        }
        var reached = SortChestUi.TestLayoutStage(width, height, rows);
        box.Ok = reached >= stage;
        box.Detail = $"stage {reached} of {stage}";
    }

    private static void ForgetLayout(Rig rig, Container container)
    {
        var inventory = container.GetInventory();
        SortChestUi.TestForgetLayout(inventory.GetWidth(), inventory.GetHeight(), rig.P.GetInventory().GetHeight());
    }

    private static string Safe(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]))
            {
                chars[i] = '-';
            }
        }
        return new string(chars);
    }

    // One container kind: panel opens, buttons shown, nothing overlaps (own measure), pointer reach each button; with
    // sortToo three items with gaps end in the first three slots. The mod's own layout verdict and its "Sort buttons"
    // warning are only noted here: sortchest.bug.layout-warning checks them (buttons sit right of the panel today).
    private static IEnumerator CheckContainer(Rig rig, Checks c, Container container, string label, string test, bool sortToo)
    {
        var gui = rig.Gui;
        var inventory = container.GetInventory();
        var width = inventory.GetWidth();
        var height = inventory.GetHeight();
        var cap = width * height;
        var rows = rig.P.GetInventory().GetHeight();
        if (sortToo)
        {
            if (!c.Check(cap >= 4, $"{label}: {width}x{height} is too small for the three test items"))
            {
                yield break;
            }
            Fill(c, container, (cap - 1, "Wood", 3), (cap / 2, "Stone", 2), (1, "Resin", 1));
        }
        ForgetLayout(rig, container);
        var box = new Box();
        yield return Open(rig, container, box);
        if (!c.Check(box.Ok, $"{label}: the container panel opens ({box.Detail})"))
        {
            yield break;
        }
        yield return WaitLayout(rig, container, 2, box);
        c.Check(box.Ok, $"{label}: the mod's layout check of this opening finished ({box.Detail})");
        var sort = SortChestUi.TestSort;
        var criterion = SortChestUi.TestCriterion;
        var shown = sort != null && criterion != null && sort.Go.activeInHierarchy && criterion.Go.activeInHierarchy;
        c.Check(shown, $"{label}: Sort and sort order buttons are shown");
        if (shown)
        {
            var measured = LayoutProblem(gui, sort, criterion);
            c.Check(measured == null, $"{label}: {measured}");
            c.Check(Overlay(sort.Go), $"{label}: the buttons' place on screen can be measured (screen-space canvas)");
            // Nothing that takes the pointer lies over the middle of a button.
            var overSort = Hit(sort, out _);
            var overCriterion = Hit(criterion, out _);
            c.Check(overSort != null && overSort.transform.IsChildOf(sort.Go.transform),
                $"{label}: a pointer on the middle of Sort reaches the button first (reached '{(overSort != null ? overSort.name : "nothing")}')");
            c.Check(overCriterion != null && overCriterion.transform.IsChildOf(criterion.Go.transform),
                $"{label}: a pointer on the middle of the sort order button reaches it first (reached '{(overCriterion != null ? overCriterion.name : "nothing")}')");
            var root = container.transform.root;
            var own = SortChestUi.TestLayoutProblem(gui);
            c.Note($"{label} ({Utils.GetPrefabName(root.gameObject)}) is {width}x{height} with {rows} inventory rows: buttons "
                   + $"{(SortChestUi.TestSecondRow(width, height, rows) ? "in a second row" : "next to Place stacks")}, Sort "
                   + $"{Fmt(WorldRect(sort.Rect))}, sort order {Fmt(WorldRect(criterion.Rect))}, container panel {Fmt(WorldRect(gui.m_container))}, "
                   + $"screen {Screen.width}x{Screen.height}; the mod's own layout check says: {(own == null ? "fine" : "the buttons " + own)}; "
                   + $"its storage {(ReferenceEquals(container.m_nview, root.GetComponent<ZNetView>()) ? "shares the network object of the whole thing" : "has its own network object")}");
        }
        SelfTest.Screenshot(test, Safe(label) + "-" + rows + "rows");
        yield return null;
        yield return null;
        if (sortToo && shown)
        {
            var order = ByName("Wood", "Stone", "Resin");
            var stacks = new Dictionary<string, int> { { "Wood", 3 }, { "Stone", 2 }, { "Resin", 1 } };
            var want = Expect(inventory, order.Select(p => Cell(p, stacks[p])));
            var click = ClickSort(rig, container);
            c.Check(click.Sorted && Same(Slots(inventory), want),
                $"{label}: Sort fills the rows from the top-left slot: {Show(Slots(inventory))}, expected {Show(want)} ({click})");
        }
        yield return Close(rig);
    }

    // ================================================================ sortchest.name (T01, T07)

    private static IEnumerator RunName()
    {
        const string N = NameTest;
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        Rig rig = null;
        try
        {
            rig = new Rig();
            var chest = rig.Spawn(Chest);
            if (!c.Check(chest != null && chest.GetInventory() != null, $"setup: {Chest} with a container"))
            {
                c.Report();
                yield break;
            }
            var inv = chest.GetInventory();
            var cap = Cap(inv);
            c.Note($"Chest ({Chest}) is {inv.GetWidth()}x{inv.GetHeight()}; game language {Localization.instance.GetSelectedLanguage()}");
            if (!c.Check(cap >= BasicItems.Length + 2, $"setup: the Chest has {cap} slots, the test needs {BasicItems.Length + 2}"))
            {
                c.Report();
                yield break;
            }
            var stacks = new Dictionary<string, int>();
            foreach (var p in BasicItems)
            {
                stacks[p] = p == "Wood" ? 10 : p == "Stone" ? 5 : 1;
            }
            Fill(c, chest, Scatter(cap, BasicItems.Select(p => (p, stacks[p])).ToArray()));
            var totals = Totals(inv);
            SetSortBy(SortCriterion.Name);
            Plugin.TestMergeStacks = true;
            var box = new Box();
            yield return Open(rig, chest, box);
            if (!c.Check(box.Ok, $"the Chest panel opens ({box.Detail})"))
            {
                c.Report();
                yield break;
            }
            var sort = SortChestUi.TestSort;
            var criterion = SortChestUi.TestCriterion;
            c.Check(sort != null && criterion != null && sort.Go.activeInHierarchy && criterion.Go.activeInHierarchy,
                "Sort and sort order buttons are shown on the Chest panel");
            c.Check(sort != null && sort.Label == "Sort" && criterion != null && criterion.Label == "By name",
                $"button labels 'Sort' and 'By name' (got '{(sort != null ? sort.Label : null)}', '{(criterion != null ? criterion.Label : null)}')");

            // In English the order is the one written in TESTING.md; else the order of the names in the game language.
            var order = ByName(BasicItems);
            if (English)
            {
                c.Check(order.SequenceEqual(BasicByEnglishName),
                    $"the English names give the order of T01 (got {string.Join(", ", order.Select(p => LocalName(p)))})");
                order = BasicByEnglishName;
            }
            else
            {
                c.Note("game language is not English: checked against the order of the names in the game language: "
                       + string.Join(", ", order.Select(p => LocalName(p))));
            }
            var want = Expect(inv, order.Select(p => Cell(p, stacks[p])));
            var before = Slots(inv);
            c.Check(!Same(before, want), "setup: the Chest starts unsorted, with gaps");

            var click = ClickSort(rig, chest);
            c.Check(click.Sent, $"the Sort button can be clicked ({click.Why})");
            c.Check(click.Sorted && click.Moved > 0, $"the click sorts ({click})");
            c.Check(Same(Slots(inv), want), $"by name, reading order, empty slots last: {Show(Slots(inv))}, expected {Show(want)}");
            c.Check(Totals(inv) == totals && inv.NrOfItems() == BasicItems.Length, $"nothing lost or duplicated ({Totals(inv)}, was {totals})");
            c.Check(click.Saved, "the sorted Chest is saved (network data changed)");
            var stored = Decode(chest, StoredBytes(chest));
            c.Check(stored != null && Same(Slots(stored), want), $"the saved data hold the same layout: {Show(stored != null ? Slots(stored) : null)}");
            if (click.SoundKinds > 0)
            {
                c.Check(click.Sounds == click.SoundKinds, $"one move sound ({click.Sounds} sound object(s) made, expected {click.SoundKinds})");
            }
            else
            {
                c.Note("the game has no item-move sound set: sound not checked");
            }
            SelfTest.Screenshot(N, "sorted");
            yield return null;
            yield return null;

            // T07: second click.
            var bytes = StoredBytes(chest);
            var logMark = _watch != null ? _watch.MineCount : 0;
            var again = ClickSort(rig, chest);
            c.Check(again.Sent && again.Outcome == ContainerSorter.SortOutcome.Done && !again.Written && again.Moved == 0 && again.Merged == 0,
                $"second click: already sorted, nothing written ({again})");
            c.Check(_watch != null && _watch.MineSince(logMark).Any(l => l.Key == LogLevel.Debug
                                                                         && l.Value.IndexOf("(already sorted, nothing saved)", StringComparison.Ordinal) >= 0),
                "second click writes the Debug line '(already sorted, nothing saved)'");
            c.Check(!again.Saved && SameBytes(bytes, StoredBytes(chest)), "second click saves nothing");
            c.Check(again.Sounds == 0, $"second click makes no move sound ({again.Sounds})");
            c.Check(Same(Slots(inv), want), $"second click moves nothing: {Show(Slots(inv))}");

            // Close and reopen: the game loads the saved data again.
            yield return Close(rig);
            yield return new WaitForSecondsRealtime(1.3f);
            yield return Open(rig, chest, box);
            c.Check(box.Ok && Same(Slots(chest.GetInventory()), want),
                $"after closing and reopening: same layout ({box.Detail}) {Show(Slots(chest.GetInventory()))}");
            c.Report();
        }
        finally
        {
            rig?.Done();
        }
    }

    // ================================================================ sortchest.type (T02, C03 type groups)

    private static IEnumerator RunType()
    {
        const string N = TypeTest;
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        Rig rig = null;
        try
        {
            rig = new Rig();
            var chest = rig.Spawn(Chest);
            if (!c.Check(chest != null && chest.GetInventory() != null && Cap(chest.GetInventory()) >= BasicItems.Length + 2,
                    $"setup: {Chest} with at least {BasicItems.Length + 2} slots"))
            {
                c.Report();
                yield break;
            }
            var inv = chest.GetInventory();
            var cap = Cap(inv);
            Fill(c, chest, Scatter(cap, BasicItems.Select(p => (p, 1)).ToArray()));
            SetSortBy(SortCriterion.Type);
            var box = new Box();
            yield return Open(rig, chest, box);
            if (!c.Check(box.Ok, $"the Chest panel opens ({box.Detail})"))
            {
                c.Report();
                yield break;
            }
            // Weapons (axe before club), food, materials by name, trophy.
            var materials = ByName("Flint", "Resin", "Stone", "Wood");
            if (English)
            {
                c.Check(materials.SequenceEqual(new[] { "Flint", "Resin", "Stone", "Wood" }),
                    $"English material names in the order of T02 (got {string.Join(", ", materials)})");
            }
            var order = new List<string> { "AxeFlint", "Club", "Raspberry" };
            order.AddRange(materials);
            order.Add("TrophyBoar");
            var want = Expect(inv, order);
            var click = ClickSort(rig, chest);
            c.Check(click.Sent && click.Sorted, $"the click sorts by type ({click})");
            c.Check(Same(Slots(inv), want), $"by type: {Show(Slots(inv))}, expected {Show(want)}");
            c.Check(TypeRank("AxeFlint") == "0.1" && TypeRank("Club") == "0.2",
                $"Flint Axe is an axe (0.1) and the Club a club (0.2): {TypeRank("AxeFlint")}, {TypeRank("Club")}");
            c.Check(TypeRank("Raspberry") == "6.0" && TypeRank("Wood") == "7.0" && TypeRank("TrophyBoar") == "9.0",
                $"food 6.0, material 7.0, trophy 9.0: {TypeRank("Raspberry")}, {TypeRank("Wood")}, {TypeRank("TrophyBoar")}");
            SelfTest.Screenshot(N, "by-type");
            yield return null;
            yield return null;

            // C03: tools and light together, then food, then mead, then material. The tankard of C03 is not in this
            // chest: the game's Tankard has the torch animation, so today it lands between pickaxe and torch
            // (sortchest.bug.tankard-group holds that check alone).
            yield return Close(rig);
            var second = new[] { "Wood", "MeadHealthMinor", "Torch", "Raspberry", "PickaxeAntler" };
            Fill(c, chest, Scatter(cap, second.Select(p => (p, 1)).ToArray()));
            SetSortBy(SortCriterion.Type);
            yield return Open(rig, chest, box);
            if (c.Check(box.Ok, $"the Chest panel opens again ({box.Detail})"))
            {
                var want2 = Expect(inv, new[] { "PickaxeAntler", "Torch", "Raspberry", "MeadHealthMinor", "Wood" });
                click = ClickSort(rig, chest);
                c.Check(click.Sent && click.Sorted && Same(Slots(inv), want2),
                    $"pickaxe and torch together, food, mead, material: {Show(Slots(inv))}, expected {Show(want2)} ({click})");
                c.Check(TypeRank("PickaxeAntler") == "5.1" && TypeRank("Torch") == "5.2" && TypeRank("MeadHealthMinor") == "6.1",
                    $"type ranks pickaxe 5.1, torch 5.2, mead 6.1: {TypeRank("PickaxeAntler")}, {TypeRank("Torch")}, {TypeRank("MeadHealthMinor")}");
                c.Check(ItemKinds.Classify(Shared("MeadHealthMinor")) == ItemKind.Potion,
                    $"the mead's kind is Potion, the 'Meads and potions' of Crafting Search and Sort ({ItemKinds.Classify(Shared("MeadHealthMinor"))})");
            }
            c.Report();
        }
        finally
        {
            rig?.Done();
        }
    }

    // ================================================================ sortchest.biome (T03, T14)

    private static IEnumerator RunBiome()
    {
        const string N = BiomeTest;
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        Rig rig = null;
        try
        {
            rig = new Rig();
            var order = new (string Prefab, BiomeRank Rank)[]
            {
                ("Raspberry", BiomeRank.Meadows), ("Wood", BiomeRank.Meadows), ("SwordBronze", BiomeRank.BlackForest),
                ("CopperOre", BiomeRank.BlackForest), ("ArmorIronChest", BiomeRank.Swamp), ("Guck", BiomeRank.Swamp),
                ("SerpentScale", BiomeRank.Ocean), ("SilverOre", BiomeRank.Mountain), ("Tar", BiomeRank.Plains),
                ("BlackMarble", BiomeRank.Mistlands), ("FlametalOreNew", BiomeRank.Ashlands), ("Frostwood", BiomeRank.DeepNorth),
            };
            // Order of TESTING.md T03 ("put in").
            var put = new[] { "Frostwood", "Tar", "Wood", "SilverOre", "FlametalOreNew", "SerpentScale", "Guck", "BlackMarble", "CopperOre",
                "SwordBronze", "ArmorIronChest", "Raspberry" };
            var chest = rig.Spawn("piece_chest_blackmetal");
            if (!c.Check(chest != null && chest.GetInventory() != null && Cap(chest.GetInventory()) >= put.Length + 2,
                    $"setup: piece_chest_blackmetal with at least {put.Length + 2} slots"))
            {
                c.Report();
                yield break;
            }
            var inv = chest.GetInventory();
            c.Note($"Black metal chest (piece_chest_blackmetal) is {inv.GetWidth()}x{inv.GetHeight()}");
            Fill(c, chest, Scatter(Cap(inv), put.Select(p => (p, 1)).ToArray()));

            // Like the first "By biome" sort after loading a world: the index is not built yet.
            BiomeIndex.Clear();
            var builds = BiomeIndex.TestBuilds;
            var logMark = _watch != null ? _watch.MineCount : 0;
            SetSortBy(SortCriterion.Biome);
            var box = new Box();
            yield return Open(rig, chest, box);
            if (!c.Check(box.Ok, $"the Black metal chest panel opens ({box.Detail})"))
            {
                c.Report();
                yield break;
            }
            var want = Expect(inv, order.Select(o => o.Prefab));
            var click = ClickSort(rig, chest);
            c.Check(click.Sent && click.Sorted, $"the click sorts by biome ({click})");
            c.Check(Same(Slots(inv), want), $"by biome, Meadows to Deep North, type then name inside a biome: {Show(Slots(inv))}, expected {Show(want)}");
            foreach (var o in order)
            {
                var known = BiomeIndex.TestEntry(o.Prefab, out var rank, out var source);
                c.Check(known && rank == o.Rank, $"{o.Prefab} belongs to {o.Rank} (index says {(known ? rank + " (" + source + ")" : "unknown")})");
            }
            SelfTest.Screenshot(N, "by-biome");
            yield return null;
            yield return null;

            // T14: built once, fast, and its log lines.
            c.Check(BiomeIndex.TestBuilds == builds + 1, $"the first By biome sort builds the biome index once ({BiomeIndex.TestBuilds - builds} builds)");
            c.Check(BiomeIndex.TestLastBuildMs >= 0 && BiomeIndex.TestLastBuildMs < 1000,
                $"the build takes well under a second ({BiomeIndex.TestLastBuildMs} ms)");
            var lines = _watch != null ? _watch.MineSince(logMark) : new List<KeyValuePair<LogLevel, string>>();
            var built = lines.Where(l => l.Key == LogLevel.Info && l.Value.StartsWith("Biome index built in ", StringComparison.Ordinal)).ToList();
            c.Check(built.Count == 1, $"one Info line 'Biome index built in ... ms' ({built.Count})");
            if (built.Count > 0)
            {
                c.Note(built[0].Value);
            }
            var perItem = new Dictionary<string, string>(StringComparer.Ordinal);
            const string head = "Biome index: ";
            foreach (var line in lines)
            {
                if (line.Key != LogLevel.Debug || !line.Value.StartsWith(head, StringComparison.Ordinal))
                {
                    continue;
                }
                var arrow = line.Value.IndexOf(" -> ", StringComparison.Ordinal);
                if (arrow > head.Length && line.Value.IndexOf(" | type ", StringComparison.Ordinal) > arrow)
                {
                    perItem[line.Value.Substring(head.Length, arrow - head.Length)] = line.Value;
                }
            }
            var itemNames = new HashSet<string>(ObjectDB.instance.m_items.Where(go => go != null).Select(go => go.name), StringComparer.Ordinal);
            c.Check(perItem.Count == itemNames.Count && itemNames.All(perItem.ContainsKey),
                $"one Debug line per item ({perItem.Count} lines, {itemNames.Count} items)");
            var expected = new (string Prefab, string[] Parts)[]
            {
                ("Wood", new[] { "Wood -> Meadows (Table)" }),
                ("Bronze", new[] { "Bronze -> BlackForest (Derived)" }),
                ("BoarJerky", new[] { "BoarJerky -> BlackForest (Derived)" }),
                ("MeadHealthMinor", new[] { "MeadHealthMinor -> BlackForest (Derived)", "-> 6.1 Food and potions" }),
                ("Coal", new[] { "Coal -> BlackForest (Table)" }),
                ("Hammer", new[] { "-> 5.0 Tools and light" }),
                ("Hoe", new[] { "-> 5.0 Tools and light" }),
                ("Cultivator", new[] { "-> 5.0 Tools and light" }),
                ("PickaxeAntler", new[] { "-> 5.1 Tools and light" }),
                ("FishingRod", new[] { "-> 5.1 Tools and light" }),
                ("Scythe", new[] { "-> 5.1 Tools and light" }),
                ("Torch", new[] { "-> 5.2 Tools and light" }),
                ("SwordBronze", new[] { "-> 0.0 Weapons" }),
            };
            // Tankard line of T14 ("-> 10.0 Other") not here: wrong today, sortchest.bug.tankard-group hold it alone.
            if (perItem.TryGetValue("Tankard", out var tankardLine))
            {
                c.Note(tankardLine);
            }
            foreach (var e in expected)
            {
                var has = perItem.TryGetValue(e.Prefab, out var text);
                c.Check(has && e.Parts.All(part => text.IndexOf(part, StringComparison.Ordinal) >= 0),
                    $"log line of {e.Prefab} says '{string.Join("' and '", e.Parts)}' (line: {(has ? text : "none")})");
            }
            var blue = perItem.TryGetValue("MushroomBlue", out var blueLine) ? blueLine : null;
            c.Check(blue != null && (blue.IndexOf("(Scan)", StringComparison.Ordinal) >= 0 || blue.IndexOf("MushroomBlue -> Unknown", StringComparison.Ordinal) >= 0),
                $"log line of MushroomBlue says a scanned biome or Unknown (line: {blue ?? "none"})");
            if (blue != null)
            {
                c.Note(blue);
            }
            var failed = Warnings(logMark, "Biome index");
            c.Check(failed.Count == 0, $"no warning from the biome index build: {(failed.Count > 0 ? failed[0] : "")}");

            // Second By biome sort in the same world: no rebuild.
            c.Check(Unsort(inv), "setup: two items swapped by hand");
            var again = ClickSort(rig, chest);
            c.Check(again.Sent && again.Sorted && Same(Slots(inv), want), $"second By biome sort puts the two items back ({again})");
            var builtAgain = _watch != null
                ? _watch.MineSince(logMark).Count(l => l.Value.StartsWith("Biome index built in ", StringComparison.Ordinal))
                : 0;
            c.Check(BiomeIndex.TestBuilds == builds + 1 && builtAgain == 1,
                $"the second By biome sort does not rebuild the index ({BiomeIndex.TestBuilds - builds} builds, {builtAgain} 'built' lines)");
            c.Report();
        }
        finally
        {
            rig?.Done();
        }
    }

    // ================================================================ sortchest.criterion (T04, without the config file)

    private static string Label(SortCriterion criterion) =>
        criterion == SortCriterion.Name ? "By name" : criterion == SortCriterion.Biome ? "By biome" : "By type";

    private static string Words(SortCriterion criterion) =>
        criterion == SortCriterion.Name ? "by name" : criterion == SortCriterion.Biome ? "by biome" : "by type";

    // Label and Sort tooltip show this sort order.
    private static bool Shows(SortCriterion criterion, out string got)
    {
        var sort = SortChestUi.TestSort;
        var button = SortChestUi.TestCriterion;
        got = $"label '{(button != null ? button.Label : null)}', Sort tooltip '{(sort != null && sort.Tip != null ? sort.Tip.m_text : null)}'";
        return button != null && button.Label == Label(criterion) && sort != null && sort.Tip != null
               && sort.Tip.m_text == SortTipStart + Words(criterion) + ".";
    }

    private static IEnumerator RunCriterion()
    {
        const string N = CriterionTest;
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        Rig rig = null;
        try
        {
            rig = new Rig();
            var chest = rig.Spawn(Chest);
            if (!c.Check(chest != null && chest.GetInventory() != null && Cap(chest.GetInventory()) >= 6, $"setup: {Chest} with at least 6 slots"))
            {
                c.Report();
                yield break;
            }
            var inv = chest.GetInventory();
            Fill(c, chest, Scatter(Cap(inv), ("Wood", 4), ("AxeFlint", 1), ("Raspberry", 2), ("TrophyBoar", 1)));
            var config = Plugin.SortBy.Value;
            SetSortBy(SortCriterion.Name);
            var box = new Box();
            yield return Open(rig, chest, box);
            if (!c.Check(box.Ok, $"the Chest panel opens ({box.Detail})"))
            {
                c.Report();
                yield break;
            }
            var slots = Slots(inv);
            var rev = Rev(chest);
            var clicks = ContainerSorter.Clicks;
            var sounds = MoveSounds(rig.Gui);
            c.Check(Shows(SortCriterion.Name, out var got), $"start: By name ({got})");
            foreach (var next in new[] { SortCriterion.Type, SortCriterion.Biome, SortCriterion.Name })
            {
                var sent = Press(SortChestUi.TestCriterion, out var why);
                yield return null;
                yield return null;
                c.Check(sent, $"the sort order button can be clicked ({why})");
                c.Check(Plugin.TestSortBy == next && Shows(next, out got), $"one click: sort order {next}, label and Sort tooltip follow ({Plugin.TestSortBy}, {got})");
                c.Check(Same(Slots(inv), slots) && Rev(chest) == rev && ContainerSorter.Clicks == clicks,
                    $"the click on the sort order button does not sort or save ({Show(Slots(inv))})");
            }
            var button = SortChestUi.TestCriterion;
            c.Check(button != null && button.Tip != null && button.Tip.m_topic == CriterionTopic && button.Tip.m_text == CriterionTip,
                $"sort order tooltip unchanged by the clicks ('{(button != null && button.Tip != null ? button.Tip.m_text : null)}')");
            c.Check(MoveSounds(rig.Gui).All(sounds.Contains), "no move sound from the sort order button");
            c.Check(Plugin.SortBy.Value == config, $"the test never wrote the SortBy setting (still {config})");

            // The shown order is the one Sort uses.
            SetSortBy(SortCriterion.Type);
            c.Check(Shows(SortCriterion.Type, out got), $"a change from outside the button shows too ({got})");
            var click = ClickSort(rig, chest);
            c.Check(click.Sent && click.Sorted && ContainerSorter.LastCriterion == SortCriterion.Type,
                $"Sort then uses the shown order ({ContainerSorter.LastCriterion}, {click})");
            c.Report();
        }
        finally
        {
            rig?.Done();
        }
    }

    // ================================================================ sortchest.merge (T05, T06)

    private static IEnumerator RunMerge()
    {
        const string N = MergeTest;
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        Rig rig = null;
        try
        {
            rig = new Rig();
            var chest = rig.Spawn(Chest);
            if (!c.Check(chest != null && chest.GetInventory() != null && Cap(chest.GetInventory()) >= 10, $"setup: {Chest} with at least 10 slots"))
            {
                c.Report();
                yield break;
            }
            var inv = chest.GetInventory();
            var wood = Shared("Wood");
            var max = wood != null ? wood.m_maxStackSize : 0;
            if (!c.Check(max >= 31, $"setup: Wood stacks to at least 31 (stack size {max})"))
            {
                c.Report();
                yield break;
            }
            if (max != 50)
            {
                c.Note($"Wood's stack size is {max}, not the game's 50 (another mod?): expected stacks worked out from {max}");
            }

            // T05, merge on: 20 + 30 + 15 -> full stack(s) first, adjacent.
            Fill(c, chest, (0, "Wood", 20), (4, "Wood", 30), (8, "Wood", 15));
            SetSortBy(SortCriterion.Name);
            Plugin.TestMergeStacks = true;
            var box = new Box();
            yield return Open(rig, chest, box);
            if (!c.Check(box.Ok, $"the Chest panel opens ({box.Detail})"))
            {
                c.Report();
                yield break;
            }
            yield return null;
            var weight = inv.GetTotalWeight();
            var weightText = rig.Gui.m_containerWeight.text;
            var merged = new List<int>();
            for (var left = 65; left > 0; left -= max)
            {
                merged.Add(Mathf.Min(left, max));
            }
            var want = Expect(inv, merged.Select(n => Cell("Wood", n)));
            var click = ClickSort(rig, chest);
            c.Check(click.Sent && click.Sorted, $"the click merges and sorts ({click})");
            c.Check(Same(Slots(inv), want), $"20 + 30 + 15 Wood become {string.Join(" + ", merged)}, adjacent from the first slot: {Show(Slots(inv))}");
            c.Check(click.Merged == 3 - merged.Count, $"{3 - merged.Count} stack(s) merged away ({click.Merged})");
            c.Check(Mathf.Abs(inv.GetTotalWeight() - weight) < 0.001f, $"container weight unchanged ({inv.GetTotalWeight()}, was {weight})");
            yield return null;
            yield return null;
            c.Check(rig.Gui.m_containerWeight.text == weightText, $"the weight number on the panel is unchanged ('{rig.Gui.m_containerWeight.text}', was '{weightText}')");
            var stored = Decode(chest, StoredBytes(chest));
            c.Check(stored != null && Same(Slots(stored), want), $"the saved data hold the merged stacks: {Show(stored != null ? Slots(stored) : null)}");
            SelfTest.Screenshot(N, "merged");
            yield return null;
            yield return null;

            // T05, merge off: stacks stay separate, larger first, adjacent.
            yield return Close(rig);
            Fill(c, chest, (0, "Wood", 20), (4, "Wood", 30), (8, "Wood", 15));
            SetSortBy(SortCriterion.Name);
            Plugin.TestMergeStacks = false;
            yield return Open(rig, chest, box);
            if (c.Check(box.Ok, $"the Chest panel opens again ({box.Detail})"))
            {
                want = Expect(inv, new[] { Cell("Wood", 30), Cell("Wood", 20), Cell("Wood", 15) });
                click = ClickSort(rig, chest);
                c.Check(click.Sent && click.Sorted && click.Merged == 0 && Same(Slots(inv), want),
                    $"merging off: stacks stay separate, larger first, adjacent: {Show(Slots(inv))} ({click})");
            }

            // T06: stacks that differ in anything never merge. Chest stays open while item objects are compared.
            yield return Close(rig);
            var items = Fill(c, chest, (0, "Wood", 15), (3, "Wood", 20), (5, "Wood", 5), (6, "Wood", 6), (7, "Wood", 7), (8, "Wood", 8));
            if (items.All(i => i != null))
            {
                var clean = items[0];
                var cheated = items[1];
                cheated.m_cheated = true;
                items[2].m_crafterID = 77L;
                items[2].m_crafterName = "MCProbe crafter";
                items[3].m_customData["MC.SelfTest.Mark"] = "1";
                items[4].m_worldLevel = 1;
                items[5].m_pickedUp = false;
                inv.Changed();
                SetSortBy(SortCriterion.Name);
                Plugin.TestMergeStacks = true;
                yield return Open(rig, chest, box);
                if (c.Check(box.Ok, $"the Chest panel opens a third time ({box.Detail})"))
                {
                    var tipClean = clean.GetTooltip();
                    var tipCheated = cheated.GetTooltip();
                    var mark = Localization.instance.Localize("$achievements_cheated_item_inventory");
                    if (PlayerProfile.s_bypassCheatChecks)
                    {
                        c.Note("cheat checks are bypassed on this profile: the tooltips carry no 'cheated' line");
                    }
                    else
                    {
                        c.Check(tipCheated.IndexOf(mark, StringComparison.Ordinal) >= 0 && tipClean.IndexOf(mark, StringComparison.Ordinal) < 0,
                            $"setup: only the cheated stack's tooltip has the cheated line ('{mark}')");
                    }
                    var stacks = items.Select(i => i.m_stack).ToArray();
                    click = ClickSort(rig, chest);
                    c.Check(click.Sent && click.Outcome == ContainerSorter.SortOutcome.Done && click.Merged == 0, $"nothing merged ({click})");
                    c.Check(inv.NrOfItems() == items.Count && items.All(inv.ContainsItem), "every stack is still there, the same item objects");
                    c.Check(items.Select(i => i.m_stack).SequenceEqual(stacks), $"stack sizes unchanged ({string.Join(", ", items.Select(i => i.m_stack))})");
                    c.Check(cheated.m_cheated && !clean.m_cheated && items[2].m_crafterID == 77L && items[2].m_crafterName == "MCProbe crafter"
                            && items[3].m_customData.Count == 1 && items[4].m_worldLevel == 1 && !items[5].m_pickedUp && clean.m_pickedUp,
                        "cheated mark, crafter, extra data, world level and picked-up mark each stay on their own stack");
                    c.Check(clean.GetTooltip() == tipClean && cheated.GetTooltip() == tipCheated, "the tooltips of the clean and the cheated stack are unchanged");
                    want = Expect(inv, new[] { 20, 15, 8, 7, 6, 5 }.Select(n => Cell("Wood", n)));
                    c.Check(Same(Slots(inv), want), $"sorted next to each other, larger first: {Show(Slots(inv))}");
                }
            }
            c.Report();
        }
        finally
        {
            rig?.Done();
        }
    }

    // ================================================================ sortchest.drag (T08)

    private static IEnumerator RunDrag()
    {
        const string N = DragTest;
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        Rig rig = null;
        try
        {
            rig = new Rig();
            var gui = rig.Gui;
            var chest = rig.Spawn(Chest);
            if (!c.Check(chest != null && chest.GetInventory() != null && Cap(chest.GetInventory()) >= 6, $"setup: {Chest} with at least 6 slots"))
            {
                c.Report();
                yield break;
            }
            var inv = chest.GetInventory();
            var stacks = new Dictionary<string, int> { { "Wood", 4 }, { "Stone", 3 }, { "Resin", 2 }, { "Flint", 1 } };
            var items = Fill(c, chest, Scatter(Cap(inv), stacks.Select(kv => (kv.Key, kv.Value)).ToArray()));
            var want = Expect(inv, ByName(stacks.Keys.ToArray()).Select(p => Cell(p, stacks[p])));
            var totals = Totals(inv);
            SetSortBy(SortCriterion.Name);
            var box = new Box();
            yield return Open(rig, chest, box);
            if (!c.Check(box.Ok && items.All(i => i != null), $"the Chest panel opens ({box.Detail})"))
            {
                c.Report();
                yield break;
            }

            // Drag from the chest (what a click on a chest slot starts).
            var held = items[0];
            gui.SetupDragItem(held, inv, held.m_stack);
            yield return null;
            c.Check(gui.m_dragGo != null && ReferenceEquals(gui.m_dragItem, held), "setup: an item of the Chest is being dragged");
            var click = ClickSort(rig, chest);
            c.Check(click.Sent && click.Sorted, $"Sort works during a drag ({click})");
            c.Check(gui.m_dragGo == null && gui.m_dragItem == null && gui.m_dragInventory == null, "the drag is cancelled");
            c.Check(Same(Slots(inv), want), $"the Chest is sorted: {Show(Slots(inv))}, expected {Show(want)}");
            c.Check(Totals(inv) == totals && inv.NrOfItems() == stacks.Count && inv.ContainsItem(held), $"nothing lost or duplicated ({Totals(inv)})");

            // Drag from the player inventory: the item stays there.
            c.Check(Unsort(inv), "setup: two Chest items swapped by hand");
            var mine = rig.P.GetInventory();
            var own = mine.GetAllItems().Count > 0 ? mine.GetAllItems()[0] : null;
            if (own == null)
            {
                // Empty test character: one item put in its list without the pick-up side (no new knowledge, no stat).
                var temp = Make("Wood", 3);
                if (temp != null)
                {
                    temp.m_gridPos = new Vector2i(0, 0);
                    mine.GetAllItems().Add(temp);
                    rig.Undo(() => mine.GetAllItems().Remove(temp));
                    own = temp;
                }
            }
            if (c.Check(own != null, "setup: an item in the player inventory"))
            {
                var stack = own.m_stack;
                var pos = own.m_gridPos;
                var count = mine.NrOfItems();
                gui.SetupDragItem(own, mine, 1);
                yield return null;
                c.Check(gui.m_dragGo != null && ReferenceEquals(gui.m_dragItem, own), "setup: an item of the player inventory is being dragged");
                click = ClickSort(rig, chest);
                c.Check(click.Sent && click.Sorted && Same(Slots(inv), want), $"Sort works during that drag too ({click}) {Show(Slots(inv))}");
                c.Check(gui.m_dragGo == null && gui.m_dragItem == null, "the drag is cancelled");
                c.Check(mine.ContainsItem(own) && own.m_stack == stack && own.m_gridPos == pos && mine.NrOfItems() == count,
                    "the dragged item stays in the player inventory, same slot and amount");
                c.Check(Totals(inv) == totals && inv.NrOfItems() == stacks.Count, $"the Chest holds what it held ({Totals(inv)})");
            }
            c.Report();
        }
        finally
        {
            rig?.Done();
        }
    }

    // ================================================================ sortchest.kinds (T09)

    private static IEnumerator RunKinds()
    {
        const string N = KindsTest;
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        Rig rig = null;
        try
        {
            rig = new Rig();
            SetSortBy(SortCriterion.Name);
            var kinds = new (string Prefab, string Label, Place Place)[]
            {
                ("piece_chest", "Reinforced chest", Place.Near), ("piece_chest_private", "Personal chest", Place.Near),
                ("piece_chest_barrel", "Barrel", Place.Near), ("Cart", "Cart", Place.Vehicle), ("Karve", "Karve", Place.Vehicle),
                ("VikingShip", "Longship", Place.Vehicle),
            };
            foreach (var kind in kinds)
            {
                var container = rig.Spawn(kind.Prefab, kind.Place);
                if (!c.Check(container != null && container.GetInventory() != null, $"{kind.Label}: prefab '{kind.Prefab}' exists and has a container"))
                {
                    continue;
                }
                SetSortBy(SortCriterion.Name);
                yield return CheckContainer(rig, c, container, kind.Label, N, true);
                rig.Destroy(container);
                yield return null;
            }
            c.Report();
        }
        finally
        {
            rig?.Done();
        }
    }

    // ================================================================ sortchest.tombstone (T10)

    private static IEnumerator RunTombstone()
    {
        const string N = TombstoneTest;
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        Rig rig = null;
        try
        {
            rig = new Rig();
            var gui = rig.Gui;
            var go = rig.SpawnObject(rig.P.m_tombstone, Place.Near);
            var tomb = go != null ? go.GetComponent<TombStone>() : null;
            var grave = go != null ? go.GetComponent<Container>() : null;
            if (!c.Check(tomb != null && grave != null && grave.GetInventory() != null && Cap(grave.GetInventory()) >= 6,
                    "setup: the player's tombstone prefab with a container of at least 6 slots"))
            {
                c.Report();
                yield break;
            }
            // Like Player.CreateTombStone: name and owner right after the spawn.
            var profile = Game.instance.GetPlayerProfile();
            tomb.Setup(profile.GetName(), profile.GetPlayerID());
            var inv = grave.GetInventory();
            Fill(c, grave, Scatter(Cap(inv), ("Wood", 5), ("Stone", 4), ("Resin", 3), ("Flint", 2), ("Raspberry", 1)));
            SetSortBy(SortCriterion.Name);
            // The container panel itself (the tombstone's own E key would loot it at once when it fits).
            var box = new Box();
            yield return Open(rig, grave, box);
            if (!c.Check(box.Ok, $"the tombstone panel opens ({box.Detail})"))
            {
                c.Report();
                yield break;
            }
            yield return null;
            var sort = SortChestUi.TestSort;
            var criterion = SortChestUi.TestCriterion;
            c.Check(sort != null && criterion != null && !sort.Go.activeSelf && !criterion.Go.activeSelf, "tombstone panel: no Sort buttons");
            c.Check(gui.m_takeAllButton.gameObject.activeInHierarchy && gui.m_stackAllButton.gameObject.activeInHierarchy,
                "tombstone panel: Take all and Place stacks shown as usual");
            // Even a Sort that gets past the hidden button changes nothing.
            var slots = Slots(inv);
            var rev = Rev(grave);
            ContainerSorter.TestReset();
            ContainerSorter.TrySortOpenContainer();
            c.Check(ContainerSorter.LastOutcome == ContainerSorter.SortOutcome.Ineligible && Same(Slots(inv), slots) && Rev(grave) == rev,
                $"a Sort on the tombstone is refused and changes nothing ({ContainerSorter.LastOutcome})");
            SelfTest.Screenshot(N, "tombstone");
            yield return null;
            yield return null;
            yield return Close(rig);
            rig.Destroy(grave);

            // Next chest: buttons back.
            var chest = rig.Spawn(Chest);
            if (c.Check(chest != null && chest.GetInventory() != null && Cap(chest.GetInventory()) >= 4, $"setup: {Chest}"))
            {
                var chestInv = chest.GetInventory();
                Fill(c, chest, Scatter(Cap(chestInv), ("Wood", 1), ("Stone", 1), ("Resin", 1)));
                yield return Open(rig, chest, box);
                sort = SortChestUi.TestSort;
                criterion = SortChestUi.TestCriterion;
                c.Check(box.Ok && sort != null && criterion != null && sort.Go.activeInHierarchy && criterion.Go.activeInHierarchy,
                    $"a chest opened after the tombstone shows the Sort buttons again ({box.Detail})");
                var click = ClickSort(rig, chest);
                c.Check(click.Sent && click.Sorted && Same(Slots(chestInv), Expect(chestInv, ByName("Wood", "Stone", "Resin"))),
                    $"and Sort works there ({click})");
            }
            c.Report();
        }
        finally
        {
            rig?.Done();
        }
    }

    // ================================================================ sortchest.obliterator (T11)

    private static IEnumerator RunObliterator()
    {
        const string N = ObliteratorTest;
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        Rig rig = null;
        try
        {
            rig = new Rig();
            var go = rig.SpawnObject(ZNetScene.instance.GetPrefab("incinerator"), Place.Away);
            var burner = go != null ? go.GetComponentInChildren<Incinerator>(true) : null;
            var container = burner != null ? burner.m_container : null;
            if (!c.Check(container != null && container.GetInventory() != null && Cap(container.GetInventory()) >= 4 && burner.m_incinerateSwitch != null,
                    "setup: prefab 'incinerator' with its container and lever"))
            {
                c.Report();
                yield break;
            }
            var inv = container.GetInventory();
            c.Note($"Obliterator (incinerator) is {inv.GetWidth()}x{inv.GetHeight()}");
            var cap = Cap(inv);
            Fill(c, container, (cap - 1, "Wood", 3), (cap / 2, "Stone", 2), (1, "Resin", 1));
            SetSortBy(SortCriterion.Name);
            var box = new Box();
            yield return Open(rig, container, box);
            if (!c.Check(box.Ok, $"the Obliterator panel opens ({box.Detail})"))
            {
                c.Report();
                yield break;
            }
            var sort = SortChestUi.TestSort;
            c.Check(sort != null && sort.Go.activeInHierarchy, "the Sort buttons are shown on the Obliterator");
            var stacks = new Dictionary<string, int> { { "Wood", 3 }, { "Stone", 2 }, { "Resin", 1 } };
            var want = Expect(inv, ByName("Wood", "Stone", "Resin").Select(p => Cell(p, stacks[p])));
            var click = ClickSort(rig, container);
            c.Check(click.Sent && click.Sorted && Same(Slots(inv), want), $"Sort works in the Obliterator: {Show(Slots(inv))} ({click})");
            yield return Close(rig);
            c.Check(!container.IsInUse(), "after closing, the Obliterator is no longer in use");

            // The lever, like a player pulling it.
            var allowed = new HashSet<string>(StringComparer.Ordinal);
            if (burner.m_defaultResult != null)
            {
                allowed.Add(burner.m_defaultResult.gameObject.name);
            }
            if (burner.m_conversions != null)
            {
                foreach (var conversion in burner.m_conversions)
                {
                    if (conversion != null && conversion.m_result != null)
                    {
                        allowed.Add(conversion.m_result.gameObject.name);
                    }
                }
            }
            var done = new[] { Localization.instance.Localize("$piece_incinerator_success"), Localization.instance.Localize("$piece_incinerator_conversion") };
            var center = MessageHud.instance != null ? MessageHud.instance.m_messageCenterText : null;
            if (center != null)
            {
                center.text = "";
            }
            var pulled = burner.OnIncinerate(burner.m_incinerateSwitch, rig.P, null);
            c.Check(pulled, "the lever can be pulled");
            var sorted = Show(Slots(inv));
            var end = Time.realtimeSinceStartup + 14f;
            while (Time.realtimeSinceStartup < end && Show(Slots(container.GetInventory())) == sorted)
            {
                yield return null;
            }
            yield return null;
            var left = container.GetInventory().GetAllItems().Select(Prefab).ToList();
            c.Check(Show(Slots(container.GetInventory())) != sorted && left.All(allowed.Contains),
                $"the lever destroyed the sorted items (left: {(left.Count > 0 ? string.Join(", ", left) : "nothing")}; allowed results: {string.Join(", ", allowed)})");
            if (center != null && !Hud.IsUserHidden())
            {
                c.Check(done.Contains(center.text), $"the game's own message for a done obliteration ('{center.text}')");
            }
            c.Report();
        }
        finally
        {
            rig?.Done();
        }
    }

    // ================================================================ sortchest.gamepad (T12; C02 controller part)

    private static IEnumerator RunGamepad()
    {
        const string N = GamepadTest;
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        Rig rig = null;
        try
        {
            rig = new Rig();
            var gui = rig.Gui;
            var chest = rig.Spawn(Chest);
            if (!c.Check(chest != null && chest.GetInventory() != null && Cap(chest.GetInventory()) >= 6, $"setup: {Chest} with at least 6 slots"))
            {
                c.Report();
                yield break;
            }
            var inv = chest.GetInventory();
            var stacks = new Dictionary<string, int> { { "Wood", 4 }, { "Stone", 3 }, { "Resin", 2 }, { "Flint", 1 } };
            Fill(c, chest, Scatter(Cap(inv), stacks.Select(kv => (kv.Key, kv.Value)).ToArray()));
            var want = Expect(inv, ByName(stacks.Keys.ToArray()).Select(p => Cell(p, stacks[p])));
            SetSortBy(SortCriterion.Name);
            ForgetLayout(rig, chest);
            var box = new Box();
            yield return Open(rig, chest, box);
            if (!c.Check(box.Ok, $"the Chest panel opens ({box.Detail})"))
            {
                c.Report();
                yield break;
            }
            yield return WaitLayout(rig, chest, 1, box);
            c.Check(box.Ok, $"the mod's layout check of this opening ran ({box.Detail})");

            // The controller part of Sort. (The sort order button's key: sortchest.bug.pad-order-key.)
            var sort = SortChestUi.TestSort;
            var group = SortChestUi.TestContainerGroup(gui);
            if (!c.Check(sort != null && sort.Pad != null && group != null, "Sort has a controller part and the chest grid has a focus group"))
            {
                c.Report();
                yield break;
            }
            c.Check(sort.Pad.m_zinputKey == SortChestUi.SortKey && sort.Pad.m_keyCode == KeyCode.None,
                $"Sort listens to View/Select only (key '{sort.Pad.m_zinputKey}', keyboard {sort.Pad.m_keyCode})");
            c.Check(ReferenceEquals(sort.Pad.m_group, group),
                $"Sort's controller part belongs to the chest grid group ('{(sort.Pad.m_group != null ? sort.Pad.m_group.name : "none")}', chest grid '{group.name}')");
            var order = SortChestUi.TestCriterion;
            c.Note($"the game's controller keys: {GameKeys(gui)}; the sort order button's key: "
                   + $"'{(order != null && order.Pad != null ? order.Pad.m_zinputKey ?? "none" : "no controller part")}'");
            var takePad = PadOf(gui.m_takeAllButton);
            var stackPad = PadOf(gui.m_stackAllButton);
            // Game button on View/Select: a press here would run it too. No key is pressed then.
            if (!c.Check((takePad == null || takePad.m_zinputKey != SortChestUi.SortKey) && (stackPad == null || stackPad.m_zinputKey != SortChestUi.SortKey),
                    "the game's Take all and Place stacks do not use View/Select (no key pressed in this test otherwise)"))
            {
                c.Report();
                yield break;
            }

            // Chest grid focused: View/Select sorts.
            yield return Focus(rig, group);
            if (!c.Check(group.IsActive, "setup: the chest grid can be focused"))
            {
                c.Report();
                yield break;
            }
            var press = new Clicked();
            yield return PressKey(rig, chest, SortChestUi.SortKey, press);
            c.Check(press.Sent && press.Ran == 1 && press.Sorted && Same(Slots(inv), want),
                $"chest grid focused: View/Select sorts once: {Show(Slots(inv))} ({press}; Sort ran {press.Ran} time(s))");

            // Left stick pushed in the four directions (the game's own buttons for it; simulated, so no accidental
            // stick click): never a sort, never another sort order.
            c.Check(Unsort(inv), "setup: two Chest items swapped by hand");
            var mixed = Slots(inv);
            foreach (var move in new[] { "JoyLStickRight", "JoyLStickDown", "JoyLStickLeft", "JoyLStickUp" })
            {
                yield return PressKey(rig, chest, move, press);
                c.Check(press.Sent && press.Ran == 0 && !press.Saved && Plugin.TestSortBy == SortCriterion.Name && Same(Slots(inv), mixed),
                    $"chest grid focused: the left stick move {move} does not sort or change the sort order "
                    + $"({(press.Sent ? "" : press.Why + "; ")}Sort ran {press.Ran} time(s), order {Plugin.TestSortBy})");
            }

            // Own inventory or crafting panel focused: View/Select does nothing to the chest.
            var others = new List<(string What, UIGroupHandler Group)> { ("your own inventory", gui.m_playerGrid != null ? gui.m_playerGrid.m_uiGroup : null) };
            if (gui.m_uiGroups != null && gui.m_uiGroups.Length > 3)
            {
                others.Add(("the crafting panel", gui.m_uiGroups[3]));
            }
            foreach (var other in others)
            {
                if (!c.Check(other.Group != null && !ReferenceEquals(other.Group, group), $"setup: {other.What} has its own focus group"))
                {
                    continue;
                }
                yield return Focus(rig, other.Group);
                c.Check(!group.IsActive, $"setup: with {other.What} focused the chest grid is not");
                yield return PressKey(rig, chest, SortChestUi.SortKey, press);
                c.Check(press.Sent && press.Ran == 0 && !press.Saved && Same(Slots(inv), mixed),
                    $"{other.What} focused: View/Select does nothing to the chest (Sort ran {press.Ran} time(s)) {Show(Slots(inv))}");
            }
            yield return Focus(rig, group);
            yield return PressKey(rig, chest, SortChestUi.SortKey, press);
            c.Check(press.Sent && press.Ran == 1 && press.Sorted && Same(Slots(inv), want),
                $"back on the chest grid: View/Select sorts again ({press})");

            // Glyph of Sort: only with a controller as input device, and only while the chest grid is focused.
            rig.ForceGamepad();
            yield return Close(rig);
            yield return Open(rig, chest, box);
            sort = SortChestUi.TestSort;
            if (c.Check(box.Ok && sort != null && ZInput.IsGamepadActive(),
                    $"setup: Chest open with a controller as input device ({box.Detail}; controller enabled in the game settings: {ZInput.IsGamepadEnabled()})"))
            {
                if (c.Check(sort.Hint != null, "Sort has a glyph object"))
                {
                    yield return Focus(rig, group);
                    c.Check(sort.Hint.activeInHierarchy, "chest grid focused: the Sort glyph is shown");
                    var glyphBack = Localization.instance.Localize("$KEY_" + SortChestUi.SortKey);
                    c.Check(!string.IsNullOrEmpty(sort.Glyph) && sort.Glyph == glyphBack,
                        $"the glyph text is the game's View/Select glyph ('{sort.Glyph}' / '{glyphBack}')");
                    SelfTest.Screenshot(N, "glyphs");
                    yield return null;
                    yield return null;
                    if (gui.m_playerGrid != null && gui.m_playerGrid.m_uiGroup != null)
                    {
                        yield return Focus(rig, gui.m_playerGrid.m_uiGroup);
                        c.Check(!sort.Hint.activeInHierarchy, "own inventory focused: no glyph on Sort");
                    }
                    yield return Focus(rig, group);
                    c.Check(sort.Hint.activeInHierarchy, "chest grid focused again: the Sort glyph is back");
                }
            }

            // The game's own controller buttons still fire, and only they. Chest emptied first and their work swapped
            // for a counter: Take all would put the items in the test character's inventory.
            yield return Close(rig);
            inv.GetAllItems().Clear();
            inv.Changed();
            SetSortBy(SortCriterion.Name);
            yield return Open(rig, chest, box);
            if (c.Check(box.Ok, $"setup: the emptied Chest opens ({box.Detail})"))
            {
                yield return Focus(rig, group);
                var takeFired = CountInstead(rig, gui.m_takeAllButton, gui.OnTakeAll);
                var stackFired = CountInstead(rig, gui.m_stackAllButton, gui.OnStackAll);
                var pressed = 0;
                foreach (var v in new (string What, UIGamePad Pad, int[] Fired, int[] Other)[]
                         {
                             ("Take all", takePad, takeFired, stackFired), ("Place stacks", stackPad, stackFired, takeFired),
                         })
                {
                    var key = v.Pad != null ? v.Pad.m_zinputKey : null;
                    if (string.IsNullOrEmpty(key) || key == "JoyButtonB" || key == "JoyButtonY")
                    {
                        c.Note($"the game's {v.What} controller button not pressed in this test (key '{key ?? "none"}')");
                        continue;
                    }
                    if (!c.Check(v.Pad.IsInteractive(), $"the game's {v.What} controller button ({key}) is usable with the chest grid focused"))
                    {
                        continue;
                    }
                    pressed++;
                    var before = v.Fired[0];
                    var otherBefore = v.Other[0];
                    yield return PressKey(rig, chest, key, press);
                    c.Check(press.Sent && v.Fired[0] - before == 1 && v.Other[0] == otherBefore && press.Ran == 0 && Plugin.TestSortBy == SortCriterion.Name,
                        $"the game's {v.What} controller button ({key}) still fires, and only it ({v.Fired[0] - before} time(s), the other game button "
                        + $"{v.Other[0] - otherBefore} time(s), Sort ran {press.Ran} time(s), sort order {Plugin.TestSortBy})");
                }
                c.Check(pressed > 0, "at least one of the game's two controller buttons could be pressed in this test");
            }
            c.Report();
        }
        finally
        {
            rig?.Done();
        }
    }

    // ================================================================ sortchest.layout (T13)

    // Tooltip the game builds when the pointer comes over the button: its two texts.
    private static bool Hover(SortChestUi.TestView view, out string topic, out string text)
    {
        topic = null;
        text = null;
        var tip = view != null ? view.Tip : null;
        var canvas = view != null && view.Go != null ? view.Go.GetComponentInParent<Canvas>() : null;
        if (tip == null || tip.m_tooltipPrefab == null || canvas == null)
        {
            return false;
        }
        UITooltip.HideTooltip();
        tip.OnHoverStart(view.Go);
        var name = tip.m_tooltipPrefab.name + "(Clone)";
        Transform shown = null;
        foreach (Transform child in canvas.transform)
        {
            if (child.name == name)
            {
                shown = child; // last one = the one just made
            }
        }
        if (shown != null)
        {
            var topicText = Utils.FindChild(shown, "Topic");
            var bodyText = Utils.FindChild(shown, "Text");
            var topicTmp = topicText != null ? topicText.GetComponent<TMP_Text>() : null;
            var bodyTmp = bodyText != null ? bodyText.GetComponent<TMP_Text>() : null;
            topic = topicTmp != null ? topicTmp.text : null;
            text = bodyTmp != null ? bodyTmp.text : null;
        }
        UITooltip.HideTooltip();
        return shown != null;
    }

    private static IEnumerator RunLayout()
    {
        const string N = LayoutTest;
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        Rig rig = null;
        try
        {
            rig = new Rig();
            var chest = rig.Spawn(Chest);
            if (!c.Check(chest != null && chest.GetInventory() != null, $"setup: {Chest}"))
            {
                c.Report();
                yield break;
            }
            // Tooltips.
            SetSortBy(SortCriterion.Biome);
            var box = new Box();
            yield return Open(rig, chest, box);
            if (!c.Check(box.Ok, $"the Chest panel opens ({box.Detail})"))
            {
                c.Report();
                yield break;
            }
            var sort = SortChestUi.TestSort;
            var criterion = SortChestUi.TestCriterion;
            var sortText = SortTipStart + "by biome.";
            c.Check(sort != null && sort.Tip != null && sort.Tip.m_topic == SortTopic && sort.Tip.m_text == sortText,
                $"Sort tooltip '{SortTopic}' / '{sortText}' (got '{(sort != null && sort.Tip != null ? sort.Tip.m_topic + "' / '" + sort.Tip.m_text : null)}')");
            c.Check(criterion != null && criterion.Tip != null && criterion.Tip.m_topic == CriterionTopic && criterion.Tip.m_text == CriterionTip,
                $"sort order tooltip '{CriterionTopic}' / '{CriterionTip}' (got '{(criterion != null && criterion.Tip != null ? criterion.Tip.m_topic + "' / '" + criterion.Tip.m_text : null)}')");
            if (Hover(sort, out var topic, out var text))
            {
                c.Check(topic == SortTopic && text == sortText, $"hovering Sort builds the game's tooltip with these texts ('{topic}' / '{text}')");
            }
            else
            {
                c.Check(false, "hovering Sort builds no tooltip object");
            }
            if (Hover(criterion, out topic, out text))
            {
                c.Check(topic == CriterionTopic && text == CriterionTip, $"hovering the sort order button builds the game's tooltip with these texts ('{topic}' / '{text}')");
            }
            else
            {
                c.Check(false, "hovering the sort order button builds no tooltip object");
            }
            yield return Close(rig);

            // Chest, Karve, Black metal chest, Drakkar with the usual inventory rows.
            yield return CheckContainer(rig, c, chest, "Chest", N, false);
            var blackmetal = rig.Spawn("piece_chest_blackmetal");
            if (c.Check(blackmetal != null && blackmetal.GetInventory() != null, "Black metal chest: prefab 'piece_chest_blackmetal' exists and has a container"))
            {
                yield return CheckContainer(rig, c, blackmetal, "Black metal chest", N, false);
            }
            foreach (var ship in new[] { ("Karve", "Karve"), ("VikingShip_Ashlands", "Drakkar") })
            {
                var storage = rig.Spawn(ship.Item1, Place.Vehicle);
                if (!c.Check(storage != null && storage.GetInventory() != null, $"{ship.Item2}: prefab '{ship.Item1}' exists and has a container"))
                {
                    continue;
                }
                yield return CheckContainer(rig, c, storage, ship.Item2, N, false);
                rig.Destroy(storage);
                yield return null;
            }

            // Two extra inventory rows (console: inventorysize 6).
            var rows = rig.P.GetInventory().GetHeight();
            var more = rows == 4 ? 6 : Mathf.Min(rows + 2, 9);
            if (rows != 4)
            {
                c.Note($"the test character has {rows} inventory rows, not 4: extra rows checked with {more}");
            }
            rig.SetRows(more);
            yield return null;
            yield return CheckContainer(rig, c, chest, "Chest", N, false);
            if (blackmetal != null && blackmetal.GetInventory() != null)
            {
                yield return CheckContainer(rig, c, blackmetal, "Black metal chest", N, false);
            }
            c.Report();
        }
        finally
        {
            rig?.Done();
        }
    }

    // ================================================================ sortchest.toggle (T15, T18 without config)

    private static bool ShowPatched()
    {
        var method = AccessTools.Method(typeof(InventoryGui), nameof(InventoryGui.Show), new[] { typeof(Container), typeof(int) });
        var info = method != null ? Harmony.GetPatchInfo(method) : null;
        return info != null && info.Postfixes.Any(p => p.owner == ModInfo.Guid);
    }

    private static int OurObjects(InventoryGui gui) =>
        gui.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith(SortChestUi.TestNamePrefix, StringComparison.Ordinal));

    private static bool ButtonsShown()
    {
        var sort = SortChestUi.TestSort;
        var criterion = SortChestUi.TestCriterion;
        return sort != null && criterion != null && sort.Go.activeInHierarchy && criterion.Go.activeInHierarchy;
    }

    private static IEnumerator RunToggle()
    {
        const string N = ToggleTest;
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        Rig rig = null;
        var plugin = Self();
        try
        {
            rig = new Rig();
            var gui = rig.Gui;
            var chest = rig.Spawn(Chest);
            if (!c.Check(plugin != null && plugin.IsActive && chest != null && chest.GetInventory() != null && Cap(chest.GetInventory()) >= 6,
                    $"setup: mod active and {Chest} with at least 6 slots"))
            {
                c.Report();
                yield break;
            }
            var inv = chest.GetInventory();
            var stacks = new Dictionary<string, int> { { "Wood", 4 }, { "Stone", 3 }, { "Resin", 2 }, { "Flint", 1 } };
            Fill(c, chest, Scatter(Cap(inv), stacks.Select(kv => (kv.Key, kv.Value)).ToArray()));
            var want = Expect(inv, ByName(stacks.Keys.ToArray()).Select(p => Cell(p, stacks[p])));
            var box = new Box();
            yield return Open(rig, chest, box);
            if (!c.Check(box.Ok && ButtonsShown() && ShowPatched(), $"setup: Chest open with the Sort buttons ({box.Detail})"))
            {
                c.Report();
                yield break;
            }
            var slots = Slots(inv);
            var rev = Rev(chest);

            // Off with the chest open (the steps the framework runs when Enabled goes false; no setting written).
            var sortGo = SortChestUi.TestSort.Go;
            var criterionGo = SortChestUi.TestCriterion.Go;
            c.Check(plugin.TestTurnOff(), "setup: feature turned off");
            c.Check(!sortGo.activeSelf && !criterionGo.activeSelf && SortChestUi.TestSort == null, "off: both buttons disappear at once");
            yield return null;
            yield return null;
            c.Check(sortGo == null && criterionGo == null && OurObjects(gui) == 0, $"off: the buttons are destroyed, nothing of ours left on the screen ({OurObjects(gui)} objects)");
            c.Check(!ShowPatched(), "off: our patch on the inventory screen is removed");
            c.Check(ReferenceEquals(gui.m_currentContainer, chest) && gui.m_container.gameObject.activeInHierarchy && Same(Slots(inv), slots) && Rev(chest) == rev,
                "off: the chest stays open and untouched");
            c.Check(gui.m_takeAllButton.gameObject.activeInHierarchy && gui.m_stackAllButton.gameObject.activeInHierarchy, "off: Take all and Place stacks still there");
            SelfTest.Screenshot(N, "off");
            yield return null;
            yield return null;

            // T18: off with the inventory closed, then a chest opened: the game's own panel.
            yield return Close(rig);
            yield return Open(rig, chest, box);
            c.Check(box.Ok && OurObjects(gui) == 0 && gui.m_takeAllButton.gameObject.activeInHierarchy && gui.m_stackAllButton.gameObject.activeInHierarchy,
                $"off: a chest opened now shows the game's own panel, no Sort buttons ({OurObjects(gui)} objects)");
            yield return Close(rig);

            // On with the inventory closed, then a chest opened: buttons back.
            c.Check(plugin.TestTurnOn(), "setup: feature turned on");
            yield return null;
            yield return Open(rig, chest, box);
            c.Check(box.Ok && ButtonsShown() && ShowPatched(), $"on again: a chest opened now shows the Sort buttons ({box.Detail})");

            // T15 second half: off and on again while the chest stays open.
            c.Check(plugin.TestTurnOff(), "setup: feature turned off with the chest open");
            yield return null;
            yield return null;
            c.Check(OurObjects(gui) == 0 && ReferenceEquals(gui.m_currentContainer, chest), "off again: buttons gone, chest still open");
            c.Check(plugin.TestTurnOn(), "setup: feature turned on with the chest open");
            c.Check(ButtonsShown() && ReferenceEquals(gui.m_currentContainer, chest), "on: the buttons appear at once, without reopening the chest");
            yield return null;
            yield return null;
            c.Check(ButtonsShown() && ShowPatched(), "on: the buttons stay, our patch is back");
            SetSortBy(SortCriterion.Name);
            var click = ClickSort(rig, chest);
            c.Check(click.Sent && click.Sorted && Same(Slots(inv), want), $"on: Sort works ({click}) {Show(Slots(inv))}");
            SelfTest.Screenshot(N, "on-again");
            yield return null;
            yield return null;
            c.Report();
        }
        finally
        {
            try
            {
                if (plugin != null)
                {
                    plugin.TestTurnOn();
                }
            }
            catch (Exception e)
            {
                Log.Error($"Self-test could not turn the feature back on: {e}");
            }
            rig?.Done();
        }
    }

    // ================================================================ sortchest.owner (M03, M05 stand-in)

    private static IEnumerator RunOwner()
    {
        const string N = OwnerTest;
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        Rig rig = null;
        try
        {
            rig = new Rig();
            var gui = rig.Gui;
            var chest = rig.Spawn(Chest);
            if (!c.Check(chest != null && chest.GetInventory() != null && Cap(chest.GetInventory()) >= 6, $"setup: {Chest} with at least 6 slots"))
            {
                c.Report();
                yield break;
            }
            var inv = chest.GetInventory();
            var stacks = new Dictionary<string, int> { { "Wood", 4 }, { "Stone", 3 }, { "Resin", 2 }, { "Flint", 1 } };
            Fill(c, chest, Scatter(Cap(inv), stacks.Select(kv => (kv.Key, kv.Value)).ToArray()));
            var want = Expect(inv, ByName(stacks.Keys.ToArray()).Select(p => Cell(p, stacks[p])));
            SetSortBy(SortCriterion.Name);
            var box = new Box();
            yield return Open(rig, chest, box);
            if (!c.Check(box.Ok, $"the Chest panel opens ({box.Detail})"))
            {
                c.Report();
                yield break;
            }
            var zdo = Zdo(chest);
            var me = ZDOMan.GetSessionID();
            var other = me + 7919L; // no player has this id: stands for the other player

            // M03: while open (and sorted) the chest is "in use": another player's request is refused.
            var click = ClickSort(rig, chest);
            c.Check(click.Sent && click.Sorted && Same(Slots(inv), want), $"Sort works on the open chest ({click})");
            c.Check(chest.IsInUse() && zdo.GetInt(ZDOVars.s_inUse) == 1, "open and sorted: the chest is still marked in use for everyone");
            chest.RPC_RequestOpen(other, 0L);
            c.Check(zdo.GetOwner() == me && chest.IsOwner(), "another player's request to open it now is refused (the game's 'in use' answer): the chest stays ours");

            // M05: the chest is given to another player while our panel is open (ship: the player aboard).
            c.Check(Unsort(inv), "setup: two Chest items swapped by hand");
            var mixed = Slots(inv);
            var bytes = StoredBytes(chest);
            ZDOMan.instance.m_releaseZDOTimer = 0f; // the game hands ownerless things back every 2 s: not during these checks
            zdo.SetOwner(other);
            var lost = ClickSort(rig, chest);
            c.Check(lost.Sent && lost.Outcome == ContainerSorter.SortOutcome.NotOwner && !lost.Saved && lost.Sounds == 0,
                $"a Sort click right after the chest went to another player is refused ({lost})");
            c.Check(Same(Slots(inv), mixed) && SameBytes(bytes, StoredBytes(chest)), "and changes nothing, here or in the saved data");
            yield return null;
            yield return null;
            c.Check(!chest.IsOwner(), "setup: the chest is still the other player's two frames later");
            // Panel closing itself then = game rule, checked alone in sortchest.owner-panel (MultiUserChest keeps it open).
            c.Note($"chest another player's for two frames: panel shown {gui.m_container.gameObject.activeInHierarchy}, buttons shown {ButtonsShown()}");
            // Whatever the panel does: a second click while the chest is not ours is refused too.
            var still = ClickSort(rig, chest);
            c.Check(!still.Sent || (still.Outcome == ContainerSorter.SortOutcome.NotOwner && !still.Saved),
                $"a Sort click two frames later is still refused, or the button is gone ({still})");
            c.Check(Same(Slots(inv), mixed) && SameBytes(bytes, StoredBytes(chest)), "nothing lost or duplicated meanwhile");

            // Ours again: Sort works once more.
            zdo.SetOwner(me);
            var back = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < back && !(gui.m_container.gameObject.activeInHierarchy && ButtonsShown()))
            {
                yield return null;
            }
            yield return null;
            if (c.Check(gui.m_container.gameObject.activeInHierarchy && ButtonsShown(), "ours again with the inventory still open: the panel and the Sort buttons are shown"))
            {
                click = ClickSort(rig, chest);
                c.Check(click.Sent && click.Sorted && Same(Slots(inv), want), $"ours again: Sort works ({click})");
            }

            // M03 second half: after we close, the other player's request is granted and gets the sorted content.
            yield return Close(rig);
            c.Check(!chest.IsInUse() && zdo.GetInt(ZDOVars.s_inUse) == 0, "closed: the chest is no longer in use");
            ZDOMan.instance.m_releaseZDOTimer = 0f;
            chest.RPC_RequestOpen(other, 0L);
            c.Check(zdo.GetOwner() == other, "after closing, another player's request to open is granted (the chest is handed to that player)");
            zdo.SetOwner(me);
            var stored = Decode(chest, StoredBytes(chest));
            c.Check(stored != null && Totals(stored) == Totals(inv), $"what that player gets holds everything ({(stored != null ? Totals(stored) : "nothing")})");
            c.Note($"content handed over: {Show(stored != null ? Slots(stored) : null)}");
            c.Report();
        }
        finally
        {
            rig?.Done();
        }
    }

    // ================================================================ sortchest.stale (M06 stand-in)

    private static IEnumerator RunStale()
    {
        const string N = StaleTest;
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        Rig rig = null;
        try
        {
            rig = new Rig();
            var gui = rig.Gui;
            var chest = rig.Spawn(Chest);
            if (!c.Check(chest != null && chest.GetInventory() != null && Cap(chest.GetInventory()) >= 8, $"setup: {Chest} with at least 8 slots"))
            {
                c.Report();
                yield break;
            }
            var inv = chest.GetInventory();
            var cap = Cap(inv);
            Fill(c, chest, (cap - 1, "Wood", 4), (0, "Stone", 3), (cap - 2, "Resin", 2), (2, "Flint", 1));
            SetSortBy(SortCriterion.Name);
            var box = new Box();
            yield return Open(rig, chest, box);
            if (!c.Check(box.Ok, $"the Chest panel opens ({box.Detail})"))
            {
                c.Report();
                yield break;
            }
            var zdo = Zdo(chest);
            var me = ZDOMan.GetSessionID();
            var other = me + 7919L;
            var local = Slots(inv);

            // What the other player leaves: the Wood taken, Coal added in the last free slot.
            var theirs = Decode(chest, StoredBytes(chest));
            var coal = Make("Coal", 4);
            if (!c.Check(theirs != null && theirs.NrOfItems() == 4 && coal != null, "setup: the saved content can be read and changed"))
            {
                c.Report();
                yield break;
            }
            var taken = theirs.GetAllItems().First(i => Prefab(i) == "Wood");
            theirs.GetAllItems().Remove(taken);
            coal.m_gridPos = new Vector2i((cap - 1) % inv.GetWidth(), (cap - 1) / inv.GetWidth());
            theirs.GetAllItems().Add(coal);
            var theirBytes = Bytes(theirs);
            var theirSlots = Slots(theirs);
            var theirTotals = Totals(theirs);

            // Hand-off while open, then the player closes the inventory: the chest's in-use mark stays stuck, so the
            // game never reloads it. The other player changes the content. Then the chest is ours again.
            ZDOMan.instance.m_releaseZDOTimer = 0f;
            zdo.SetOwner(other);
            gui.Hide();
            zdo.Set(ZDOVars.s_items, theirBytes);
            zdo.SetOwner(me);
            c.Check(chest.IsInUse(), "setup: the in-use mark is stuck after the hand-off");
            yield return new WaitForSecondsRealtime(1.3f);
            c.Check(Same(Slots(inv), local), $"setup: our copy of the chest is now out of date ({Show(Slots(inv))})");

            yield return Open(rig, chest, box);
            if (!c.Check(box.Ok, $"the chest opens again from our side ({box.Detail})"))
            {
                c.Report();
                yield break;
            }
            var center = MessageHud.instance != null ? MessageHud.instance.m_messageCenterText : null;
            if (center != null)
            {
                center.text = "";
            }
            var stale = ClickSort(rig, chest);
            c.Check(stale.Sent && stale.Outcome == ContainerSorter.SortOutcome.Stale && !stale.Saved && stale.Sounds == 0,
                $"Sort on the out-of-date copy is refused ({stale})");
            if (center != null && !Hud.IsUserHidden())
            {
                c.Check(center.text == ContainerSorter.StaleMessage, $"center message '{ContainerSorter.StaleMessage}' (got '{center.text}')");
            }
            else
            {
                c.Note("HUD hidden or no message object: center message not checked");
            }
            c.Check(SameBytes(StoredBytes(chest), theirBytes), "the saved content is still the other player's: the taken Wood does not come back, the added Coal stays");
            c.Check(Same(Slots(inv), local), "our copy is not changed either");
            SelfTest.Screenshot(N, "refused");
            yield return null;
            yield return null;

            // Close and reopen: the game loads the current content, Sort works.
            yield return Close(rig);
            c.Check(!chest.IsInUse(), "closing as the owner clears the in-use mark");
            yield return new WaitForSecondsRealtime(1.4f);
            yield return Open(rig, chest, box);
            inv = chest.GetInventory();
            c.Check(box.Ok && Same(Slots(inv), theirSlots), $"after closing and reopening we see the current content: {Show(Slots(inv))}, expected {Show(theirSlots)}");
            if (center != null)
            {
                center.text = "";
            }
            var click = ClickSort(rig, chest);
            var want = Expect(inv, ByName("Stone", "Resin", "Flint", "Coal").Select(p => Cell(p, p == "Stone" ? 3 : p == "Resin" ? 2 : p == "Coal" ? 4 : 1)));
            c.Check(click.Sent && click.Sorted && Same(Slots(inv), want), $"and Sort works: {Show(Slots(inv))}, expected {Show(want)} ({click})");
            c.Check(Totals(inv) == theirTotals, $"with exactly the other player's content ({Totals(inv)}, expected {theirTotals})");
            c.Check(center == null || center.text != ContainerSorter.StaleMessage, "no 'changed elsewhere' message this time");
            c.Report();
        }
        finally
        {
            rig?.Done();
        }
    }

    // ================================================================ sortchest.pad-late (M07 stand-in)

    // First opening of a session on a chest another player owned: the game's answer opens the screen before the chest
    // is ours, the panel (and our buttons' controller parts) come alive only when it is.
    private static IEnumerator RunPadLate(string name, float delay, bool expectGroup)
    {
        if (!Ready(name))
        {
            yield break;
        }
        var c = new Checks(name);
        Rig rig = null;
        try
        {
            rig = new Rig();
            var gui = rig.Gui;
            var chest = rig.Spawn(Chest);
            if (!c.Check(chest != null && chest.GetInventory() != null && Cap(chest.GetInventory()) >= 6, $"setup: {Chest} with at least 6 slots"))
            {
                c.Report();
                yield break;
            }
            var inv = chest.GetInventory();
            var stacks = new Dictionary<string, int> { { "Wood", 4 }, { "Stone", 3 }, { "Resin", 2 }, { "Flint", 1 } };
            Fill(c, chest, Scatter(Cap(inv), stacks.Select(kv => (kv.Key, kv.Value)).ToArray()));
            var want = Expect(inv, ByName(stacks.Keys.ToArray()).Select(p => Cell(p, stacks[p])));
            var mixed = Slots(inv);
            SortChestUi.TestRemake(gui); // fresh buttons, like the first opening after joining
            SetSortBy(SortCriterion.Name);
            var zdo = Zdo(chest);
            var me = ZDOMan.GetSessionID();
            zdo.SetOwner(me + 7919L);
            gui.Show(chest);
            var until = Time.realtimeSinceStartup + delay;
            var early = false;
            while (Time.realtimeSinceStartup < until)
            {
                ZDOMan.instance.m_releaseZDOTimer = 0f; // the game must not hand the chest back before the test does
                early |= gui.m_container.gameObject.activeInHierarchy;
                yield return null;
            }
            // Game alone: panel hidden while the chest is not ours. A mod that shows it anyway (MultiUserChest) = no late
            // panel can be made here; sortchest.owner-panel is the check of that game rule. Rest still run then (fresh
            // buttons, first opening), and the note say which case this run was.
            var late = !early && !chest.IsOwner();
            var keeper = PanelKeeper();
            c.Note(late
                ? $"the panel stayed hidden for {delay:F1} s while the chest was another player's, then the chest became ours"
                : $"the panel did NOT stay hidden while the chest was another player's (shown early {early}, chest ours {chest.IsOwner()}"
                  + $"{(keeper != null ? "; " + keeper + " is installed and shows the panel of a chest this game does not own" : "")}): "
                  + "this run only checks fresh buttons on a first opening, not a late panel");
            zdo.SetOwner(me);
            var end = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < end && !gui.m_container.gameObject.activeInHierarchy)
            {
                yield return null;
            }
            if (!c.Check(gui.m_container.gameObject.activeInHierarchy && ButtonsShown(), "the panel and the Sort buttons show once the chest is ours"))
            {
                c.Report();
                yield break;
            }
            // Time for the mod's deferred work (it waits for the panel, then one more frame).
            yield return new WaitForSecondsRealtime(1f);
            var sort = SortChestUi.TestSort;
            var criterion = SortChestUi.TestCriterion;
            var group = SortChestUi.TestContainerGroup(gui);
            if (!c.Check(sort != null && criterion != null && sort.Pad != null && criterion.Pad != null && group != null,
                    "both buttons have a controller part and the chest grid has a focus group"))
            {
                c.Report();
                yield break;
            }
            var natural = sort.Pad.GetComponentInParent<UIGroupHandler>();
            c.Note($"controller parts: Sort in group '{(sort.Pad.m_group != null ? sort.Pad.m_group.name : "none")}', sort order in "
                   + $"'{(criterion.Pad.m_group != null ? criterion.Pad.m_group.name : "none")}', chest grid group '{group.name}', group the game "
                   + $"gives them by itself '{(natural != null ? natural.name : "none")}'; the game's controller keys: {GameKeys(gui)}");
            if (expectGroup)
            {
                c.Check(ReferenceEquals(sort.Pad.m_group, group) && ReferenceEquals(criterion.Pad.m_group, group),
                    "both controller parts belong to the chest grid group from this first opening");
            }

            // View/Select acts only with the chest grid focused. (Left stick click: sortchest.bug.pad-order-key; never
            // pressed here, it is the game's Take all key.)
            var press = new Clicked();
            if (c.Check(GameButtonsOn(gui, SortChestUi.SortKey).Count == 0, "no game button of the panel listens to View/Select (no key pressed otherwise)"))
            {
                if (gui.m_playerGrid != null && gui.m_playerGrid.m_uiGroup != null)
                {
                    yield return Focus(rig, gui.m_playerGrid.m_uiGroup);
                    yield return PressKey(rig, chest, SortChestUi.SortKey, press);
                    c.Check(press.Sent && press.Ran == 0 && Same(Slots(inv), mixed), $"own inventory focused: View/Select does nothing to the chest (Sort ran {press.Ran} time(s))");
                }
                yield return Focus(rig, group);
                yield return PressKey(rig, chest, SortChestUi.SortKey, press);
                c.Check(press.Sent && press.Ran == 1 && press.Sorted && Same(Slots(inv), want), $"chest grid focused: View/Select sorts ({press}) {Show(Slots(inv))}");
            }

            // Sort glyph only with the chest grid focused (controller as input device).
            rig.ForceGamepad();
            yield return null;
            yield return null;
            if (c.Check(sort.Hint != null && ZInput.IsGamepadActive(),
                    $"Sort has a glyph object, controller as input device (controller enabled in the game settings: {ZInput.IsGamepadEnabled()})"))
            {
                yield return Focus(rig, group);
                c.Check(sort.Hint.activeInHierarchy, "chest grid focused: the Sort glyph is shown");
                if (gui.m_playerGrid != null && gui.m_playerGrid.m_uiGroup != null)
                {
                    yield return Focus(rig, gui.m_playerGrid.m_uiGroup);
                    c.Check(!sort.Hint.activeInHierarchy, "own inventory focused: no glyph on Sort");
                }
            }
            c.Report();
        }
        finally
        {
            rig?.Done();
        }
    }

    // ================================================================ sortchest.owner-panel (M05 panel part; M07 premise)

    // Game rule the ownership stand-ins rest on: the container panel shows only while this game owns the chest. Alone
    // in its own test. Other mod keeps the panel open (MultiUserChest, README names it; or any not-MC patch on the
    // game's panel update): rule cannot show, test say so in a NOTE and check the README line for that case (Sort do
    // nothing on a chest that is not ours). Panel stays open with no such mod = fail.
    private static IEnumerator RunOwnerPanel()
    {
        const string N = OwnerPanelTest;
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        Rig rig = null;
        try
        {
            rig = new Rig();
            var gui = rig.Gui;
            var chest = rig.Spawn(Chest);
            if (!c.Check(chest != null && chest.GetInventory() != null && Cap(chest.GetInventory()) >= 6, $"setup: {Chest} with at least 6 slots"))
            {
                c.Report();
                yield break;
            }
            var inv = chest.GetInventory();
            var stacks = new Dictionary<string, int> { { "Wood", 4 }, { "Stone", 3 }, { "Resin", 2 }, { "Flint", 1 } };
            Fill(c, chest, Scatter(Cap(inv), stacks.Select(kv => (kv.Key, kv.Value)).ToArray()));
            var want = Expect(inv, ByName(stacks.Keys.ToArray()).Select(p => Cell(p, stacks[p])));
            SetSortBy(SortCriterion.Name);
            var box = new Box();
            yield return Open(rig, chest, box);
            if (!c.Check(box.Ok && ButtonsShown(), $"setup: Chest open with the Sort buttons ({box.Detail})"))
            {
                c.Report();
                yield break;
            }
            var zdo = Zdo(chest);
            var me = ZDOMan.GetSessionID();
            var mixed = Slots(inv);
            var bytes = StoredBytes(chest);
            // Who can change the game rule: the mod the README names, or any mod (not MC) patching the game method
            // that shows / hides the panel each frame.
            var keeper = PanelKeeper();
            var patchers = PanelUpdatePatchers();
            var foreign = patchers.Where(o => !o.StartsWith("MC.", StringComparison.Ordinal)).ToList();

            // The chest is given to another player while our panel is open (ship: the player aboard).
            ZDOMan.instance.m_releaseZDOTimer = 0f; // the game hands ownerless things back every 2 s: not during these checks
            zdo.SetOwner(me + 7919L);
            yield return null;
            yield return null;
            c.Check(!chest.IsOwner(), "setup: the chest is still the other player's two frames later");
            var panelShown = gui.m_container.gameObject.activeInHierarchy;
            var sort = SortChestUi.TestSort;
            var criterion = SortChestUi.TestCriterion;
            c.Note($"chest another player's for two frames: panel shown {panelShown}, buttons shown {ButtonsShown()}; installed mod the README names as "
                   + $"keeping that panel open: {keeper ?? "none"}; other mods patching the game method that hides it (InventoryGui.UpdateContainer): "
                   + $"{(patchers.Count > 0 ? string.Join(", ", patchers.ToArray()) : "none")}");
            if (!panelShown)
            {
                // Game rule seen (M05): panel gone, buttons with it.
                c.Check(sort != null && criterion != null && !sort.Go.activeInHierarchy && !criterion.Go.activeInHierarchy,
                    "the container panel closed itself when the chest went to another player, and the Sort buttons go with it");
            }
            else if (keeper != null || foreign.Count > 0)
            {
                // Other mod keeps the panel of a chest this game no own open: game rule cannot show in this run. Then the
                // README line for MultiUserChest must hold: Sort does nothing for the player who does not own the chest.
                var by = keeper ?? string.Join(", ", foreign.ToArray());
                c.Note($"{by} keeps the panel open, so the game's own rule (panel closes itself, M05) was NOT seen in this run; checked instead what the "
                       + "README says for MultiUserChest: Sort does nothing for a player who does not own the chest at that moment");
                var refused = ClickSort(rig, chest);
                c.Check(!refused.Sent || (refused.Outcome == ContainerSorter.SortOutcome.NotOwner && !refused.Written && !refused.Saved && refused.Sounds == 0),
                    $"panel kept open by {by}: a Sort click on the chest that is not ours does nothing ({refused})");
            }
            else
            {
                c.Check(false, "the container panel closes itself when the chest goes to another player (it stayed shown, and no installed mod that "
                               + "patches the game's panel update explains it)");
            }
            c.Check(Same(Slots(inv), mixed) && SameBytes(bytes, StoredBytes(chest)), "nothing lost or duplicated meanwhile");

            // Ours again with the inventory still open: panel and buttons back, Sort works.
            zdo.SetOwner(me);
            var end = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < end && !(gui.m_container.gameObject.activeInHierarchy && ButtonsShown()))
            {
                yield return null;
            }
            yield return null;
            if (c.Check(gui.m_container.gameObject.activeInHierarchy && ButtonsShown(), "ours again: the panel and the Sort buttons come back"))
            {
                var click = ClickSort(rig, chest);
                c.Check(click.Sent && click.Sorted && Same(Slots(inv), want), $"ours again: Sort works ({click}) {Show(Slots(inv))}");
            }
            c.Report();
        }
        finally
        {
            rig?.Done();
        }
    }

    // ================================================================ sortchest.crossbow (C01)

    private static IEnumerator RunCrossbow()
    {
        const string N = CrossbowTest;
        const string key = "MC.Combat.Crossbow.StaysLoaded.Loaded"; // LoadedState.Key of Crossbow Stays Loaded
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        Rig rig = null;
        try
        {
            rig = new Rig();
            var chest = rig.Spawn(Chest);
            if (!c.Check(chest != null && chest.GetInventory() != null && Cap(chest.GetInventory()) >= 6, $"setup: {Chest} with at least 6 slots"))
            {
                c.Report();
                yield break;
            }
            var inv = chest.GetInventory();
            var cap = Cap(inv);
            var items = Fill(c, chest, (cap - 1, "CrossbowArbalest", 1), (2, "CrossbowArbalest", 1), (cap - 2, "Wood", 5), (0, "BoltBone", 10));
            if (!c.Check(items.All(i => i != null), "setup: two CrossbowArbalest, Wood and BoltBone"))
            {
                c.Report();
                yield break;
            }
            var loaded = items[0];
            var empty = items[1];
            // Same stamp Crossbow Stays Loaded writes (LoadedState.Mark): "v1:" + durability.
            var stamp = "v1:" + loaded.m_durability.ToString("R", CultureInfo.InvariantCulture);
            loaded.m_customData[key] = stamp;
            inv.Changed();
            var isLoaded = LoadedCheck();
            if (isLoaded != null)
            {
                c.Check(isLoaded(loaded) && !isLoaded(empty), "setup: Crossbow Stays Loaded reads one crossbow as loaded, the other as not");
            }
            else
            {
                c.Note("Crossbow Stays Loaded is not loaded in this run: only the stamp itself is checked");
            }
            SetSortBy(SortCriterion.Name);
            Plugin.TestMergeStacks = true;
            var box = new Box();
            yield return Open(rig, chest, box);
            if (!c.Check(box.Ok, $"the Chest panel opens ({box.Detail})"))
            {
                c.Report();
                yield break;
            }
            var click = ClickSort(rig, chest);
            c.Check(click.Sent && click.Sorted && click.Merged == 0, $"Sort works, nothing merged ({click})");
            c.Check(inv.NrOfItems() == 4 && inv.ContainsItem(loaded) && inv.ContainsItem(empty) && loaded.m_stack == 1 && empty.m_stack == 1,
                "two separate crossbows, the same item objects");
            c.Check(loaded.m_customData.TryGetValue(key, out var value) && value == stamp && loaded.m_customData.Count == 1 && empty.m_customData.Count == 0,
                "the loaded stamp is still on the loaded crossbow only, unchanged");
            if (isLoaded != null)
            {
                c.Check(isLoaded(loaded) && !isLoaded(empty), "Crossbow Stays Loaded still reads the same crossbow as loaded");
            }
            var a = Mathf.Abs(loaded.m_gridPos.y * inv.GetWidth() + loaded.m_gridPos.x - (empty.m_gridPos.y * inv.GetWidth() + empty.m_gridPos.x));
            c.Check(a == 1, "the two crossbows end next to each other");
            var stored = Decode(chest, StoredBytes(chest));
            var bows = stored != null ? stored.GetAllItems().Where(i => Prefab(i) == "CrossbowArbalest").ToList() : new List<ItemDrop.ItemData>();
            c.Check(bows.Count == 2 && bows.Count(i => i.m_customData.TryGetValue(key, out var v) && v == stamp) == 1
                    && bows.Count(i => i.m_customData.Count == 0) == 1,
                "the saved chest holds one stamped and one plain crossbow");
            if (isLoaded != null && bows.Count == 2)
            {
                c.Check(bows.Count(i => isLoaded(i)) == 1, "and Crossbow Stays Loaded reads exactly one of the saved crossbows as loaded");
            }
            c.Report();
        }
        finally
        {
            rig?.Done();
        }
    }

    private static Type OtherType(string assembly, string type)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.GetName().Name == assembly)
            {
                return asm.GetType(type, false);
            }
        }
        return null;
    }

    // LoadedState.IsLoaded of Crossbow Stays Loaded when that mod is in the game. Null = not there.
    private static Func<ItemDrop.ItemData, bool> LoadedCheck()
    {
        try
        {
            var type = OtherType("MC.Combat.Crossbow.StaysLoaded", "MC.Combat.CrossbowStaysLoadedMod.LoadedState");
            var method = type != null ? AccessTools.Method(type, "IsLoaded", new[] { typeof(ItemDrop.ItemData) }) : null;
            if (method == null || method.ReturnType != typeof(bool))
            {
                return null;
            }
            return item => (bool)method.Invoke(null, new object[] { item });
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ================================================================ sortchest.split (T19)

    private static IEnumerator RunSplit()
    {
        const string N = SplitTest;
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        Rig rig = null;
        try
        {
            rig = new Rig();
            var gui = rig.Gui;
            var chest = rig.Spawn(Chest);
            if (!c.Check(chest != null && chest.GetInventory() != null && Cap(chest.GetInventory()) >= 6 && gui.m_splitDialog != null,
                    $"setup: {Chest} with at least 6 slots, and the game's split dialog"))
            {
                c.Report();
                yield break;
            }
            var inv = chest.GetInventory();
            var cap = Cap(inv);
            // Two Wood stacks that a Sort would merge, and a Stone stack, unsorted.
            var items = Fill(c, chest, (cap - 1, "Wood", 20), (2, "Wood", 10), (cap - 2, "Stone", 5));
            if (!c.Check(items.All(i => i != null), "setup: Wood 20, Wood 10, Stone 5"))
            {
                c.Report();
                yield break;
            }
            var totals = Totals(inv);
            SetSortBy(SortCriterion.Name);
            Plugin.TestMergeStacks = true;
            var box = new Box();
            yield return Open(rig, chest, box);
            if (!c.Check(box.Ok, $"the Chest panel opens ({box.Detail})"))
            {
                c.Report();
                yield break;
            }
            var held = items[0];
            if (GamepadRumble.instance != null)
            {
                gui.ShowSplitDialog(held, inv); // what Shift+click on the stack calls
            }
            else
            {
                gui.m_splitDialog.UpdateLimits(held.m_stack, false);
                gui.m_splitDialog.SetActive(true);
                gui.m_splitItem = held;
                gui.m_splitInventory = inv;
            }
            yield return null;
            if (!c.Check(gui.m_splitDialog.IsActive && ReferenceEquals(gui.m_splitItem, held), "setup: the split dialog is open on the Wood stack of 20"))
            {
                c.Report();
                yield break;
            }
            var slots = Slots(inv);
            var rev = Rev(chest);
            SelfTest.Screenshot(N, "split-open");
            yield return null;
            yield return null;

            // Mouse click on Sort.
            var click = ClickSort(rig, chest);
            c.Check(!click.Sent || click.Outcome == ContainerSorter.SortOutcome.SplitDialog,
                $"a click on Sort with the split dialog open is refused ({click})");
            // The Sort action itself (a controller key that reaches it runs this).
            ContainerSorter.TestReset();
            ContainerSorter.TrySortOpenContainer();
            c.Check(ContainerSorter.LastOutcome == ContainerSorter.SortOutcome.SplitDialog, $"the Sort action is refused ({ContainerSorter.LastOutcome})");
            // View/Select with the chest grid focused.
            var group = SortChestUi.TestContainerGroup(gui);
            var press = new Clicked();
            if (group != null)
            {
                yield return Focus(rig, group);
                yield return PressKey(rig, chest, "JoyBack", press);
                c.Check(press.Sent && (press.Ran == 0 || press.Outcome == ContainerSorter.SortOutcome.SplitDialog),
                    $"View/Select with the split dialog open does not sort (Sort ran {press.Ran} time(s), {press.Outcome})");
            }
            c.Check(Same(Slots(inv), slots) && Rev(chest) == rev && inv.NrOfItems() == 3, $"no item moved, nothing merged, nothing saved: {Show(Slots(inv))}");
            c.Check(click.Sounds == 0 && press.Sounds == 0, "no move sound");
            c.Check(gui.m_splitDialog.IsActive && ReferenceEquals(gui.m_splitItem, held) && inv.ContainsItem(held) && held.m_stack == 20
                    && Mathf.Approximately(gui.m_splitDialog.m_splitSlider.maxValue, 20f),
                "the split dialog still refers to the same stack of 20");

            // Cancel: Sort works.
            gui.OnSplitCancel();
            yield return null;
            c.Check(!gui.m_splitDialog.IsActive, "setup: split dialog cancelled");
            click = ClickSort(rig, chest);
            var want = Expect(inv, ByName("Wood", "Stone").Select(p => p == "Wood" ? Cell("Wood", 30) : Cell("Stone", 5)));
            c.Check(click.Sent && click.Sorted && click.Merged == 1 && Same(Slots(inv), want), $"after cancelling, Sort merges and sorts: {Show(Slots(inv))} ({click})");

            // A split after that takes from the stack picked, and dropping it on an empty slot duplicates nothing.
            var wood = inv.GetAllItems().FirstOrDefault(i => Prefab(i) == "Wood");
            if (c.Check(wood != null && wood.m_stack == 30 && GamepadRumble.instance != null && gui.ContainerGrid != null,
                    "setup: the merged Wood stack of 30 can be split (the game's split dialog can be opened the way Shift+click opens it)"))
            {
                gui.ShowSplitDialog(wood, inv);
                yield return null;
                var amount = (int)gui.m_splitDialog.m_splitSlider.value;
                gui.OnSplitOk();
                c.Check(ReferenceEquals(gui.m_dragItem, wood) && ReferenceEquals(gui.m_dragInventory, inv) && gui.m_dragAmount == amount && amount > 0 && amount < 30,
                    $"a split after the sort drags {amount} from the stack picked");
                if (amount > 0 && amount < 30)
                {
                    // Drop on the last (empty) slot, the game's own way.
                    var free = new Vector2i(inv.GetWidth() - 1, inv.GetHeight() - 1);
                    var wasFree = inv.GetItemAt(free.x, free.y) == null;
                    var dropped = wasFree && gui.ContainerGrid.DropItem(inv, wood, amount, free);
                    gui.SetupDragItem(null, null, 1);
                    var part = inv.GetItemAt(free.x, free.y);
                    c.Check(dropped && wood.m_stack == 30 - amount && part != null && !ReferenceEquals(part, wood) && Prefab(part) == "Wood" && part.m_stack == amount
                            && inv.NrOfItems() == 3,
                        $"dropped on an empty slot: {30 - amount} stay in the stack picked, {amount} in the new slot ({Show(Slots(inv))})");
                }
                else
                {
                    gui.SetupDragItem(null, null, 1);
                }
                c.Check(Totals(inv) == totals, $"nothing duplicated ({Totals(inv)}, was {totals})");
            }
            c.Report();
        }
        finally
        {
            rig?.Done();
        }
    }

    // ================================================================ sortchest.noop-stale (T20, known wrong today)

    private static IEnumerator RunNoopStale()
    {
        const string N = NoopStaleTest;
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        Rig rig = null;
        try
        {
            rig = new Rig();
            var chest = rig.Spawn(Chest);
            if (!c.Check(chest != null && chest.GetInventory() != null && Cap(chest.GetInventory()) >= 6, $"setup: {Chest} with at least 6 slots"))
            {
                c.Report();
                yield break;
            }
            var inv = chest.GetInventory();
            var prefabs = new[] { "Wood", "AxeFlint", "Raspberry", "TrophyBoar" };
            Fill(c, chest, Scatter(Cap(inv), prefabs.Select(p => (p, 1)).ToArray()));
            SetSortBy(SortCriterion.Name);
            var box = new Box();
            yield return Open(rig, chest, box);
            if (!c.Check(box.Ok, $"the Chest panel opens ({box.Detail})"))
            {
                c.Report();
                yield break;
            }
            var first = ClickSort(rig, chest);
            var byName = Expect(inv, ByName(prefabs));
            if (!c.Check(first.Sent && first.Sorted && Same(Slots(inv), byName), $"setup: sorted by name ({first})"))
            {
                c.Report();
                yield break;
            }
            // What the game leaves after the stack of the first slot was dragged to the player inventory and back into
            // the same slot: same slots, but the item is now last in the chest's list, and that is saved.
            var list = inv.GetAllItems();
            var moved = inv.GetItemAt(0, 0);
            list.Remove(moved);
            list.Add(moved);
            inv.Changed();
            var center = MessageHud.instance != null ? MessageHud.instance.m_messageCenterText : null;
            if (center != null)
            {
                center.text = "";
            }

            var noop = ClickSort(rig, chest);
            c.Check(noop.Sent && noop.Outcome == ContainerSorter.SortOutcome.Done && !noop.Written && Same(Slots(inv), byName),
                $"first Sort click on the already sorted chest: nothing moves ({noop})");
            var second = ClickSort(rig, chest);
            c.Check(second.Sent && second.Outcome == ContainerSorter.SortOutcome.Done && !second.Written && !second.Saved,
                $"second Sort click: again 'already sorted', not refused ({second})");
            c.Check(center == null || center.text != ContainerSorter.StaleMessage, "no 'This container changed elsewhere' message after the second click");
            SetSortBy(SortCriterion.Type);
            var byType = Expect(inv, new[] { "AxeFlint", "Raspberry", "Wood", "TrophyBoar" });
            var third = ClickSort(rig, chest);
            c.Check(third.Sent && third.Sorted && Same(Slots(inv), byType),
                $"Sort after switching to By type sorts by type: {Show(Slots(inv))}, expected {Show(byType)} ({third})");
            c.Check(center == null || center.text != ContainerSorter.StaleMessage, "no 'This container changed elsewhere' message after the By type click");
            c.Report();
        }
        finally
        {
            rig?.Done();
        }
    }

    // ================================================================ sortchest.bug.tankard-group (T14 Tankard line, C03 "tankard last")

    // Known wrong today: the game's Tankard is a one-handed weapon with the torch animation, so the shared item kinds
    // call it a torch (Tools and light 5.2), not Misc / Other 10.0 as T14 and C03 expect.
    private static IEnumerator RunBugTankard()
    {
        const string N = BugTankardTest;
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        Rig rig = null;
        try
        {
            rig = new Rig();
            var chest = rig.Spawn(Chest);
            if (!c.Check(chest != null && chest.GetInventory() != null && Cap(chest.GetInventory()) >= 8 && Shared("Tankard") != null,
                    $"setup: {Chest} with at least 8 slots, and the item 'Tankard'"))
            {
                c.Report();
                yield break;
            }
            c.Note(TankardFacts());
            // Same rank the T14 Debug line prints ("-> 10.0 Other").
            c.Check(TypeRank("Tankard") == "10.0", $"T14: the Tankard is in the group Other with rank 10.0 (got {TypeRank("Tankard")})");
            var inv = chest.GetInventory();
            var items = new[] { "Wood", "Tankard", "MeadHealthMinor", "Torch", "Raspberry", "PickaxeAntler" };
            Fill(c, chest, Scatter(Cap(inv), items.Select(p => (p, 1)).ToArray()));
            SetSortBy(SortCriterion.Type);
            var box = new Box();
            yield return Open(rig, chest, box);
            if (c.Check(box.Ok, $"the Chest panel opens ({box.Detail})"))
            {
                var want = Expect(inv, new[] { "PickaxeAntler", "Torch", "Raspberry", "MeadHealthMinor", "Wood", "Tankard" });
                var click = ClickSort(rig, chest);
                c.Check(click.Sent && click.Sorted && Same(Slots(inv), want),
                    $"C03: pickaxe and torch next to each other, food, mead, material, tankard last: {Show(Slots(inv))}, expected {Show(want)} ({click})");
            }
            c.Report();
        }
        finally
        {
            rig?.Done();
        }
    }

    // ================================================================ sortchest.bug.pad-order-key (T12, M07, C02: left stick click)

    // Known wrong today: the game's Take all already listens to the left stick click (JoyLStick), also with the own
    // inventory focused. The mod then gives its key up: sort order button = mouse only, one Warning per button
    // creation, no glyph. A left stick click empties the chest into the inventory (Take all) instead of changing the
    // sort order. Me never press the key while a game button listens to it.
    private static IEnumerator RunBugPadOrderKey()
    {
        const string N = BugPadOrderKeyTest;
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        Rig rig = null;
        try
        {
            rig = new Rig();
            var gui = rig.Gui;
            var chest = rig.Spawn(Chest);
            if (!c.Check(chest != null && chest.GetInventory() != null && Cap(chest.GetInventory()) >= 6, $"setup: {Chest} with at least 6 slots"))
            {
                c.Report();
                yield break;
            }
            var inv = chest.GetInventory();
            var stacks = new Dictionary<string, int> { { "Wood", 4 }, { "Stone", 3 }, { "Resin", 2 }, { "Flint", 1 } };
            Fill(c, chest, Scatter(Cap(inv), stacks.Select(kv => (kv.Key, kv.Value)).ToArray()));
            var mixed = Slots(inv);
            SetSortBy(SortCriterion.Name);
            ForgetLayout(rig, chest);
            var box = new Box();
            yield return Open(rig, chest, box);
            if (!c.Check(box.Ok, $"the Chest panel opens ({box.Detail})"))
            {
                c.Report();
                yield break;
            }
            yield return WaitLayout(rig, chest, 1, box);
            var criterion = SortChestUi.TestCriterion;
            var group = SortChestUi.TestContainerGroup(gui);
            if (!c.Check(criterion != null && criterion.Pad != null && group != null, "the sort order button has a controller part and the chest grid a focus group"))
            {
                c.Report();
                yield break;
            }
            const string key = SortChestUi.CriterionKey;
            var takePad = PadOf(gui.m_takeAllButton);
            var stackPad = PadOf(gui.m_stackAllButton);
            c.Note($"the game's controller keys: {GameKeys(gui)}");
            var bound = criterion.Pad.m_zinputKey == key && criterion.Pad.m_keyCode == KeyCode.None;
            c.Check(bound, $"the sort order button listens to the left stick click (key '{criterion.Pad.m_zinputKey ?? "none"}', keyboard {criterion.Pad.m_keyCode})");
            var keyWarnings = Warnings(0, "already uses the controller key");
            c.Check(keyWarnings.Count == 0,
                $"no 'already uses the controller key' warning since game start ({keyWarnings.Count}): {(keyWarnings.Count > 0 ? keyWarnings[0] : "")}");
            c.Check((takePad == null || (takePad.m_zinputKey != key && takePad.m_zinputKey != SortChestUi.SortKey))
                    && (stackPad == null || (stackPad.m_zinputKey != key && stackPad.m_zinputKey != SortChestUi.SortKey)),
                "the game's Take all and Place stacks use other controller keys than the mod's two");

            // Chest grid focused: the left stick click changes the sort order only.
            yield return Focus(rig, group);
            var press = new Clicked();
            var busy = GameButtonsOn(gui, key);
            if (bound && busy.Count == 0)
            {
                yield return PressKey(rig, chest, key, press);
                c.Check(press.Sent && Plugin.TestSortBy == SortCriterion.Type && press.Ran == 0 && !press.Saved && Same(Slots(inv), mixed),
                    $"chest grid focused: the left stick click changes the sort order only (order {Plugin.TestSortBy}, Sort ran {press.Ran} time(s))");
                var after = SortChestUi.TestCriterion;
                c.Check(after != null && after.Label == "By type", "and the label follows");
                SetSortBy(SortCriterion.Name);
            }
            else
            {
                c.Check(false, "chest grid focused: the left stick click changes the sort order only (key not pressed: "
                               + (busy.Count > 0 ? $"it is the game's own key for {string.Join(" and ", busy.ToArray())} there, a press would run it" : "the mod does not listen to it")
                               + ")");
            }

            // Own inventory or crafting panel focused: the left stick click does nothing to the chest.
            var others = new List<(string What, UIGroupHandler Group)> { ("your own inventory", gui.m_playerGrid != null ? gui.m_playerGrid.m_uiGroup : null) };
            if (gui.m_uiGroups != null && gui.m_uiGroups.Length > 3)
            {
                others.Add(("the crafting panel", gui.m_uiGroups[3]));
            }
            foreach (var other in others)
            {
                if (other.Group == null || ReferenceEquals(other.Group, group))
                {
                    continue;
                }
                yield return Focus(rig, other.Group);
                busy = GameButtonsOn(gui, key);
                if (busy.Count > 0)
                {
                    c.Check(false, $"{other.What} focused: the left stick click does nothing to the chest (key not pressed: it is the game's own key for "
                                   + $"{string.Join(" and ", busy.ToArray())} there too, a press would run it)");
                    continue;
                }
                yield return PressKey(rig, chest, key, press);
                c.Check(press.Sent && Plugin.TestSortBy == SortCriterion.Name && press.Ran == 0 && !press.Saved && Same(Slots(inv), mixed),
                    $"{other.What} focused: the left stick click does nothing to the chest (order {Plugin.TestSortBy}, Sort ran {press.Ran} time(s)) {Show(Slots(inv))}");
            }

            // Glyph of the sort order button: only with a controller as input device and the chest grid focused.
            rig.ForceGamepad();
            yield return Close(rig);
            yield return Open(rig, chest, box);
            criterion = SortChestUi.TestCriterion;
            if (c.Check(box.Ok && criterion != null && ZInput.IsGamepadActive(),
                    $"setup: Chest open with a controller as input device ({box.Detail}; controller enabled in the game settings: {ZInput.IsGamepadEnabled()})"))
            {
                if (c.Check(criterion.Hint != null, "the sort order button has a glyph object (left stick)"))
                {
                    yield return Focus(rig, group);
                    var glyph = Localization.instance.Localize("$KEY_" + key);
                    c.Check(criterion.Hint.activeInHierarchy && !string.IsNullOrEmpty(criterion.Glyph) && criterion.Glyph == glyph,
                        $"chest grid focused: the left stick glyph is shown ('{criterion.Glyph}' / '{glyph}')");
                    if (gui.m_playerGrid != null && gui.m_playerGrid.m_uiGroup != null)
                    {
                        yield return Focus(rig, gui.m_playerGrid.m_uiGroup);
                        c.Check(!criterion.Hint.activeInHierarchy, "own inventory focused: no glyph on the sort order button");
                    }
                }
            }
            c.Check(Same(Slots(chest.GetInventory()), mixed), $"setup: the Chest still holds its items, unmoved ({Show(Slots(chest.GetInventory()))})");
            c.Report();
        }
        finally
        {
            rig?.Done();
        }
    }

    // ================================================================ sortchest.bug.pad-right-stick (T12, C02: right stick click)

    // Known wrong today (the game, not the mod): Place stacks listens to the right stick click (JoyRStick), so with a
    // chest open that key does something to the chest, where T12 and C02 say it does nothing. Never pressed then.
    private static IEnumerator RunBugPadRightStick()
    {
        const string N = BugPadRightStickTest;
        const string key = "JoyRStick";
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        Rig rig = null;
        try
        {
            rig = new Rig();
            var gui = rig.Gui;
            var chest = rig.Spawn(Chest);
            if (!c.Check(chest != null && chest.GetInventory() != null && Cap(chest.GetInventory()) >= 6, $"setup: {Chest} with at least 6 slots"))
            {
                c.Report();
                yield break;
            }
            var inv = chest.GetInventory();
            var stacks = new Dictionary<string, int> { { "Wood", 4 }, { "Stone", 3 }, { "Resin", 2 }, { "Flint", 1 } };
            Fill(c, chest, Scatter(Cap(inv), stacks.Select(kv => (kv.Key, kv.Value)).ToArray()));
            var mixed = Slots(inv);
            SetSortBy(SortCriterion.Name);
            ForgetLayout(rig, chest);
            var box = new Box();
            yield return Open(rig, chest, box);
            var group = SortChestUi.TestContainerGroup(gui);
            if (!c.Check(box.Ok && group != null, $"the Chest panel opens ({box.Detail})"))
            {
                c.Report();
                yield break;
            }
            yield return WaitLayout(rig, chest, 1, box);
            c.Note($"the game's controller keys: {GameKeys(gui)}");
            var sort = SortChestUi.TestSort;
            var criterion = SortChestUi.TestCriterion;
            c.Check(sort != null && criterion != null && (sort.Pad == null || sort.Pad.m_zinputKey != key) && (criterion.Pad == null || criterion.Pad.m_zinputKey != key),
                "the mod's buttons do not listen to the right stick click");
            yield return Focus(rig, group);
            var busy = GameButtonsOn(gui, key);
            if (busy.Count > 0)
            {
                c.Check(false, "chest grid focused: the right stick click does nothing to the chest (key not pressed: it is the game's own key for "
                               + $"{string.Join(" and ", busy.ToArray())} there, a press would run it)");
            }
            else
            {
                var press = new Clicked();
                yield return PressKey(rig, chest, key, press);
                c.Check(press.Sent && Plugin.TestSortBy == SortCriterion.Name && press.Ran == 0 && !press.Saved && Same(Slots(inv), mixed),
                    $"chest grid focused: the right stick click does nothing to the chest (order {Plugin.TestSortBy}, Sort ran {press.Ran} time(s)) {Show(Slots(inv))}");
            }
            // C02: Loot Pickup Filter uses the right stick click with the own inventory focused. Only noted here.
            if (gui.m_playerGrid != null && gui.m_playerGrid.m_uiGroup != null)
            {
                yield return Focus(rig, gui.m_playerGrid.m_uiGroup);
                busy = GameButtonsOn(gui, key);
                c.Note(busy.Count > 0
                    ? $"own inventory focused: the right stick click is also the game's key for {string.Join(" and ", busy.ToArray())} there (with a chest open)"
                    : "own inventory focused: no game button of the container panel listens to the right stick click");
            }
            c.Report();
        }
        finally
        {
            rig?.Done();
        }
    }

    // ================================================================ sortchest.bug.layout-warning (T13, T17: "Sort buttons" warning)

    // Known wrong today: the game's Take all sits at the left end and Place stacks at the right end of the container
    // panel, so the two buttons the mod puts after Place stacks sit right of the panel. The mod's own layout check
    // then says "stick out of the container panel" and logs a "Sort buttons ..." Warning for every new size, which T13
    // and T17 say must not be there. (Own measures of overlap and screen: sortchest.kinds, sortchest.layout.)
    private static IEnumerator RunBugLayoutWarning()
    {
        const string N = BugLayoutTest;
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        Rig rig = null;
        try
        {
            rig = new Rig();
            SetSortBy(SortCriterion.Name);
            // The containers of T13.
            var kinds = new (string Prefab, string Label, Place Place)[]
            {
                (Chest, "Chest", Place.Near), ("piece_chest_blackmetal", "Black metal chest", Place.Near),
                ("Karve", "Karve", Place.Vehicle), ("VikingShip_Ashlands", "Drakkar", Place.Vehicle),
            };
            var kept = new List<(Container Container, string Label)>();
            var factsNoted = false;
            foreach (var kind in kinds)
            {
                var container = rig.Spawn(kind.Prefab, kind.Place);
                if (!c.Check(container != null && container.GetInventory() != null, $"{kind.Label}: prefab '{kind.Prefab}' exists and has a container"))
                {
                    continue;
                }
                // First opening also writes the facts for the user (rectangles, who changed the panel).
                yield return CheckVerdict(rig, c, container, kind.Label, !factsNoted);
                factsNoted = true;
                if (kind.Place == Place.Vehicle)
                {
                    rig.Destroy(container);
                    yield return null;
                }
                else
                {
                    kept.Add((container, kind.Label));
                }
            }
            // Two extra inventory rows (console: inventorysize 6).
            var rows = rig.P.GetInventory().GetHeight();
            var more = rows == 4 ? 6 : Mathf.Min(rows + 2, 9);
            rig.SetRows(more);
            yield return null;
            foreach (var k in kept)
            {
                yield return CheckVerdict(rig, c, k.Container, $"{k.Label} with {more} inventory rows", false);
            }
            // T17: none in the whole run (this test runs after every other opening of the single-player run).
            var since = _watch != null ? _watch.LayoutWarnings() : new List<string>();
            c.Check(_watch != null && since.Count == 0,
                $"no 'Sort buttons' warning since game start ({since.Count}{(since.Count > 0 ? ", first: " + since[0] : "")})");
            c.Report();
        }
        finally
        {
            rig?.Done();
        }
    }

    // One opening: what the mod's own layout check says, and its warning in the log.
    private static IEnumerator CheckVerdict(Rig rig, Checks c, Container container, string label, bool facts)
    {
        var gui = rig.Gui;
        var logMark = _watch != null ? _watch.MineCount : 0;
        ForgetLayout(rig, container);
        var box = new Box();
        yield return Open(rig, container, box);
        if (!c.Check(box.Ok, $"{label}: the container panel opens ({box.Detail})"))
        {
            yield break;
        }
        yield return WaitLayout(rig, container, 2, box);
        c.Check(box.Ok, $"{label}: the mod's layout check of this opening finished ({box.Detail})");
        var sort = SortChestUi.TestSort;
        var criterion = SortChestUi.TestCriterion;
        if (c.Check(sort != null && criterion != null && sort.Go.activeInHierarchy && criterion.Go.activeInHierarchy, $"{label}: Sort and sort order buttons are shown"))
        {
            var own = SortChestUi.TestLayoutProblem(gui);
            c.Check(own == null, $"{label}: the mod's own layout check finds nothing wrong (it says the buttons {own}; Sort {Fmt(WorldRect(sort.Rect))}, "
                                 + $"sort order {Fmt(WorldRect(criterion.Rect))}, container panel {Fmt(WorldRect(gui.m_container))})");
            if (facts)
            {
                NoteLayoutFacts(c, gui, sort, criterion, label);
            }
        }
        var warnings = Warnings(logMark, "Sort buttons");
        c.Check(warnings.Count == 0, $"{label}: no 'Sort buttons' warning for this opening (log: {(warnings.Count > 0 ? warnings[0] : "")})");
        yield return Close(rig);
    }

    // ================================================================ sortchest.pointer (mouse reach)

    private static GameObject Hit(SortChestUi.TestView view, out Vector2 at)
    {
        at = WorldRect(view.Rect).center;
        var es = EventSystem.current;
        if (es == null)
        {
            return null;
        }
        var hits = new List<RaycastResult>();
        es.RaycastAll(new PointerEventData(es) { position = at }, hits);
        return hits.Count > 0 ? hits[0].gameObject : null;
    }

    private static IEnumerator RunPointer()
    {
        const string N = PointerTest;
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        Rig rig = null;
        try
        {
            rig = new Rig();
            var chest = rig.Spawn(Chest);
            if (!c.Check(chest != null && chest.GetInventory() != null && Cap(chest.GetInventory()) >= 6, $"setup: {Chest} with at least 6 slots"))
            {
                c.Report();
                yield break;
            }
            var inv = chest.GetInventory();
            var stacks = new Dictionary<string, int> { { "Wood", 4 }, { "Stone", 3 }, { "Resin", 2 }, { "Flint", 1 } };
            Fill(c, chest, Scatter(Cap(inv), stacks.Select(kv => (kv.Key, kv.Value)).ToArray()));
            var want = Expect(inv, ByName(stacks.Keys.ToArray()).Select(p => Cell(p, stacks[p])));
            SetSortBy(SortCriterion.Name);
            ForgetLayout(rig, chest);
            var box = new Box();
            yield return Open(rig, chest, box);
            if (!c.Check(box.Ok, $"the Chest panel opens ({box.Detail})"))
            {
                c.Report();
                yield break;
            }
            yield return WaitLayout(rig, chest, 2, box);
            c.Check(box.Ok, $"the mod's layout check of this opening finished ({box.Detail})");
            var sort = SortChestUi.TestSort;
            var criterion = SortChestUi.TestCriterion;
            if (!c.Check(sort != null && criterion != null && EventSystem.current != null && Overlay(sort.Go),
                    "setup: buttons, an event system and a screen-space canvas (button places are screen pixels)"))
            {
                c.Report();
                yield break;
            }
            // A pointer at the middle of each button must reach that button first: nothing drawn over it takes the click.
            var top = Hit(criterion, out var at);
            c.Check(top != null && top.transform.IsChildOf(criterion.Go.transform),
                $"a pointer at ({at.x:F0},{at.y:F0}) reaches the sort order button first (reached '{(top != null ? top.name : "nothing")}')");
            if (top != null && top.transform.IsChildOf(criterion.Go.transform))
            {
                ExecuteEvents.ExecuteHierarchy(top, new PointerEventData(EventSystem.current) { position = at }, ExecuteEvents.pointerClickHandler);
                yield return null;
                c.Check(Plugin.TestSortBy == SortCriterion.Type, $"a click there changes the sort order ({Plugin.TestSortBy})");
            }
            SetSortBy(SortCriterion.Name);
            top = Hit(sort, out at);
            c.Check(top != null && top.transform.IsChildOf(sort.Go.transform),
                $"a pointer at ({at.x:F0},{at.y:F0}) reaches the Sort button first (reached '{(top != null ? top.name : "nothing")}')");
            if (top != null && top.transform.IsChildOf(sort.Go.transform))
            {
                ContainerSorter.TestReset();
                ExecuteEvents.ExecuteHierarchy(top, new PointerEventData(EventSystem.current) { position = at }, ExecuteEvents.pointerClickHandler);
                c.Check(ContainerSorter.LastOutcome == ContainerSorter.SortOutcome.Done && ContainerSorter.LastWritten && Same(Slots(inv), want),
                    $"a click there sorts the chest ({ContainerSorter.LastOutcome}) {Show(Slots(inv))}");
            }
            SelfTest.Screenshot(N, "buttons");
            yield return null;
            yield return null;
            c.Report();
        }
        finally
        {
            rig?.Done();
        }
    }

    // ================================================================ sortchest.search-field (C03 controller part)

    private static IEnumerator RunSearchField()
    {
        const string N = SearchFieldTest;
        const string guid = "MC.UX.Crafting.SearchSort";
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        var other = FeatureRegistry.Find(guid);
        var guard = OtherType(guid, "MC.UX.CraftingSearchSortMod.FocusGuard");
        var field = guard != null ? AccessTools.Field(guard, "FieldUntilFrame") : null;
        if (other == null || !other.Value.IsActive || field == null || field.FieldType != typeof(int))
        {
            SelfTest.Note(N, $"Crafting Search and Sort is not active in this run (found {other != null}, its focus guard {field != null}): nothing checked");
            SelfTest.Pass(N, "0 checks: Crafting Search and Sort is not active, test skipped");
            yield break;
        }
        Rig rig = null;
        try
        {
            rig = new Rig();
            var gui = rig.Gui;
            var chest = rig.Spawn(Chest);
            if (!c.Check(chest != null && chest.GetInventory() != null && Cap(chest.GetInventory()) >= 6, $"setup: {Chest} with at least 6 slots"))
            {
                c.Report();
                yield break;
            }
            var inv = chest.GetInventory();
            var stacks = new Dictionary<string, int> { { "Wood", 4 }, { "Stone", 3 }, { "Resin", 2 }, { "Flint", 1 } };
            Fill(c, chest, Scatter(Cap(inv), stacks.Select(kv => (kv.Key, kv.Value)).ToArray()));
            var want = Expect(inv, ByName(stacks.Keys.ToArray()).Select(p => Cell(p, stacks[p])));
            var mixed = Slots(inv);
            SetSortBy(SortCriterion.Name);
            ForgetLayout(rig, chest);
            var box = new Box();
            yield return Open(rig, chest, box);
            var group = SortChestUi.TestContainerGroup(gui);
            if (!c.Check(box.Ok && group != null, $"the Chest panel opens ({box.Detail})"))
            {
                c.Report();
                yield break;
            }
            yield return WaitLayout(rig, chest, 1, box);
            yield return Focus(rig, group);

            // Its search field has the keyboard (the mark that mod keeps while the cursor is in the field).
            var before = (int)field.GetValue(null);
            rig.Undo(() => field.SetValue(null, before));
            var press = new Clicked();
            field.SetValue(null, Time.frameCount + 600);
            yield return PressKey(rig, chest, "JoyBack", press);
            c.Check(press.Sent && press.Ran == 0 && Same(Slots(inv), mixed), $"cursor in its search field: View/Select does nothing to the chest (Sort ran {press.Ran} time(s))");
            // Left stick click = the game's Take all key: its work is swapped for a counter, so a press that got through
            // would be counted, not empty the chest into the test character.
            var takeFired = CountInstead(rig, gui.m_takeAllButton, gui.OnTakeAll);
            field.SetValue(null, Time.frameCount + 600);
            yield return PressKey(rig, chest, "JoyLStick", press);
            c.Check(press.Sent && Plugin.TestSortBy == SortCriterion.Name && press.Ran == 0 && takeFired[0] == 0 && Same(Slots(inv), mixed),
                $"cursor in its search field: the left stick click does nothing to the chest (sort order {Plugin.TestSortBy}, Sort ran {press.Ran} time(s), "
                + $"the game's Take all fired {takeFired[0]} time(s))");
            field.SetValue(null, before);
            yield return null;
            yield return null;
            yield return PressKey(rig, chest, "JoyBack", press);
            c.Check(press.Sent && press.Ran == 1 && press.Sorted && Same(Slots(inv), want), $"cursor out of the field: View/Select sorts again ({press})");
            c.Report();
        }
        finally
        {
            rig?.Done();
        }
    }

    // ================================================================ sortchest.log (T17)

    private static IEnumerator RunLog()
    {
        const string N = LogTest;
        var c = new Checks(N);
        if (!c.Check(_watch != null, "setup: the log is watched since the mod started"))
        {
            c.Report();
            yield break;
        }
        // Error half of T17. Its "Sort buttons" warning half is wrong today: sortchest.bug.layout-warning holds it alone.
        var errors = _watch.Errors();
        var layout = _watch.LayoutWarnings();
        c.Note($"{_watch.MineCount} log lines of {ModInfo.Name} since game start: {errors.Count} error line(s) that come from it or name it or MC.UX, "
               + $"{layout.Count} 'Sort buttons' warning(s) (see {BugLayoutTest})");
        foreach (var p in errors.Take(8))
        {
            c.Note("log: " + p);
        }
        c.Check(_watch.MineCount > 0, "setup: lines of this mod were seen since game start (the watch works)");
        c.Check(errors.Count == 0,
            $"no error line that comes from {ModInfo.Name} or names it or MC.UX since game start ({errors.Count}, first: {(errors.Count > 0 ? errors[0] : "")})");
        c.Report();
    }

    // ================================================================ multiplayer (dedicated server without this mod)

    private static bool OnDedicatedServer(Checks c)
    {
        var client = ZNet.instance != null && !ZNet.instance.IsServer() && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected;
        return c.Check(client && SelfTest.IsMultiplayerRun, "setup: this game is a client of the test's dedicated server");
    }

    private static bool ServerHas(ZDO zdo)
    {
        foreach (var peer in ZDOMan.instance.m_peers)
        {
            if (peer != null && peer.m_zdos.TryGetValue(zdo.m_uid, out var info) && info.m_dataRevision >= zdo.DataRevision)
            {
                return true;
            }
        }
        return false;
    }

    // M01, M02, M04 (and the last part of M03): what the server holds after a sort, and gives to who asks.
    private static IEnumerator RunMpServerCopy()
    {
        const string N = MpServerCopyTest;
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        Rig rig = null;
        try
        {
            if (!OnDedicatedServer(c))
            {
                c.Report();
                yield break;
            }
            rig = new Rig();
            // Facts for the sortchest.bug.* tests, seen in a run with MC mods only (no third-party mod can have changed them).
            c.Note("this run has MC mods only: " + TankardFacts());
            c.Note($"this run has MC mods only: the game's controller keys: {GameKeys(rig.Gui)}");
            // This mod is made for the game client only: the dedicated server program never loads it.
            var processes = typeof(Plugin).GetCustomAttributes(typeof(BepInEx.BepInProcess), false).Cast<BepInEx.BepInProcess>().Select(p => p.ProcessName).ToList();
            c.Check(processes.Count == 1 && string.Equals(processes[0], "valheim.exe", StringComparison.OrdinalIgnoreCase),
                $"the mod loads in the game client only, so the dedicated server runs without it ({string.Join(", ", processes)})");
            var chest = rig.Spawn(Chest);
            if (!c.Check(chest != null && chest.GetInventory() != null && Cap(chest.GetInventory()) >= 10, $"setup: {Chest} with at least 10 slots"))
            {
                c.Report();
                yield break;
            }
            var inv = chest.GetInventory();
            var cap = Cap(inv);
            var max = Shared("Wood") != null ? Shared("Wood").m_maxStackSize : 0;
            if (!c.Check(max >= 31, $"setup: Wood stacks to at least 31 ({max})"))
            {
                c.Report();
                yield break;
            }
            Fill(c, chest, (cap - 1, "Wood", 20), (0, "Stone", 5), (cap - 2, "Wood", 10), (2, "Resin", 3), (cap - 3, "AxeFlint", 1), (4, "TrophyBoar", 1));
            var totals = Totals(inv);
            var unsortedBytes = StoredBytes(chest);
            var unsorted = Slots(inv);
            var stacks = new Dictionary<string, int> { { "Wood", 30 }, { "Stone", 5 }, { "Resin", 3 }, { "AxeFlint", 1 }, { "TrophyBoar", 1 } };
            SetSortBy(SortCriterion.Name);
            Plugin.TestMergeStacks = true;
            var box = new Box();
            yield return Open(rig, chest, box);
            if (!c.Check(box.Ok && unsortedBytes != null, $"the Chest panel opens on the server world ({box.Detail})"))
            {
                c.Report();
                yield break;
            }
            var want = Expect(inv, ByName(stacks.Keys.ToArray()).Select(p => Cell(p, stacks[p])));
            var click = ClickSort(rig, chest);
            c.Check(click.Sent && click.Sorted && click.Merged == 1 && Same(Slots(inv), want), $"Sort merges and sorts on the dedicated server: {Show(Slots(inv))} ({click})");
            yield return Close(rig);
            yield return new WaitForSecondsRealtime(1.3f);

            var zdo = Zdo(chest);
            var sortedBytes = StoredBytes(chest);
            var end = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < end && !ServerHas(zdo))
            {
                yield return null;
            }
            if (!c.Check(ServerHas(zdo) && sortedBytes != null, "the sorted chest was sent to the server"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSecondsRealtime(0.5f);

            // Make the server send its copy back: our copy is set back to the unsorted content and marked older than
            // the server's, then our (older) revision numbers are sent. The game's own sync then brings the server's
            // content, which replaces ours. The server never takes the older data.
            var rev = zdo.DataRevision;
            zdo.Set(ZDOVars.s_items, unsortedBytes);
            zdo.DataRevision = rev - 1;
            zdo.OwnerRevision = (ushort)(zdo.OwnerRevision + 1);
            ZDOMan.instance.ClientChanged(zdo.m_uid);
            var scrambled = Decode(chest, StoredBytes(chest));
            c.Check(scrambled != null && Same(Slots(scrambled), unsorted), "setup: our copy is back to the unsorted content, so only the server still has the sorted one");
            end = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < end && !(zdo.DataRevision >= rev && SameBytes(StoredBytes(chest), sortedBytes)))
            {
                yield return null;
            }
            var back = StoredBytes(chest);
            if (!c.Check(zdo.DataRevision >= rev && SameBytes(back, sortedBytes),
                    $"the server sends back the content it keeps: byte for byte the sorted, merged chest (revision {zdo.DataRevision}, was {rev})"))
            {
                c.Report();
                yield break;
            }
            // Read like a game without the mod reads it.
            var served = Decode(chest, back);
            c.Check(served != null && Same(Slots(served), want), $"read the game's own way it is the sorted layout: {Show(served != null ? Slots(served) : null)}");
            c.Check(served != null && Totals(served) == totals, $"nothing lost or duplicated ({(served != null ? Totals(served) : "")}, was {totals})");
            c.Check(served != null && served.GetAllItems().All(i => i.m_shared != null && i.m_stack >= 1 && i.m_stack <= i.m_shared.m_maxStackSize
                                                                  && i.m_customData.Count == 0 && i.m_quality == 1 && !i.m_cheated && i.m_pickedUp && i.m_crafterID == 0L),
                "every stack is a plain item: within its stack size, no extra data added by the mod, marks unchanged");
            var woodTip = served != null ? served.GetAllItems().Where(i => Prefab(i) == "Wood").Select(i => i.GetTooltip()).FirstOrDefault() : null;
            var plain = Make("Wood", 30);
            c.Check(woodTip != null && plain != null && woodTip == plain.GetTooltip(), "the merged Wood stack has the tooltip of any Wood stack of 30");

            // And our own chest shows it after the game reloaded it from that copy.
            yield return new WaitForSecondsRealtime(1.4f);
            yield return Open(rig, chest, box);
            c.Check(box.Ok && Same(Slots(chest.GetInventory()), want), $"opened again, the chest shows the server's copy: {Show(Slots(chest.GetInventory()))}");
            c.Report();
        }
        finally
        {
            rig?.Done();
        }
    }

    private static string ConfigValue(string path, string key)
    {
        var match = Regex.Match(File.ReadAllText(path), @"(?m)^" + Regex.Escape(key) + @"\s*=\s*(\S+)\s*$");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static void EditConfig(string path, string key, string value)
    {
        var text = File.ReadAllText(path);
        File.WriteAllText(path, Regex.Replace(text, @"(?m)^" + Regex.Escape(key) + @"\s*=\s*\S+[ \t]*\r?$", key + " = " + value));
    }

    // T04 with the real setting and the real config file (throwaway file in a multiplayer run).
    private static IEnumerator RunMpSettings()
    {
        const string N = MpSettingsTest;
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        Rig rig = null;
        var plugin = Self();
        var original = Plugin.SortBy.Value;
        var mergeBefore = Plugin.MergeStacks.Value;
        try
        {
            if (!OnDedicatedServer(c) || !c.Check(plugin != null && File.Exists(plugin.Config.ConfigFilePath), "setup: the mod's config file exists"))
            {
                c.Report();
                yield break;
            }
            var path = plugin.Config.ConfigFilePath;
            rig = new Rig(); // no forced sort order: the real setting is used
            var chest = rig.Spawn(Chest);
            if (!c.Check(chest != null && chest.GetInventory() != null && Cap(chest.GetInventory()) >= 6, $"setup: {Chest} with at least 6 slots"))
            {
                c.Report();
                yield break;
            }
            var inv = chest.GetInventory();
            Fill(c, chest, Scatter(Cap(inv), ("Wood", 4), ("AxeFlint", 1), ("Raspberry", 2), ("TrophyBoar", 1)));
            var box = new Box();
            yield return Open(rig, chest, box);
            if (!c.Check(box.Ok, $"the Chest panel opens ({box.Detail})"))
            {
                c.Report();
                yield break;
            }
            var slots = Slots(inv);
            var rev = Rev(chest);
            var clicks = ContainerSorter.Clicks;
            c.Check(Shows(original, out var got), $"start: the label shows the setting {original} ({got})");

            // Three clicks: setting, label, Sort tooltip and config file follow; the chest does not move.
            var value = original;
            for (var i = 0; i < 3; i++)
            {
                value = value == SortCriterion.Name ? SortCriterion.Type : value == SortCriterion.Type ? SortCriterion.Biome : SortCriterion.Name;
                var sent = Press(SortChestUi.TestCriterion, out var why);
                yield return null;
                yield return null;
                c.Check(sent && Plugin.SortBy.Value == value && Shows(value, out got), $"click {i + 1}: SortBy is {value}, label and Sort tooltip follow ({why} {Plugin.SortBy.Value}, {got})");
                c.Check(ConfigValue(path, "SortBy") == value.ToString(), $"click {i + 1}: the config file says SortBy = {value} (file: {ConfigValue(path, "SortBy")})");
                c.Check(Same(Slots(inv), slots) && Rev(chest) == rev && ContainerSorter.Clicks == clicks, $"click {i + 1}: the chest does not move");
            }
            c.Check(value == original, "three clicks cycle back to the start");

            // Edit the file with the chest open: the label follows within a second.
            var edited = original == SortCriterion.Biome ? SortCriterion.Name : SortCriterion.Biome;
            var t0 = Time.realtimeSinceStartup;
            EditConfig(path, "SortBy", edited.ToString());
            while (Time.realtimeSinceStartup - t0 < 8f && !(Plugin.SortBy.Value == edited && Shows(edited, out got)))
            {
                yield return null;
            }
            var took = Time.realtimeSinceStartup - t0;
            c.Check(Plugin.SortBy.Value == edited && Shows(edited, out got) && took <= 2f,
                $"SortBy = {edited} written in the config file: the label follows within a second or two ({took:F2} s; {Plugin.SortBy.Value}, {got})");
            c.Check(ReferenceEquals(rig.Gui.m_currentContainer, chest) && Same(Slots(inv), slots), "the chest stays open and does not move");

            // Change the setting the way ConfigurationManager does: label follows at once, file keeps it for the next start.
            var set = edited == SortCriterion.Type ? SortCriterion.Name : SortCriterion.Type;
            Plugin.SortBy.Value = set;
            yield return null;
            c.Check(Shows(set, out got), $"SortBy set to {set} (as ConfigurationManager does): the label follows ({got})");
            c.Check(ConfigValue(path, "SortBy") == set.ToString(), $"the config file keeps the last choice for the next game start (file: {ConfigValue(path, "SortBy")})");
            var click = ClickSort(rig, chest);
            c.Check(click.Sent && click.Sorted && ContainerSorter.LastCriterion == set, $"Sort uses the setting ({ContainerSorter.LastCriterion}, {click})");

            // T05 with the real MergeStacks setting: off = stacks stay separate, on = they merge.
            var max = Shared("Wood") != null ? Shared("Wood").m_maxStackSize : 0;
            if (c.Check(max >= 30 && Cap(inv) >= 10, $"setup: Wood stacks to at least 30 ({max}) and the Chest has 10 slots"))
            {
                yield return Close(rig);
                Fill(c, chest, (0, "Wood", 20), (4, "Wood", 30), (8, "Wood", 15));
                Plugin.MergeStacks.Value = false;
                yield return Open(rig, chest, box);
                click = ClickSort(rig, chest);
                var apart = Expect(inv, new[] { Cell("Wood", 30), Cell("Wood", 20), Cell("Wood", 15) });
                c.Check(box.Ok && click.Sent && click.Sorted && click.Merged == 0 && Same(Slots(inv), apart),
                    $"MergeStacks = false: stacks stay separate, larger first, adjacent: {Show(Slots(inv))} ({click})");
                c.Check(ConfigValue(path, "MergeStacks") == "false", $"the config file says MergeStacks = false (file: {ConfigValue(path, "MergeStacks")})");
                Plugin.MergeStacks.Value = true;
                click = ClickSort(rig, chest);
                var together = new List<int>();
                for (var left = 65; left > 0; left -= max)
                {
                    together.Add(Mathf.Min(left, max));
                }
                c.Check(click.Sent && click.Sorted && Same(Slots(inv), Expect(inv, together.Select(n => Cell("Wood", n)))),
                    $"MergeStacks = true: the same stacks merge to {string.Join(" + ", together)}: {Show(Slots(inv))} ({click})");
            }
            c.Report();
        }
        finally
        {
            try
            {
                Plugin.SortBy.Value = original;
                Plugin.MergeStacks.Value = mergeBefore;
            }
            catch (Exception e)
            {
                Log.Warning($"Self-test could not put the settings back: {e}");
            }
            rig?.Done();
        }
    }

    private static IEnumerator WaitActive(Plugin plugin, bool active, float seconds, Box box)
    {
        var t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < seconds && plugin.IsActive != active)
        {
            yield return null;
        }
        box.Ok = plugin.IsActive == active;
        box.Detail = $"{Time.realtimeSinceStartup - t0:F2} s, state {plugin.State}: {plugin.StatusText}";
    }

    // T15 (config file edited with a chest open), T18 (Enabled changed with the inventory closed), T16 (what a chest
    // shows while the file says Enabled = false). Throwaway config file in a multiplayer run.
    private static IEnumerator RunMpToggle()
    {
        const string N = MpToggleTest;
        if (!Ready(N))
        {
            yield break;
        }
        var c = new Checks(N);
        Rig rig = null;
        var plugin = Self();
        try
        {
            if (!OnDedicatedServer(c) || !c.Check(plugin != null && plugin.IsActive && File.Exists(plugin.Config.ConfigFilePath), "setup: mod active, config file exists"))
            {
                c.Report();
                yield break;
            }
            var path = plugin.Config.ConfigFilePath;
            rig = new Rig();
            var gui = rig.Gui;
            var chest = rig.Spawn(Chest);
            if (!c.Check(chest != null && chest.GetInventory() != null && Cap(chest.GetInventory()) >= 6, $"setup: {Chest} with at least 6 slots"))
            {
                c.Report();
                yield break;
            }
            var inv = chest.GetInventory();
            var stacks = new Dictionary<string, int> { { "Wood", 4 }, { "Stone", 3 }, { "Resin", 2 }, { "Flint", 1 } };
            Fill(c, chest, Scatter(Cap(inv), stacks.Select(kv => (kv.Key, kv.Value)).ToArray()));
            var want = Expect(inv, ByName(stacks.Keys.ToArray()).Select(p => Cell(p, stacks[p])));
            var box = new Box();
            yield return Open(rig, chest, box);
            if (!c.Check(box.Ok && ButtonsShown(), $"setup: Chest open with the Sort buttons ({box.Detail})"))
            {
                c.Report();
                yield break;
            }
            var slots = Slots(inv);
            var rev = Rev(chest);

            // T15: Enabled = false written in the config file while the chest is open.
            EditConfig(path, "Enabled", "false");
            yield return WaitActive(plugin, false, 8f, box);
            c.Check(box.Ok, $"Enabled = false in the config file turns the mod off while the game runs ({box.Detail})");
            yield return null;
            yield return null;
            c.Check(SortChestUi.TestSort == null && OurObjects(gui) == 0, $"off: both buttons are gone ({OurObjects(gui)} objects of ours left)");
            c.Check(!ShowPatched(), "off: our patch on the inventory screen is removed");
            c.Check(ReferenceEquals(gui.m_currentContainer, chest) && gui.m_container.gameObject.activeInHierarchy && Same(Slots(inv), slots) && Rev(chest) == rev,
                "off: the chest stays open and untouched");
            c.Check(gui.m_takeAllButton.gameObject.activeInHierarchy && gui.m_stackAllButton.gameObject.activeInHierarchy, "off: Take all and Place stacks still there");
            SelfTest.Screenshot(N, "off");
            yield return null;
            yield return null;

            EditConfig(path, "Enabled", "true");
            yield return WaitActive(plugin, true, 8f, box);
            c.Check(box.Ok, $"Enabled = true in the config file turns it on again, no restart ({box.Detail})");
            yield return null;
            yield return null;
            c.Check(ButtonsShown() && ReferenceEquals(gui.m_currentContainer, chest) && ShowPatched(), "on: the buttons appear without reopening the chest");
            SetSortBy(SortCriterion.Name);
            var click = ClickSort(rig, chest);
            c.Check(click.Sent && click.Sorted && Same(Slots(inv), want), $"on: Sort works ({click}) {Show(Slots(inv))}");

            // T18 / T16: turned off with the inventory closed (what the MC Mods panel checkbox sets), then a chest opened.
            yield return Close(rig);
            plugin.Enabled.Value = false;
            yield return null;
            c.Check(!plugin.IsActive && ConfigValue(path, "Enabled") == "false", $"Enabled set to false: mod off, and the config file says so for the next start ({plugin.State}, file {ConfigValue(path, "Enabled")})");
            yield return Open(rig, chest, box);
            c.Check(box.Ok && OurObjects(gui) == 0 && gui.m_takeAllButton.gameObject.activeInHierarchy && gui.m_stackAllButton.gameObject.activeInHierarchy,
                $"off: a chest opened now shows the game's own panel, no Sort buttons ({box.Detail}, {OurObjects(gui)} objects of ours)");
            yield return Close(rig);
            plugin.Enabled.Value = true;
            yield return null;
            c.Check(plugin.IsActive && ConfigValue(path, "Enabled") == "true", $"Enabled set to true: mod on, no restart ({plugin.State})");
            yield return Open(rig, chest, box);
            c.Check(box.Ok && ButtonsShown(), $"on: a chest opened now shows the Sort buttons ({box.Detail})");
            SetSortBy(SortCriterion.Name);
            c.Check(Unsort(inv), "setup: two Chest items swapped by hand");
            click = ClickSort(rig, chest);
            c.Check(click.Sent && click.Sorted && Same(Slots(inv), want), $"on: Sort works ({click})");
            c.Report();
        }
        finally
        {
            try
            {
                if (plugin != null && !plugin.Enabled.Value)
                {
                    plugin.Enabled.Value = true;
                }
            }
            catch (Exception e)
            {
                Log.Error($"Self-test could not turn the mod back on: {e}");
            }
            rig?.Done();
        }
    }
#endif
}
