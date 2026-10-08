using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
#endif

namespace MC.UX.AutoPickupFilterMod;

// Debug build only (calls vanish in Release). In-world self tests, run by world probe (tools/Test-InWorld.ps1,
// -Mod AutoPickup.Filter) and, for the lootfilter.mp.* ones, by tools/Test-Multiplayer.ps1 (scenario modded).
// One test = one group of TESTING.md items:
//   SelfTests.Ui.cs       lootfilter.button (T01, T27), lootfilter.layout (T20), lootfilter.marks (T07, T08, T29),
//                         lootfilter.mark-colours (red / green of T03, T06, T07, T12), lootfilter.chest (T12),
//                         lootfilter.badges (T13), lootfilter.outside-grid (C06),
//                         lootfilter.commands (T17), lootfilter.gamepad (T18, T35), lootfilter.markkey (T19, T34),
//                         lootfilter.dialogs (T26), lootfilter.toggle (T21, M06), lootfilter.persist (T15, T16, M04),
//                         lootfilter.future-data (T40), lootfilter.compat-search (C01), lootfilter.compat-repair (C03),
//                         lootfilter.log (T23); lootfilter.commands also covers T39
//   SelfTests.Pickup.cs   lootfilter.everything (T02), lootfilter.skip (T03, T04, T05), lootfilter.only (T06, T09),
//                         lootfilter.vswitch (T10, T28), lootfilter.owndrops (T11, T36), lootfilter.spear (T14),
//                         lootfilter.handoff (M02), lootfilter.foreign-patch (C07)
//   SelfTests.Harvest.cs  lootfilter.harvest (T24), lootfilter.stations (T24 optional part, T37),
//                         lootfilter.filtered (T25), lootfilter.door (T32), lootfilter.kiln (T33),
//                         lootfilter.scythe (T30, T31), lootfilter.scythe-swing (real swing, T30),
//                         lootfilter.compat-batch (C02)
//   lootfilter.bug.*      one check each that the mod does not meet today (red until the mod is fixed), kept alone so
//                         the other checks of its item stay green: lootfilter.bug.badge-hotbar-number (T13, Ui),
//                         lootfilter.bug.lists-panel-over-grid (T18, Ui), lootfilter.bug.pick-column (T38, Harvest)
//   SelfTests.Mp.cs       lootfilter.mp.filter (M01, M04), lootfilter.mp.handoff (M02), lootfilter.mp.remote-harvest
//                         (M05). Mod is client only: it never load on the dedicated server, so no server step.
// Me never write config: settings forced in memory with TestHooks (then the entry's real change handlers are run with
// RaiseChanged), mouse / key / controller played with TestHooks, feature turned off with TestHooks.ForceOff. Rig put
// back filter data of the character, inventory, equipment, known items, skills, auto pickup switch, the player's
// place and look, and destroy every spawned thing and new item drop.
internal static partial class SelfTests
{
    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        foreach (var test in Tests)
        {
            SelfTest.Register(test.Key, test.Value);
        }
        foreach (var test in MpTests)
        {
            SelfTest.RegisterMultiplayer(test.Key, SelfTest.Modded, test.Value);
        }
#endif
    }

    // OnDeactivated call me. Hooks stay: a test that turn the mod off and on itself (lootfilter.toggle) still need
    // them; every test clear its own in finally (Rig.Restore).
    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        foreach (var test in Tests)
        {
            SelfTest.Unregister(test.Key);
        }
        foreach (var test in MpTests)
        {
            SelfTest.UnregisterMultiplayer(test.Key);
        }
#endif
    }

#if DEBUG
    // Single-player tests in run order. Log scan last: it look at what the others left in the log.
    private static readonly KeyValuePair<string, Func<IEnumerator>>[] Tests =
    {
        new KeyValuePair<string, Func<IEnumerator>>(ButtonName, RunButton),
        new KeyValuePair<string, Func<IEnumerator>>(LayoutName, RunLayout),
        new KeyValuePair<string, Func<IEnumerator>>(EverythingName, RunEverything),
        new KeyValuePair<string, Func<IEnumerator>>(SkipName, RunSkip),
        new KeyValuePair<string, Func<IEnumerator>>(OnlyName, RunOnly),
        new KeyValuePair<string, Func<IEnumerator>>(MarksName, RunMarks),
        new KeyValuePair<string, Func<IEnumerator>>(MarkColoursName, RunMarkColours),
        new KeyValuePair<string, Func<IEnumerator>>(VSwitchName, RunVSwitch),
        new KeyValuePair<string, Func<IEnumerator>>(OwnDropsName, RunOwnDrops),
        new KeyValuePair<string, Func<IEnumerator>>(SpearName, RunSpear),
        new KeyValuePair<string, Func<IEnumerator>>(ChestName, RunChest),
        new KeyValuePair<string, Func<IEnumerator>>(BadgesName, RunBadges),
        new KeyValuePair<string, Func<IEnumerator>>(BugBadgeHotbarName, RunBugBadgeHotbar),
        new KeyValuePair<string, Func<IEnumerator>>(OutsideGridName, RunOutsideGrid),
        new KeyValuePair<string, Func<IEnumerator>>(PersistName, RunPersist),
        new KeyValuePair<string, Func<IEnumerator>>(FutureDataName, RunFutureData),
        new KeyValuePair<string, Func<IEnumerator>>(CommandsName, RunCommands),
        new KeyValuePair<string, Func<IEnumerator>>(GamepadName, RunGamepad),
        new KeyValuePair<string, Func<IEnumerator>>(BugListsPanelName, RunBugListsPanel),
        new KeyValuePair<string, Func<IEnumerator>>(MarkKeyName, RunMarkKey),
        new KeyValuePair<string, Func<IEnumerator>>(DialogsName, RunDialogs),
        new KeyValuePair<string, Func<IEnumerator>>(ToggleName, RunToggle),
        new KeyValuePair<string, Func<IEnumerator>>(HarvestName, RunHarvest),
        new KeyValuePair<string, Func<IEnumerator>>(StationsName, RunStations),
        new KeyValuePair<string, Func<IEnumerator>>(FilteredName, RunFiltered),
        new KeyValuePair<string, Func<IEnumerator>>(DoorName, RunDoor),
        new KeyValuePair<string, Func<IEnumerator>>(KilnName, RunKiln),
        new KeyValuePair<string, Func<IEnumerator>>(ScytheName, RunScythe),
        new KeyValuePair<string, Func<IEnumerator>>(ScytheSwingName, RunScytheSwing),
        new KeyValuePair<string, Func<IEnumerator>>(BugPickColumnName, RunBugPickColumn),
        new KeyValuePair<string, Func<IEnumerator>>(HandoffName, RunHandoff),
        new KeyValuePair<string, Func<IEnumerator>>(ForeignPatchName, RunForeignPatch),
        new KeyValuePair<string, Func<IEnumerator>>(CompatSearchName, RunCompatSearch),
        new KeyValuePair<string, Func<IEnumerator>>(CompatRepairName, RunCompatRepair),
        new KeyValuePair<string, Func<IEnumerator>>(CompatBatchName, RunCompatBatch),
        new KeyValuePair<string, Func<IEnumerator>>(LogName, RunLog),
    };

    // Multiplayer tests (client joined to the dedicated server, scenario modded).
    private static readonly KeyValuePair<string, Func<IEnumerator>>[] MpTests =
    {
        new KeyValuePair<string, Func<IEnumerator>>(MpFilterName, RunMpFilter),
        new KeyValuePair<string, Func<IEnumerator>>(MpHandoffName, RunMpHandoff),
        new KeyValuePair<string, Func<IEnumerator>>(MpRemoteHarvestName, RunMpRemoteHarvest),
    };

    private const string ButtonObject = "MC_LootFilterButton";
    private const string ListsObject = "MC_LootFilterLists";
    private const string MarkObject = "MC_LootFilterMark";
    private const string SpriteIgnored = "MC_LootFilter_Ignored";
    private const string SpriteSelected = "MC_LootFilter_Selected";
    private const string SkipLineIgnored = "Auto pickup skips this (ignored)";
    private const string SkipLineNotSelected = "Auto pickup skips this (not selected)";
    private const string SkipLineAny = "Auto pickup skips this";

    // ---------------------------------------------------------------- checks

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

    private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string F(Vector3 v) => $"({F(v.x)}, {F(v.y)}, {F(v.z)})";

    private static string F(Rect r) => $"({F(r.xMin)},{F(r.yMin)})-({F(r.xMax)},{F(r.yMax)})";

    private static IEnumerator Frames(int n)
    {
        for (var i = 0; i < n; i++)
        {
            yield return null;
        }
    }

    // New colliders join physics at the next simulation step.
    private static IEnumerator Settle()
    {
        yield return null;
        yield return new WaitForFixedUpdate();
        yield return null;
    }

    private static string L(string text) => Localization.instance != null ? Localization.instance.Localize(text) : text;

    // "$item_stone" for "Stone".
    private static string Shared(string prefab)
    {
        var go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
        var drop = go != null ? go.GetComponent<ItemDrop>() : null;
        return drop != null ? drop.m_itemData.m_shared.m_name : "?" + prefab;
    }

    private static int Count(Inventory inv, string prefab) => inv.CountItems(Shared(prefab), -1, false);

    // ---------------------------------------------------------------- messages (what reach MessageHud)

    private sealed class Shown
    {
        internal MessageHud.MessageType Type;
        internal string Text;
    }

    private static readonly List<Shown> Messages = new List<Shown>();
    private static Harmony _tap;

    // Me listen at MessageHud.ShowMessage (raw text, tokens not yet translated). Last + void: run after every other
    // prefix, and skip a message another mod swallowed (__runOriginal false = it never reach the HUD).
    private static void TapMessages()
    {
        Messages.Clear();
        if (_tap != null)
        {
            return;
        }
        _tap = new Harmony(ModInfo.Guid + ".selftest");
        var prefix = new HarmonyMethod(typeof(SelfTests), nameof(ShowMessageTap)) { priority = Priority.Last };
        _tap.Patch(AccessTools.Method(typeof(MessageHud), nameof(MessageHud.ShowMessage)), prefix: prefix);
    }

    private static void UntapMessages()
    {
        if (_tap != null)
        {
            _tap.UnpatchSelf();
            _tap = null;
        }
        Messages.Clear();
    }

    private static void ShowMessageTap(MessageHud.MessageType type, string text, bool __runOriginal)
    {
        if (!__runOriginal)
        {
            return;
        }
        Messages.Add(new Shown { Type = type, Text = text ?? "" });
    }

    private static int TopLeft(string exact) =>
        Messages.Count(m => m.Type == MessageHud.MessageType.TopLeft && m.Text == exact);

    private static int TopLeftStarting(string start) =>
        Messages.Count(m => m.Type == MessageHud.MessageType.TopLeft && m.Text.StartsWith(start, StringComparison.Ordinal));

    private static readonly string[] OurMessageStarts =
    {
        "Auto pickup filter", "Ignored by auto pickup", "No longer ignored", "Selected for auto pickup", "No longer selected",
        "Choose Skip ignored", "This item cannot be filtered",
    };

    // Messages of this mod shown since the last Messages.Clear() (other mods and the game talk too).
    private static int OurMessages() =>
        Messages.Count(m => OurMessageStarts.Any(s => m.Text.StartsWith(s, StringComparison.Ordinal)));

    private static string AllMessages() =>
        Messages.Count == 0 ? "none" : string.Join(" | ", Messages.Select(m => m.Type + ": " + m.Text).ToArray());

    // ---------------------------------------------------------------- log lines of this mod

    private sealed class LogTap : ILogListener
    {
        private readonly object _gate = new object();
        private readonly List<KeyValuePair<LogLevel, string>> _lines = new List<KeyValuePair<LogLevel, string>>();

        public void LogEvent(object sender, LogEventArgs e)
        {
            try
            {
                if (e == null || e.Source == null || e.Source.SourceName != ModInfo.Name)
                {
                    return;
                }
                var text = e.Data as string ?? e.Data?.ToString() ?? "";
                // Own test result lines are not mod errors.
                if (text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal))
                {
                    return;
                }
                lock (_gate)
                {
                    _lines.Add(new KeyValuePair<LogLevel, string>(e.Level, text));
                }
            }
            catch
            {
                // Me never throw inside logger.
            }
        }

        public void Dispose()
        {
        }

        internal int Count(LogLevel level, string contains = null)
        {
            lock (_gate)
            {
                return _lines.Count(l => (l.Key & level) != 0 && (contains == null || l.Value.IndexOf(contains, StringComparison.Ordinal) >= 0));
            }
        }

        internal string First(LogLevel level)
        {
            lock (_gate)
            {
                foreach (var l in _lines)
                {
                    if ((l.Key & level) != 0)
                    {
                        return l.Value.Length > 200 ? l.Value.Substring(0, 200) : l.Value;
                    }
                }
            }
            return "";
        }
    }

    // Sites PatchGuard already reported (own copy of the shared class in this dll). -1 = cannot read.
    private static int GuardCount()
    {
        var field = typeof(PatchGuard).GetField("Reported", BindingFlags.NonPublic | BindingFlags.Static);
        return field != null && field.GetValue(null) is HashSet<string> set ? set.Count : -1;
    }

    // ---------------------------------------------------------------- settings

    // Run the entry's real SettingChanged handlers (the lambdas Plugin.BindConfig hooked), without touching Value:
    // nothing saved. BepInEx keep them in the event's backing field. False = not found (test then fail).
    private static bool RaiseChanged(ConfigEntryBase entry)
    {
        if (entry == null)
        {
            return false;
        }
        var field = entry.GetType().GetField("SettingChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        if (!(field != null && field.GetValue(entry) is EventHandler handler))
        {
            return false;
        }
        handler(entry, EventArgs.Empty);
        return true;
    }

    private static ModPlugin PluginInstance()
    {
        return Chainloader.PluginInfos.TryGetValue(ModInfo.Guid, out var info) ? info.Instance as ModPlugin : null;
    }

    private static bool OtherModActive(string guid)
    {
        var view = FeatureRegistry.Find(guid);
        return view != null && view.Value.IsActive;
    }

    // ---------------------------------------------------------------- rig

    // Me = what the tests change on the player and the world. Made at test start (neutral settings, clean filter of a
    // never-used character, auto pickup on, message and log taps on). Restore (finally, no yield) put all back.
    private sealed class Rig
    {
        private struct Slot
        {
            internal ItemDrop.ItemData Item;
            internal int Stack;
            internal Vector2i Pos;
            internal float Durability;
        }

        internal readonly Player Player;
        internal readonly Vector3 Origin;
        internal readonly Vector3 Forward;
        internal readonly Vector3 Right;
        internal readonly LogTap Logs = new LogTap();
        private readonly string _name;
        private readonly Quaternion _rotation;
        private readonly float _pitch;
        private readonly Quaternion _yaw;
        private readonly ItemDrop.ItemData _right;
        private readonly ItemDrop.ItemData _left;
        private readonly List<Slot> _inventory = new List<Slot>();
        private readonly HashSet<ItemDrop.ItemData> _inventorySet = new HashSet<ItemDrop.ItemData>();
        private readonly HashSet<ItemDrop> _drops;
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly bool _autoPickup;
        private readonly Dictionary<string, string> _filterKeys = new Dictionary<string, string>();
        private readonly List<string> _material;
        private readonly List<string> _recipes;
        private readonly List<string> _trophies;
        private readonly List<string> _tutorials;
        private readonly List<string> _uniques;
        private readonly Dictionary<string, int> _stations;
        private readonly Dictionary<Skills.SkillType, KeyValuePair<float, float>> _skills = new Dictionary<Skills.SkillType, KeyValuePair<float, float>>();
        private readonly float _resourceRate;
        private readonly int _rows;
        private readonly bool _noCost;
        private readonly bool _consoleShown;
        private readonly int _guardCount;

        internal static readonly string[] FilterKeys =
        {
            FilterState.KeyVersion, FilterState.KeyMode, FilterState.KeyIgnored, FilterState.KeySelected,
        };

        internal Rig(Player player, string name)
        {
            Player = player;
            _name = name;
            Origin = player.transform.position;
            _rotation = player.transform.rotation;
            var fwd = player.transform.forward;
            fwd.y = 0f;
            Forward = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Right = Vector3.Cross(Vector3.up, Forward);
            _pitch = player.m_lookPitch;
            _yaw = player.m_lookYaw;
            _right = player.GetRightItem();
            _left = player.GetLeftItem();
            foreach (var item in player.GetInventory().GetAllItems())
            {
                _inventory.Add(new Slot { Item = item, Stack = item.m_stack, Pos = item.m_gridPos, Durability = item.m_durability });
                _inventorySet.Add(item);
            }
            _drops = new HashSet<ItemDrop>(ItemDrop.s_instances);
            _autoPickup = Player.m_enableAutoPickup;
            foreach (var key in FilterKeys)
            {
                if (player.m_customData.TryGetValue(key, out var value))
                {
                    _filterKeys[key] = value;
                }
            }
            _material = new List<string>(player.m_knownMaterial);
            _recipes = new List<string>(player.m_knownRecipes);
            _trophies = new List<string>(player.m_trophies);
            _tutorials = new List<string>(player.m_shownTutorials);
            _uniques = new List<string>(player.m_uniques);
            _stations = new Dictionary<string, int>(player.m_knownStations);
            foreach (var pair in player.m_skills.m_skillData)
            {
                _skills[pair.Key] = new KeyValuePair<float, float>(pair.Value.m_level, pair.Value.m_accumulator);
            }
            _resourceRate = Game.m_resourceRate;
            _rows = player.GetInventory().GetHeight();
            _noCost = player.m_noPlacementCost;
            _consoleShown = Console.instance != null && Console.instance.m_chatWindow.gameObject.activeSelf;
            _guardCount = GuardCount();

            // Neutral start, whatever the tester's config and character say.
            TestHooks.ClearInput();
            TestHooks.ClearSettings();
            // Mouse and keyboard in use, whatever device the tester touched last (controller tests say otherwise).
            TestHooks.PadActive = false;
            TestHooks.PadOnly = false;
            TestHooks.ExemptHarvest = true;
            TestHooks.GamepadControls = true;
            TestHooks.ShowMarkers = true;
            TestHooks.ShowInHoverText = true;
            TestHooks.ButtonOffsetX = 0f;
            TestHooks.ButtonOffsetY = 0f;
            TestHooks.DefaultIgnored = "";
            TestHooks.DefaultSelected = "";
            TestHooks.MarkKey = new KeyboardShortcut(KeyCode.Mouse2);
            FilterUi.CacheMarkKey(Plugin.MarkKeyNow);
            FilterUi.PlacementDirty = true;
            FilterUi.TestForgetMarkMessageTime();
            foreach (var key in FilterKeys)
            {
                player.m_customData.Remove(key);
            }
            FilterState.Reset();
            FilterState.EnsureLocal();
            HarvestGrace.Clear();
            Player.m_enableAutoPickup = true;
            TapMessages();
            BepInEx.Logging.Logger.Listeners.Add(Logs);
        }

        internal Inventory Inv => Player.GetInventory();

        // New PatchGuard reports since the test began (a patch body of this mod threw).
        internal int NewGuardReports => GuardCount() - _guardCount;

        internal ItemDrop.ItemData Give(string prefab, int stack = 1, int quality = 1)
        {
            return Inv.AddItem(prefab, stack, quality, 0, 0L, "", false);
        }

        // Point on the ground: f metres ahead of where the test began, r metres to the right.
        internal Vector3 Spot(float f, float r = 0f, float up = 0f)
        {
            var p = Origin + Forward * f + Right * r;
            p.y = GroundAt(p) + up;
            return p;
        }

        internal float GroundAt(Vector3 p)
        {
            var mask = LayerMask.GetMask("terrain", "static_solid", "Default", "piece");
            if (Physics.Raycast(new Vector3(p.x, Origin.y + 3f, p.z), Vector3.down, out var hit, 12f, mask, QueryTriggerInteraction.Ignore))
            {
                return hit.point.y;
            }
            return ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(p) : p.y;
        }

        internal GameObject Spawn(string prefabName, Vector3 pos, Quaternion rot)
        {
            var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefabName) : null;
            if (prefab == null)
            {
                return null;
            }
            var go = Object.Instantiate(prefab, pos, rot);
            _spawned.Add(go);
            return go;
        }

        // Item on the ground like the spawn console command make it.
        internal ItemDrop Drop(string prefab, int stack, Vector3 pos)
        {
            var item = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            if (item == null)
            {
                return null;
            }
            var go = Object.Instantiate(item, pos, Quaternion.identity);
            _spawned.Add(go);
            var drop = go.GetComponent<ItemDrop>();
            if (drop != null)
            {
                ItemDrop.OnCreateNew(drop);
                drop.SetStack(stack);
            }
            return drop;
        }

        internal void Track(GameObject go)
        {
            if (go != null)
            {
                _spawned.Add(go);
            }
        }

        // Thing a test handed to another owner (made-up player, the server) come back first: only its owner can
        // remove it from the world for everybody.
        internal void Destroy(GameObject go)
        {
            if (go == null || ZNetScene.instance == null)
            {
                return;
            }
            var view = go.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && !view.IsOwner())
            {
                view.ClaimOwnership();
            }
            ZNetScene.instance.Destroy(go);
        }

        // Item drops born since the test began (other tests' leftovers are not ours).
        internal List<ItemDrop> NewDrops(HashSet<ItemDrop> since = null)
        {
            var list = new List<ItemDrop>();
            foreach (var drop in ItemDrop.s_instances)
            {
                if (drop != null && !(since ?? _drops).Contains(drop))
                {
                    list.Add(drop);
                }
            }
            return list;
        }

        internal HashSet<ItemDrop> DropsNow() => new HashSet<ItemDrop>(ItemDrop.s_instances);

        // Player stand at p (walk there in one step).
        internal void MoveTo(Vector3 p)
        {
            Player.transform.position = p;
            if (Player.m_body != null)
            {
                Player.m_body.position = p;
                Player.m_body.linearVelocity = Vector3.zero;
            }
        }

        internal void Look(float pitch)
        {
            Player.m_lookPitch = pitch;
            Player.UpdateEyeRotation();
            Player.m_lookDir = Player.m_eye.forward;
        }

        // Body and look both along dir (flat), pitch degrees down. Attack turn the body to where the player LOOK
        // (Attack.Start), and camera yaw is often not the body's: without this a swing or throw go another way.
        internal void Face(Vector3 dir, float pitch = 0f)
        {
            dir.y = 0f;
            var rot = Quaternion.LookRotation(dir.sqrMagnitude > 1e-4f ? dir.normalized : Forward);
            Player.m_lookYaw = rot;
            Player.transform.rotation = rot;
            if (Player.m_body != null)
            {
                Player.m_body.rotation = rot;
            }
            Look(pitch);
            Physics.SyncTransforms();
        }

        internal void Restore()
        {
            Try("hooks", () =>
            {
                TestHooks.ClearInput();
                TestHooks.ClearSettings();
                FilterUi.CacheMarkKey(Plugin.MarkKeyNow);
                FilterUi.PlacementDirty = true;
                FilterState.BumpVersion();
            });
            Try("feature on", () =>
            {
                if (TestHooks.ForceOff)
                {
                    TestHooks.ForceOff = false;
                    FeatureRegistry.RefreshAll();
                }
            });
            Try("gui", () =>
            {
                UITooltip.HideTooltip();
                var gui = InventoryGui.instance;
                if (gui != null)
                {
                    gui.m_skillsDialog.gameObject.SetActive(false);
                    gui.m_trophiesPanel.SetActive(false);
                    if (InventoryGui.IsVisible())
                    {
                        gui.Hide();
                    }
                }
                if (Console.instance != null && !_consoleShown)
                {
                    Console.instance.m_chatWindow.gameObject.SetActive(false);
                }
                var es = EventSystem.current;
                if (es != null && es.currentSelectedGameObject != null && es.currentSelectedGameObject.name == ButtonObject)
                {
                    es.SetSelectedGameObject(null);
                }
            });
            Try("rows", () =>
            {
                if (Player.GetInventory().GetHeight() != _rows)
                {
                    Player.SetInventorySize(_rows);
                }
            });
            Try("spawned", () =>
            {
                for (var i = _spawned.Count - 1; i >= 0; i--)
                {
                    var go = _spawned[i];
                    if (go == null)
                    {
                        continue;
                    }
                    // Chest or station keep nothing when it go.
                    var container = go.GetComponentInChildren<Container>();
                    if (container != null && container.GetInventory() != null)
                    {
                        container.GetInventory().RemoveAll();
                    }
                    Destroy(go);
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
            Try("place", () =>
            {
                MoveTo(Origin);
                Player.transform.rotation = _rotation;
                Player.m_lookYaw = _yaw;
                Player.m_lookPitch = _pitch;
                Player.UpdateEyeRotation();
            });
            Try("auto pickup", () => Player.m_enableAutoPickup = _autoPickup);
            Try("world rates", () => Game.m_resourceRate = _resourceRate);
            Try("no cost", () => Player.m_noPlacementCost = _noCost);
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
                    slot.Item.m_durability = slot.Durability;
                    if (!inv.ContainsItem(slot.Item))
                    {
                        inv.AddItem(slot.Item, slot.Pos);
                    }
                    else
                    {
                        slot.Item.m_gridPos = slot.Pos;
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
            Try("known items", () =>
            {
                Reset(Player.m_knownMaterial, _material);
                Reset(Player.m_knownRecipes, _recipes);
                Reset(Player.m_trophies, _trophies);
                Reset(Player.m_shownTutorials, _tutorials);
                Reset(Player.m_uniques, _uniques);
                Player.m_knownStations.Clear();
                foreach (var pair in _stations)
                {
                    Player.m_knownStations[pair.Key] = pair.Value;
                }
            });
            Try("skills", () =>
            {
                var data = Player.m_skills.m_skillData;
                foreach (var type in data.Keys.ToArray())
                {
                    if (_skills.TryGetValue(type, out var saved))
                    {
                        data[type].m_level = saved.Key;
                        data[type].m_accumulator = saved.Value;
                    }
                    else
                    {
                        data.Remove(type);
                    }
                }
            });
            Try("filter data", () =>
            {
                foreach (var key in FilterKeys)
                {
                    Player.m_customData.Remove(key);
                }
                foreach (var pair in _filterKeys)
                {
                    Player.m_customData[pair.Key] = pair.Value;
                }
                FilterState.Reset();
                HarvestGrace.Clear();
                FilterUi.TestForgetMarkMessageTime();
            });
            Try("taps", () =>
            {
                BepInEx.Logging.Logger.Listeners.Remove(Logs);
                UntapMessages();
            });
        }

        private static void Reset(HashSet<string> set, List<string> saved)
        {
            set.Clear();
            foreach (var s in saved)
            {
                set.Add(s);
            }
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

    // ---------------------------------------------------------------- drops and pickup

    private static bool Alive(ItemDrop drop) => drop != null && drop.gameObject != null;

    private static int AliveCount(IEnumerable<ItemDrop> drops) => drops.Count(Alive);

    private static int StackTotal(IEnumerable<ItemDrop> drops) => drops.Where(Alive).Sum(d => d.m_itemData.m_stack);

    // Wait until every drop is gone (picked up) or time is up.
    private static IEnumerator WaitGone(IList<ItemDrop> drops, float seconds)
    {
        var end = Time.time + seconds;
        while (Time.time < end && AliveCount(drops) > 0)
        {
            yield return null;
        }
    }

    // Wait until a new item drop is born (after "since"), then two frames more so the whole batch is here.
    private static IEnumerator WaitNewDrops(Rig rig, HashSet<ItemDrop> since, float seconds, List<ItemDrop> into)
    {
        var end = Time.time + seconds;
        while (Time.time < end && rig.NewDrops(since).Count == 0)
        {
            yield return null;
        }
        yield return null;
        yield return null;
        into.Clear();
        into.AddRange(rig.NewDrops(since));
    }

    // Player walk over each drop still on the ground (one step each), up to seconds. Drops the filter let through are
    // picked on the way.
    private static IEnumerator WalkOver(Rig rig, IList<ItemDrop> drops, float seconds)
    {
        var end = Time.time + seconds;
        while (Time.time < end)
        {
            ItemDrop next = null;
            foreach (var drop in drops)
            {
                if (Alive(drop))
                {
                    next = drop;
                    break;
                }
            }
            if (next == null)
            {
                break;
            }
            var p = next.transform.position;
            p.y = rig.GroundAt(p);
            rig.MoveTo(p);
            yield return new WaitForSeconds(0.35f);
        }
        rig.MoveTo(rig.Origin);
        yield return null;
    }

    // Player stand on the drop for a while: true = drop still there after.
    private static IEnumerator StandOn(Rig rig, ItemDrop drop, float seconds)
    {
        if (Alive(drop))
        {
            var p = drop.transform.position;
            p.y = rig.GroundAt(p);
            rig.MoveTo(p);
        }
        yield return new WaitForSeconds(seconds);
    }

    // Use key on an item on the ground (vanilla Player.Interact, like E).
    private static IEnumerator UsePickup(Rig rig, ItemDrop drop, float seconds = 2f)
    {
        if (!Alive(drop))
        {
            yield break;
        }
        rig.Player.Interact(drop.gameObject, false, false);
        var end = Time.time + seconds;
        while (Time.time < end && Alive(drop))
        {
            yield return null;
        }
    }

    private static string HoverOf(ItemDrop drop) => Alive(drop) ? drop.GetHoverText() ?? "" : "";

    // Real attack with the weapon in hand. Game refuse it while a pick up / equip animation of the step before still
    // play (Humanoid.StartAttack: InMinorAction): ask each frame until it start, up to seconds. started[0] = it did.
    private static IEnumerator StartAttackSoon(Player player, bool secondary, float seconds, bool[] started)
    {
        started[0] = false;
        var end = Time.time + seconds;
        while (Time.time < end)
        {
            if (!player.InAttack() && player.StartAttack(null, secondary))
            {
                started[0] = true;
                yield break;
            }
            yield return null;
        }
    }

    // ---------------------------------------------------------------- inventory screen

    // Open the inventory (or a chest) and wait until the panel stopped moving.
    private static IEnumerator Open(Container container = null)
    {
        var gui = InventoryGui.instance;
        gui.Show(container);
        var last = new Vector3(float.NaN, 0f, 0f);
        var lastScale = -1f;
        var steady = 0;
        var end = Time.realtimeSinceStartup + 4f;
        while (Time.realtimeSinceStartup < end && steady < 6)
        {
            yield return null;
            var pos = gui.m_player.position;
            var scale = gui.m_player.lossyScale.x;
            steady = pos == last && Mathf.Abs(scale - lastScale) < 1e-4f && scale > 0.5f ? steady + 1 : 0;
            last = pos;
            lastScale = scale;
        }
        yield return null;
    }

    private static IEnumerator Close()
    {
        var gui = InventoryGui.instance;
        if (gui != null)
        {
            gui.Hide();
        }
        for (var i = 0; i < 4; i++)
        {
            yield return null;
        }
    }

    // shownOnly false = also the button whose panel is still switched off (frame of the inventory opening).
    private static Button FindButton(bool shownOnly = true)
    {
        var gui = InventoryGui.instance;
        if (gui == null)
        {
            return null;
        }
        var t = Utils.FindChild(gui.transform, ButtonObject);
        return t != null && (!shownOnly || t.gameObject.activeInHierarchy) ? t.GetComponent<Button>() : null;
    }

    // Box around every slot of a grid, in screen pixels.
    private static Rect SlotArea(InventoryGrid grid)
    {
        var first = WorldRect((RectTransform)grid.m_elements[0].transform);
        var last = WorldRect((RectTransform)grid.m_elements[grid.m_elements.Count - 1].transform);
        return Rect.MinMaxRect(Mathf.Min(first.xMin, last.xMin), Mathf.Min(first.yMin, last.yMin),
            Mathf.Max(first.xMax, last.xMax), Mathf.Max(first.yMax, last.yMax));
    }

    // Average colour a mark draw: opaque pixels of its sprite, times the image tint. Sprite texture is not readable
    // from code, so it go through the graphics card (blit, read back). False = nothing to read.
    private static bool MarkColour(Image badge, out Color colour)
    {
        colour = default;
        var sprite = badge != null ? badge.sprite : null;
        var tex = sprite != null ? sprite.texture : null;
        if (tex == null)
        {
            return false;
        }
        var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
        var before = RenderTexture.active;
        Texture2D copy = null;
        try
        {
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0f, 0f, tex.width, tex.height), 0, 0);
            copy.Apply();
            float r = 0f, g = 0f, b = 0f;
            var n = 0;
            foreach (var p in copy.GetPixels())
            {
                if (p.a < 0.5f)
                {
                    continue;
                }
                r += p.r;
                g += p.g;
                b += p.b;
                n++;
            }
            if (n == 0)
            {
                return false;
            }
            var tint = badge.color;
            colour = new Color(r / n * tint.r, g / n * tint.g, b / n * tint.b, tint.a);
            return true;
        }
        finally
        {
            RenderTexture.active = before;
            RenderTexture.ReleaseTemporary(rt);
            if (copy != null)
            {
                Object.Destroy(copy);
            }
        }
    }

    private static string LabelOf(Button button)
    {
        var label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
        return label != null ? label.text : "";
    }

    private static string TooltipOf(Button button)
    {
        var tip = button != null ? button.GetComponent<UITooltip>() : null;
        return tip != null ? tip.m_text ?? "" : "";
    }

    private static Rect WorldRect(RectTransform rt)
    {
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        return Rect.MinMaxRect(Mathf.Min(corners[0].x, corners[2].x), Mathf.Min(corners[0].y, corners[2].y),
            Mathf.Max(corners[0].x, corners[2].x), Mathf.Max(corners[0].y, corners[2].y));
    }

    // Box of the drawn letters of a text (its rect is often the whole slot). False = nothing drawn.
    private static bool GlyphRect(TMP_Text text, out Rect rect)
    {
        rect = default;
        if (text == null || !text.enabled || !text.gameObject.activeInHierarchy || string.IsNullOrEmpty(text.text))
        {
            return false;
        }
        text.ForceMeshUpdate();
        if (text.textInfo == null || text.textInfo.characterCount == 0)
        {
            return false;
        }
        var b = text.textBounds;
        var a = text.transform.TransformPoint(b.min);
        var c = text.transform.TransformPoint(b.max);
        rect = Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));
        return rect.width > 0.5f && rect.height > 0.5f;
    }

    // First thing the UI raycast hit at this screen point (what a real mouse click would land on).
    private static GameObject TopHit(Vector2 screen)
    {
        var es = EventSystem.current;
        if (es == null)
        {
            return null;
        }
        var ped = new PointerEventData(es) { position = screen };
        var hits = new List<RaycastResult>();
        es.RaycastAll(ped, hits);
        return hits.Count > 0 ? hits[0].gameObject : null;
    }

    // Left click on the button through Button.OnPointerClick (so "not interactable" really block it).
    private static void Click(Button button)
    {
        var es = EventSystem.current;
        var ped = new PointerEventData(es) { button = PointerEventData.InputButton.Left };
        ExecuteEvents.Execute(button.gameObject, ped, ExecuteEvents.pointerClickHandler);
    }

    private static InventoryElement ElementOf(InventoryGrid grid, ItemDrop.ItemData item)
    {
        if (grid == null || item == null)
        {
            return null;
        }
        var idx = item.m_gridPos.y * grid.m_width + item.m_gridPos.x;
        return idx >= 0 && idx < grid.m_elements.Count ? grid.m_elements[idx] : null;
    }

    private static Image BadgeOf(InventoryGrid grid, ItemDrop.ItemData item)
    {
        var element = ElementOf(grid, item);
        var t = element != null ? element.transform.Find(MarkObject) : null;
        return t != null ? t.GetComponent<Image>() : null;
    }

    private static bool BadgeOn(InventoryGrid grid, ItemDrop.ItemData item, string sprite = null)
    {
        var badge = BadgeOf(grid, item);
        return badge != null && badge.enabled && badge.gameObject.activeInHierarchy
               && (sprite == null || (badge.sprite != null && badge.sprite.name == sprite));
    }

    // Badges shown in a grid now.
    private static int BadgesOn(InventoryGrid grid)
    {
        var n = 0;
        if (grid == null)
        {
            return 0;
        }
        foreach (var element in grid.m_elements)
        {
            var t = element != null ? element.transform.Find(MarkObject) : null;
            var image = t != null ? t.GetComponent<Image>() : null;
            if (image != null && image.enabled)
            {
                n++;
            }
        }
        return n;
    }

    // Mouse pointer on the item's slot + mark key pressed (TestHooks): the real gesture path run next frame.
    private static IEnumerator MarkGesture(InventoryGrid grid, ItemDrop.ItemData item)
    {
        var element = ElementOf(grid, item);
        if (element != null)
        {
            TestHooks.Pointer = WorldRect(element.transform as RectTransform).center;
            TestHooks.PressMark();
        }
        yield return null;
        yield return null;
        TestHooks.Pointer = null;
    }

    // Controller: right stick click with your grid selection on the item's slot.
    private static IEnumerator StickGesture(InventoryGrid grid, ItemDrop.ItemData item)
    {
        if (item != null)
        {
            grid.SetGamepadSelection(item.m_gridPos);
        }
        TestHooks.PressStick();
        yield return null;
        yield return null;
    }

    // ---------------------------------------------------------------- chat and console

    // Run a command line in a terminal (chat or console); give back the lines it printed.
    private static List<string> Run(Terminal terminal, string line)
    {
        var buffer = terminal.m_chatBuffer;
        var before = buffer.Count;
        var last = before > 0 ? buffer[before - 1] : null;
        terminal.TryRunCommand(line);
        var result = new List<string>();
        // Buffer drop old lines past 300: then count stay, so take from the old last line.
        var start = before;
        if (buffer.Count < before || (buffer.Count == before && before > 0 && !ReferenceEquals(buffer[before - 1], last)))
        {
            start = Mathf.Max(0, buffer.LastIndexOf(last) + 1);
        }
        for (var i = start; i < buffer.Count; i++)
        {
            result.Add(buffer[i]);
        }
        return result;
    }

    private static bool AnyLine(List<string> lines, string start) =>
        lines.Any(l => l != null && l.StartsWith(start, StringComparison.Ordinal));

    private static bool AnyLineHas(List<string> lines, string part) =>
        lines.Any(l => l != null && l.IndexOf(part, StringComparison.Ordinal) >= 0);

    private static string Lines(List<string> lines) => lines.Count == 0 ? "(nothing)" : string.Join(" / ", lines.ToArray());

    private static bool SameSet(HashSet<string> set, params string[] keys) => set.SetEquals(keys);
#endif
}
