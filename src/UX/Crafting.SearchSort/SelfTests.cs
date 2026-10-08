using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
#endif

namespace MC.UX.CraftingSearchSortMod;

// Debug build only (calls vanish in Release). In-world self tests, run by world probe (tools/Test-InWorld.ps1,
// -Mod Crafting.SearchSort) and one multiplayer client test (tools/Test-Multiplayer.ps1, scenario modded).
// Each test spawn its own station 3.5 m in front of player (use range made large, roof and fire not needed, on that
// copy only), open it with the vanilla calls, drive our row like a player (field text, click events through the UI
// event system, ZInput game buttons through ButtonDef.Press), and check game state. Tests by TESTING.md item:
//   csearch.layout     T01  row at every station and plain inventory: place, clip, nothing covered, scroll range
//   csearch.search     T02 T03 T04  filter = what vanilla requirement list show, case and spaces
//   csearch.select     T05 T06 T11  no match, selection kept or first row, right-click clear
//   csearch.sort       T13 T14 T16 T17  category / weapon type first (stable), search + sort, reset
//   csearch.byname     T15  Name (A-Z), greyed rows mixed in
//   csearch.menu       T12  sort menu content, highlight, close paths (button, tab, B, E, Tab), W still walk
//   csearch.typing     T07  field has keyboard: game buttons do nothing, chest stay open
//   csearch.binds      T08  console bind call skipped while typing
//   csearch.focus      T09  Esc / Enter leave the field: text and filter stay, keys held one more frame
//   csearch.focuskey   T10  focus key (stand in), console open, Use / Inventory on same key, empty key
//   csearch.badkey     T34  key the game cannot read: one warning, rest work
//   csearch.memory     T18  sort per station in character data, RememberSort off and on
//   csearch.keeptext   T19  search cleared on close, KeepSearchText
//   csearch.upgrade    T20  upgrade tab at the forge
//   csearch.potential  T32  Forge of Potential
//   csearch.food       T21  food stations (notes for the eyes)
//   csearch.tools      T22  tools and torch, row dump lines
//   csearch.bug.tankard T22 REAL BUG, alone: tankard land under Tools and light, README and T22 say Other
//   csearch.craft     T23 M02  craft with filter, x5, type during craft, crafted item is plain
//   csearch.nocost     T24  nocost list
//   csearch.console    T25  filtercraft / sortcraft with us
//   csearch.language   T26  language change
//   csearch.click      T27  click reach field and Sort button in plain inventory
//   csearch.gamepad    T28  D-pad never land on ours, B leave field / close menu
//   csearch.toggle     T29 T30 T33 M03  what OnDeactivated / OnActivated do, in use
//   csearch.repair     C01  One Click Repair All keep filter and sort
//   csearch.hidden     T35  game close inventory while typing (teleport, station gone)
//   csearch.rightclick T36  right-click clear leave UI navigation on (own small test: believed broken)
//   csearch.keys       T08 T10 T12  REAL key events (Unity Input System): need game window focused, say so if not
//   csearch.log        T31  no error line of ours since start (last test)
//   csearch.mp.station M01 M02  on a dedicated server: search, sort, craft, drop
//   csearch.mp.remember / .keeptext / .focuskey  T18 T19 T34 T10  the REAL settings changed (ConfigEntry.Value): only
//                      in the multiplayer run, where config files are throwaway
// Single player: me never write config: RememberSort / KeepSearchText forced in memory (Plugin.ReadRememberSort /
// ReadKeepSearchText), focus key press faked on one frame (CraftSearch.FocusKeyDown), focus key setting through
// CraftSearch.ReadFocusKey.
// Rig put back: inventory, known recipes / materials / stations, Crafting skill, saved sorts, world key, console
// filter and sort, language, place, and destroy what it spawn.
internal static class SelfTests
{
    private const string LayoutName = "csearch.layout";
    private const string SearchName = "csearch.search";
    private const string SelectName = "csearch.select";
    private const string SortName = "csearch.sort";
    private const string ByNameName = "csearch.byname";
    private const string MenuName = "csearch.menu";
    private const string TypingName = "csearch.typing";
    private const string BindsName = "csearch.binds";
    private const string FocusName = "csearch.focus";
    private const string FocusKeyName = "csearch.focuskey";
    private const string BadKeyName = "csearch.badkey";
    private const string MemoryName = "csearch.memory";
    private const string KeepTextName = "csearch.keeptext";
    private const string UpgradeName = "csearch.upgrade";
    private const string PotentialName = "csearch.potential";
    private const string FoodName = "csearch.food";
    private const string ToolsName = "csearch.tools";
    private const string BugTankardName = "csearch.bug.tankard";
    private const string CraftName = "csearch.craft";
    private const string NoCostName = "csearch.nocost";
    private const string ConsoleName = "csearch.console";
    private const string LanguageName = "csearch.language";
    private const string ClickName = "csearch.click";
    private const string GamepadName = "csearch.gamepad";
    private const string ToggleName = "csearch.toggle";
    private const string RepairName = "csearch.repair";
    private const string HiddenName = "csearch.hidden";
    private const string RightClickName = "csearch.rightclick";
    private const string KeysName = "csearch.keys";
    private const string LogName = "csearch.log";
    private const string MpStationName = "csearch.mp.station";
    private const string MpRememberName = "csearch.mp.remember";
    private const string MpKeepTextName = "csearch.mp.keeptext";
    private const string MpFocusKeyName = "csearch.mp.focuskey";

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        LogWatch.Install();
        foreach (var test in Tests())
        {
            SelfTest.Register(test.Key, test.Value);
        }
        SelfTest.RegisterMultiplayer(MpStationName, SelfTest.Modded, RunMpStation);
        SelfTest.RegisterMultiplayer(MpRememberName, SelfTest.Modded, RunMpRemember);
        SelfTest.RegisterMultiplayer(MpKeepTextName, SelfTest.Modded, RunMpKeepText);
        SelfTest.RegisterMultiplayer(MpFocusKeyName, SelfTest.Modded, RunMpFocusKey);
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        foreach (var test in Tests())
        {
            SelfTest.Unregister(test.Key);
        }
        SelfTest.UnregisterMultiplayer(MpStationName);
        SelfTest.UnregisterMultiplayer(MpRememberName);
        SelfTest.UnregisterMultiplayer(MpKeepTextName);
        SelfTest.UnregisterMultiplayer(MpFocusKeyName);
        ClearOverrides();
#endif
    }

#if DEBUG
    // ---------- hooks (memory only) ----------

    // Set = Plugin.ReadRememberSort / ReadKeepSearchText answer this, not the config value.
    internal static bool? RememberSortOverride;
    internal static bool? KeepSearchTextOverride;

    // Frame on which CraftSearch.FocusKeyDown say "key went down" (stand in for the keyboard). -1 = never.
    internal static int FocusKeyFrame = -1;

    private static void ClearOverrides()
    {
        RememberSortOverride = null;
        KeepSearchTextOverride = null;
        FocusKeyFrame = -1;
    }

    // Registration order = run order. Log test last: it look back on everything before it.
    private static KeyValuePair<string, Func<IEnumerator>>[] Tests()
    {
        return new[]
        {
            new KeyValuePair<string, Func<IEnumerator>>(LayoutName, RunLayout),
            new KeyValuePair<string, Func<IEnumerator>>(SearchName, RunSearch),
            new KeyValuePair<string, Func<IEnumerator>>(SelectName, RunSelect),
            new KeyValuePair<string, Func<IEnumerator>>(SortName, RunSort),
            new KeyValuePair<string, Func<IEnumerator>>(ByNameName, RunByName),
            new KeyValuePair<string, Func<IEnumerator>>(MenuName, RunMenu),
            new KeyValuePair<string, Func<IEnumerator>>(TypingName, RunTyping),
            new KeyValuePair<string, Func<IEnumerator>>(BindsName, RunBinds),
            new KeyValuePair<string, Func<IEnumerator>>(FocusName, RunFocus),
            new KeyValuePair<string, Func<IEnumerator>>(FocusKeyName, RunFocusKey),
            new KeyValuePair<string, Func<IEnumerator>>(BadKeyName, RunBadKey),
            new KeyValuePair<string, Func<IEnumerator>>(MemoryName, RunMemory),
            new KeyValuePair<string, Func<IEnumerator>>(KeepTextName, RunKeepText),
            new KeyValuePair<string, Func<IEnumerator>>(UpgradeName, RunUpgrade),
            new KeyValuePair<string, Func<IEnumerator>>(PotentialName, RunPotential),
            new KeyValuePair<string, Func<IEnumerator>>(FoodName, RunFood),
            new KeyValuePair<string, Func<IEnumerator>>(ToolsName, RunTools),
            new KeyValuePair<string, Func<IEnumerator>>(BugTankardName, RunBugTankard),
            new KeyValuePair<string, Func<IEnumerator>>(CraftName, RunCraft),
            new KeyValuePair<string, Func<IEnumerator>>(NoCostName, RunNoCost),
            new KeyValuePair<string, Func<IEnumerator>>(ConsoleName, RunConsole),
            new KeyValuePair<string, Func<IEnumerator>>(LanguageName, RunLanguage),
            new KeyValuePair<string, Func<IEnumerator>>(ClickName, RunClick),
            new KeyValuePair<string, Func<IEnumerator>>(GamepadName, RunGamepad),
            new KeyValuePair<string, Func<IEnumerator>>(ToggleName, RunToggle),
            new KeyValuePair<string, Func<IEnumerator>>(RepairName, RunRepair),
            new KeyValuePair<string, Func<IEnumerator>>(HiddenName, RunHidden),
            new KeyValuePair<string, Func<IEnumerator>>(RightClickName, RunRightClick),
            new KeyValuePair<string, Func<IEnumerator>>(KeysName, RunKeys),
            new KeyValuePair<string, Func<IEnumerator>>(LogName, RunLog),
        };
    }

    // Names SearchUi give its objects.
    private const string RowObject = "MC_CraftSearchRow";
    private const string FieldObject = "MC_CraftSearchField";
    private const string SortButtonObject = "MC_CraftSortButton";
    private const string MenuObject = "MC_CraftSortMenu";
    private const string OptionPrefix = "MC_SortOption_";

    // Gap SearchUi leave between row and list (its private const).
    private const float RowGap = 4f;

    private const string RepairGuid = "MC.Crafting.Repair.OneClickAll";
    private const string IdolsGuid = "MC.Crafting.Forge.IdolUpgrades";

    private static readonly string[] AllStations =
    {
        "piece_stonecutter", "piece_workbench", "forge", "blackforge", "piece_magetable", "piece_artisanstation",
        "piece_cauldron", "piece_MeadCauldron", "piece_preptable",
    };

    private static readonly Vector3[] Corners = new Vector3[4];

    // ---------- log watch ----------

    // Me listen to BepInEx log from mod start: keep error and warning lines that are ours (own source, or text name
    // us: exception from our MonoBehaviour come through "Unity Log"). Own "[selftest]" lines left out. Debug lines of
    // ours only while a test ask (KeepDebug). Log events can come from any thread: me lock.
    private sealed class LogWatch : BepInEx.Logging.ILogListener
    {
        internal static readonly LogWatch Instance = new LogWatch();
        private static bool _installed;

        private const int Max = 200;
        private readonly object _gate = new object();
        private readonly List<string> _errors = new List<string>();
        private readonly List<string> _warnings = new List<string>();
        private readonly List<string> _debug = new List<string>();
        private int _errorCount;
        private int _warningCount;

        internal volatile bool KeepDebug;

        internal static void Install()
        {
            if (_installed)
            {
                return;
            }
            BepInEx.Logging.Logger.Listeners.Add(Instance);
            _installed = true;
        }

        public void LogEvent(object sender, BepInEx.Logging.LogEventArgs eventArgs)
        {
            try
            {
                if (eventArgs == null)
                {
                    return;
                }
                var level = eventArgs.Level;
                var isError = (level & (BepInEx.Logging.LogLevel.Error | BepInEx.Logging.LogLevel.Fatal)) != 0;
                var isWarning = (level & BepInEx.Logging.LogLevel.Warning) != 0;
                var isDebug = KeepDebug && (level & BepInEx.Logging.LogLevel.Debug) != 0;
                if (!isError && !isWarning && !isDebug)
                {
                    return;
                }
                var text = eventArgs.Data as string ?? eventArgs.Data?.ToString() ?? "";
                var ours = eventArgs.Source != null && eventArgs.Source.SourceName == ModInfo.Name;
                if (!ours && text.IndexOf("MC.UX.Crafting", StringComparison.Ordinal) < 0
                    && text.IndexOf(ModInfo.Name, StringComparison.Ordinal) < 0)
                {
                    return;
                }
                if (text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal))
                {
                    return; // FAIL lines of self tests (ours or the probe's about us)
                }
                lock (_gate)
                {
                    if (isError)
                    {
                        _errorCount++;
                        Keep(_errors, text);
                    }
                    else if (isWarning)
                    {
                        _warningCount++;
                        Keep(_warnings, text);
                    }
                    else if (ours)
                    {
                        Keep(_debug, text);
                    }
                }
            }
            catch
            {
                // Me never throw inside logger.
            }
        }

        private static void Keep(List<string> list, string text)
        {
            if (list.Count < Max)
            {
                list.Add(text);
            }
        }

        public void Dispose()
        {
        }

        internal int ErrorCount
        {
            get
            {
                lock (_gate)
                {
                    return _errorCount;
                }
            }
        }

        internal int WarningCount
        {
            get
            {
                lock (_gate)
                {
                    return _warningCount;
                }
            }
        }

        internal List<string> Errors()
        {
            lock (_gate)
            {
                return new List<string>(_errors);
            }
        }

        internal List<string> Warnings()
        {
            lock (_gate)
            {
                return new List<string>(_warnings);
            }
        }

        internal List<string> TakeDebug()
        {
            lock (_gate)
            {
                var copy = new List<string>(_debug);
                _debug.Clear();
                return copy;
            }
        }

        // Warnings with this text among those logged after the first "from" ones.
        internal int WarningsWith(string part, int from)
        {
            lock (_gate)
            {
                var n = 0;
                for (var i = Math.Max(0, from); i < _warnings.Count; i++)
                {
                    if (_warnings[i].IndexOf(part, StringComparison.Ordinal) >= 0)
                    {
                        n++;
                    }
                }
                return n;
            }
        }

        internal int KeptWarnings
        {
            get
            {
                lock (_gate)
                {
                    return _warnings.Count;
                }
            }
        }
    }

    // ---------- small helpers ----------

    private sealed class Checks
    {
        private readonly string _name;
        private readonly List<string> _failures = new List<string>();
        private int _count;
        private bool _complete;
        private bool _reported;

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

        // Test reached its natural end.
        internal void Complete() => _complete = true;

        // Called from finally: exactly one PASS or FAIL, also after a timeout or a throw.
        internal void Report()
        {
            if (_reported)
            {
                return;
            }
            _reported = true;
            if (_failures.Count > 0)
            {
                SelfTest.Fail(_name, $"{_failures.Count} of {_count} checks failed: {string.Join("; ", _failures.ToArray())}");
            }
            else if (!_complete)
            {
                SelfTest.Fail(_name, $"stopped before its end (timeout or exception) after {_count} checks");
            }
            else
            {
                SelfTest.Pass(_name, $"{_count} checks OK");
            }
        }
    }

    private sealed class Box<T>
    {
        internal T Value;
    }

    private static string Loc(string token) => Localization.instance.Localize(token) ?? "";

    // Own normaliser (not RecipeTerms'): lower case, every white space gone.
    private static string Norm(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "";
        }
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.ToLowerInvariant())
        {
            if (!char.IsWhiteSpace(ch))
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    private static string F(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static string F(Rect r) => $"x {F(r.xMin)}..{F(r.xMax)} y {F(r.yMin)}..{F(r.yMax)}";

    private static bool Ready(string test)
    {
        if (Player.m_localPlayer == null || InventoryGui.instance == null || ZNetScene.instance == null
            || ObjectDB.instance == null || ZoneSystem.instance == null)
        {
            SelfTest.Fail(test, "no local player, inventory window or world");
            return false;
        }
        if (!CraftSearch.Active)
        {
            SelfTest.Fail(test, "the mod is not active");
            return false;
        }
        return true;
    }

    private static IEnumerator Frames(int n)
    {
        for (var i = 0; i < n; i++)
        {
            yield return null;
        }
    }

    // Wait (real time) until cond, at most seconds. At least one frame.
    private static IEnumerator Until(Func<bool> cond, float seconds)
    {
        var until = Time.realtimeSinceStartup + seconds;
        do
        {
            yield return null;
        }
        while (!cond() && Time.realtimeSinceStartup < until);
    }

    // ---------- rig ----------

    // Me hold what a test change (player, world, mod state) as undo steps. Done run them newest first, in finally
    // (no yield there). Things every test may touch are kept at start: place, known lists, inventory, Crafting skill,
    // saved sorts, UI navigation flag. Forced settings start at the defaults whatever the player's config say.
    private sealed class Rig
    {
        internal readonly string Test;
        internal readonly Player P;
        internal readonly Inventory Inv;
        internal readonly InventoryGui Gui;
        internal readonly Vector3 Home;
        internal readonly Vector3 HomeForward;
        internal bool ModTouched; // test called CraftSearch.Deactivate: Done turn it on again

        private readonly Quaternion _homeRotation;
        private readonly Quaternion _homeYaw;
        private readonly List<KeyValuePair<string, Action>> _undo = new List<KeyValuePair<string, Action>>();
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<ZInput.ButtonDef> _pressed = new List<ZInput.ButtonDef>();

        internal Rig(string test)
        {
            Test = test;
            P = Player.m_localPlayer;
            Gui = InventoryGui.instance;
            Inv = P.GetInventory();
            Home = P.transform.position;
            _homeRotation = P.transform.rotation;
            _homeYaw = P.m_lookYaw;
            var forward = P.transform.forward;
            forward.y = 0f;
            HomeForward = forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward;

            KeepKnown();
            KeepInventory();
            KeepSkill();
            KeepSortMemory();
            KeepNavigation();
            KeepTabs();
            RememberSortOverride = true;
            KeepSearchTextOverride = false;
            // Items me give or a pressed Inventory button would call the tutorial raven: count them as seen for the
            // test (known lists are put back after).
            foreach (var tutorial in new[] { "inventory", "hammer", "hoe", "bellfragment", "pickaxe", "shield", "mold", "sacrificialblood", "boss_trophy", "wishbone", "ore", "food", "trinket" })
            {
                P.m_shownTutorials.Add(tutorial);
            }
        }

        internal void Undo(string what, Action action) => _undo.Add(new KeyValuePair<string, Action>(what, action));

        private void KeepKnown()
        {
            var recipes = new List<string>(P.m_knownRecipes);
            var materials = new List<string>(P.m_knownMaterial);
            var stations = new Dictionary<string, int>(P.m_knownStations);
            var tutorials = new List<string>(P.m_shownTutorials);
            Undo("known lists", () =>
            {
                P.m_knownRecipes.Clear();
                P.m_knownRecipes.UnionWith(recipes);
                P.m_knownMaterial.Clear();
                P.m_knownMaterial.UnionWith(materials);
                P.m_knownStations.Clear();
                foreach (var pair in stations)
                {
                    P.m_knownStations[pair.Key] = pair.Value;
                }
                P.m_shownTutorials.Clear();
                P.m_shownTutorials.UnionWith(tutorials);
            });
        }

        // Whole inventory back: same item objects, same stacks, quality, durability, slots. What a test gave or
        // crafted is gone, what it used is back.
        private void KeepInventory()
        {
            var items = new List<ItemDrop.ItemData>(Inv.GetAllItems());
            var stacks = items.Select(i => i.m_stack).ToArray();
            var qualities = items.Select(i => i.m_quality).ToArray();
            var durabilities = items.Select(i => i.m_durability).ToArray();
            var slots = items.Select(i => i.m_gridPos).ToArray();
            Undo("inventory", () =>
            {
                var list = Inv.m_inventory;
                foreach (var item in list.ToArray())
                {
                    if (!items.Contains(item) && P.IsItemEquiped(item))
                    {
                        P.UnequipItem(item, false);
                    }
                }
                list.Clear();
                for (var i = 0; i < items.Count; i++)
                {
                    items[i].m_stack = stacks[i];
                    items[i].m_quality = qualities[i];
                    items[i].m_durability = durabilities[i];
                    items[i].m_gridPos = slots[i];
                    list.Add(items[i]);
                }
                Inv.Changed();
            });
        }

        private void KeepSkill()
        {
            var data = P.m_skills.m_skillData;
            var had = data.TryGetValue(Skills.SkillType.Crafting, out var skill);
            var level = had ? skill.m_level : 0f;
            var accumulator = had ? skill.m_accumulator : 0f;
            Undo("Crafting skill", () =>
            {
                if (!had)
                {
                    data.Remove(Skills.SkillType.Crafting);
                }
                else if (data.TryGetValue(Skills.SkillType.Crafting, out var now))
                {
                    now.m_level = level;
                    now.m_accumulator = accumulator;
                }
            });
        }

        private void KeepSortMemory()
        {
            var had = P.m_customData.TryGetValue(SortMemory.Key, out var value);
            Undo("saved sorts", () =>
            {
                if (had)
                {
                    P.m_customData[SortMemory.Key] = value;
                }
                else
                {
                    P.m_customData.Remove(SortMemory.Key);
                }
                ClearOverrides();
                // Sort in memory of the mod follow the character data again (station me was at last).
                CraftSearch.OnRememberSortChanged();
            });
        }

        private void KeepNavigation()
        {
            var es = EventSystem.current;
            if (es == null)
            {
                return;
            }
            var navigation = es.sendNavigationEvents;
            Undo("UI navigation", () =>
            {
                if (EventSystem.current != null)
                {
                    EventSystem.current.sendNavigationEvents = navigation;
                }
            });
        }

        // Craft / Upgrade tab the panel was on (the game keeps it between stations): as found after.
        private void KeepTabs()
        {
            if (Gui.m_tabCraft == null || Gui.m_tabUpgrade == null)
            {
                return;
            }
            var craft = Gui.m_tabCraft.interactable;
            var upgrade = Gui.m_tabUpgrade.interactable;
            Undo("tabs", () =>
            {
                Gui.m_tabCraft.interactable = craft;
                Gui.m_tabUpgrade.interactable = upgrade;
            });
        }

        // Crafting skill 0: no bonus item from a craft (the game rolls for one from the skill). Put back by the rig.
        internal void NoCraftBonus()
        {
            if (P.m_skills.m_skillData.TryGetValue(Skills.SkillType.Crafting, out var skill))
            {
                skill.m_level = 0f;
                skill.m_accumulator = 0f;
            }
        }

        // No saved sort for any station: each opens on Default.
        internal void ForgetSorts()
        {
            P.m_customData.Remove(SortMemory.Key);
            CraftSearch.OnRememberSortChanged();
        }

        // World key AllRecipesUnlocked on (every recipe of the station listed), off again after when it was off.
        internal IEnumerator UnlockAllRecipes(Checks c)
        {
            var zone = ZoneSystem.instance;
            if (!zone.GetGlobalKey(GlobalKeys.AllRecipesUnlocked))
            {
                Undo("world key AllRecipesUnlocked", () => ZoneSystem.instance.RemoveGlobalKey(GlobalKeys.AllRecipesUnlocked));
                zone.SetGlobalKey(GlobalKeys.AllRecipesUnlocked);
                yield return Until(() => zone.GetGlobalKey(GlobalKeys.AllRecipesUnlocked), 5f);
            }
            c.Check(zone.GetGlobalKey(GlobalKeys.AllRecipesUnlocked), "setup: world key AllRecipesUnlocked not set");
        }

        // Console "filtercraft" list empty for the test, as before after.
        internal void ClearCraftFilter()
        {
            var old = new List<string>(Player.s_FilterCraft);
            Undo("filtercraft", () => Player.s_FilterCraft = old);
            Player.s_FilterCraft = new List<string>();
        }

        // Console "sortcraft" key as before after.
        internal void KeepCraftSort()
        {
            var had = P.TryGetUniqueKeyValue("sortcraft", out var value);
            Undo("sortcraft", () =>
            {
                P.RemoveUniqueKeyValue("sortcraft");
                if (had)
                {
                    P.AddUniqueKeyValue("sortcraft", value);
                }
            });
            P.RemoveUniqueKeyValue("sortcraft");
        }

        internal void NoCost(bool on)
        {
            var old = P.NoCostCheat();
            Undo("nocost", () => P.SetNoPlacementCost(old));
            P.SetNoPlacementCost(on);
        }

        // Focus key as the mod read it (memory only); config value read again after.
        internal void FocusKey(KeyboardShortcut shortcut)
        {
            if (!_focusKeyKept)
            {
                _focusKeyKept = true;
                Undo("focus key", () => CraftSearch.ReadFocusKey(Plugin.FocusSearchKey.Value));
            }
            CraftSearch.ReadFocusKey(shortcut);
        }

        private bool _focusKeyKept;

        internal ItemDrop.ItemData Give(string prefab, int amount, int quality = 1)
        {
            return Inv.AddItem(prefab, amount, quality, 0, 0L, "", false);
        }

        // Every recipe made at this station known to the player (what a progressed character has). Known lists are
        // put back by the rig.
        internal int TeachRecipesOf(CraftingStation station)
        {
            var n = 0;
            foreach (var recipe in ObjectDB.instance.m_recipes)
            {
                if (recipe == null || !recipe.m_enabled || recipe.m_item == null || recipe.m_craftingStation == null
                    || recipe.m_craftingStation.m_name != station.m_name)
                {
                    continue;
                }
                if (P.m_knownRecipes.Add(recipe.m_item.m_itemData.m_shared.m_name))
                {
                    n++;
                }
            }
            return n;
        }

        internal void Spawned(GameObject go) => _spawned.Add(go);

        internal void DestroySpawned()
        {
            foreach (var go in _spawned)
            {
                if (go != null)
                {
                    ZNetScene.instance.Destroy(go);
                }
            }
            _spawned.Clear();
        }

        internal void Pressed(ZInput.ButtonDef button)
        {
            if (!_pressed.Contains(button))
            {
                _pressed.Add(button);
            }
        }

        internal void GoHome()
        {
            P.transform.SetPositionAndRotation(Home, _homeRotation);
            var body = P.m_body;
            if (body != null)
            {
                body.position = Home;
                body.rotation = _homeRotation;
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                }
            }
            P.m_lookYaw = _homeYaw;
            P.SetLookDir(_homeYaw * Vector3.forward);
        }

        internal void Done()
        {
            Try("buttons", () =>
            {
                foreach (var button in _pressed)
                {
                    button.ResetState();
                }
                _pressed.Clear();
            });
            Try("window", () =>
            {
                SearchUi.CloseMenu();
                SearchUi.DropFocus();
                if (Gui != null && InventoryGui.IsVisible())
                {
                    Gui.Hide();
                }
                if (P != null)
                {
                    P.SetCraftingStation(null);
                }
            });
            Try("spawned", DestroySpawned);
            for (var i = _undo.Count - 1; i >= 0; i--)
            {
                Try(_undo[i].Key, _undo[i].Value);
            }
            _undo.Clear();
            Try("messages", () =>
            {
                // "New recipe / material" pop-ups queued by what me gave: not over the next test's screenshots.
                if (MessageHud.instance != null)
                {
                    MessageHud.instance.ClearUnlockQueue();
                }
            });
            Try("mod", () =>
            {
                ClearOverrides();
                if (ModTouched || !CraftSearch.Active)
                {
                    CraftSearch.Activate();
                }
                var field = SearchUi.Field;
                if (field != null && field.text.Length > 0)
                {
                    field.text = "";
                }
            });
            Try("place", () =>
            {
                if (P != null)
                {
                    GoHome();
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
                SelfTest.Note(Test, $"clean-up of {what} failed: {e.GetType().Name}: {e.Message}");
            }
        }
    }

    // ---------- stations and window ----------

    private static bool Shown(InventoryGui gui) => gui != null && gui.m_animator != null && gui.m_animator.GetBool("visible");

    // Spawn station in front of where the test started (side = metres to the right), make this copy usable from
    // anywhere near and without roof or fire, then open it like CraftingStation.Interact does. box = null: no prefab.
    private static IEnumerator Open(Rig rig, string prefabName, Box<CraftingStation> box, float settle = 0.4f, float side = 0f)
    {
        box.Value = null;
        var prefab = ZNetScene.instance.GetPrefab(prefabName);
        if (prefab == null)
        {
            yield break;
        }
        var right = Vector3.Cross(Vector3.up, rig.HomeForward);
        var pos = rig.Home + rig.HomeForward * 3.5f + right * side;
        pos.y = ZoneSystem.instance.GetGroundHeight(pos);
        var go = Object.Instantiate(prefab, pos, Quaternion.LookRotation(-rig.HomeForward));
        rig.Spawned(go);
        var station = go.GetComponentInChildren<CraftingStation>();
        if (station == null)
        {
            yield break;
        }
        station.m_useDistance = 50f;
        station.m_craftRequireRoof = false;
        station.m_craftRequireFire = false;
        var wear = go.GetComponent<WearNTear>();
        if (wear != null)
        {
            wear.enabled = false; // no support check: piece stand on bare ground
        }
        yield return new WaitForSeconds(0.3f);
        box.Value = station;
        yield return Reopen(rig, station, settle);
    }

    // Open the window at a station me spawned before (null = plain inventory, player grid active).
    private static IEnumerator Reopen(Rig rig, CraftingStation station, float settle = 0.4f)
    {
        // Craft tab, whatever tab the last station left (a station without Craft tab switches by itself).
        if (rig.Gui.m_tabCraft != null && rig.Gui.m_tabUpgrade != null)
        {
            rig.Gui.m_tabCraft.interactable = false;
            rig.Gui.m_tabUpgrade.interactable = true;
        }
        rig.P.SetCraftingStation(station);
        rig.Gui.Show(null, station != null ? 3 : 1);
        yield return new WaitForSecondsRealtime(settle);
        yield return null;
    }

    private static IEnumerator Close(Rig rig)
    {
        if (Shown(rig.Gui))
        {
            rig.Gui.Hide();
        }
        rig.P.SetCraftingStation(null);
        // IsVisible stay true two frames after Hide.
        yield return Frames(4);
    }

    // ---------- our objects ----------

    private static RectTransform RowOf()
    {
        var field = SearchUi.Field;
        return field != null ? field.transform.parent as RectTransform : null;
    }

    private static RectTransform SortButton()
    {
        var row = RowOf();
        return row != null ? row.Find(SortButtonObject) as RectTransform : null;
    }

    private static string SortLabel()
    {
        var button = SortButton();
        var text = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
        return text != null ? text.text : null;
    }

    // Menu object alive and shown (a closed one is hidden at once, destroyed at end of frame).
    private static RectTransform MenuOf(InventoryGui gui)
    {
        var parent = gui.m_crafting;
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name == MenuObject && child.gameObject.activeSelf)
            {
                return child as RectTransform;
            }
        }
        return null;
    }

    private static bool IsOurs(Transform t, InventoryGui gui)
    {
        var row = RowOf();
        var menu = MenuOf(gui);
        return (row != null && t.IsChildOf(row)) || (menu != null && t.IsChildOf(menu));
    }

    private static ScrollRect ScrollOf(InventoryGui gui)
    {
        ScrollRect scroll = null;
        if (gui.m_recipeEnsureVisible != null)
        {
            scroll = gui.m_recipeEnsureVisible.GetComponent<ScrollRect>();
        }
        if (scroll == null && gui.m_recipeListRoot != null)
        {
            scroll = gui.m_recipeListRoot.GetComponentInParent<ScrollRect>();
        }
        return scroll;
    }

    private static RectTransform ViewOf(ScrollRect scroll) => scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;

    // What really cut the rows: nearest mask above the list content (inside the ScrollRect), else the view.
    private static RectTransform ClipOf(InventoryGui gui, ScrollRect scroll)
    {
        var t = gui.m_recipeListRoot.parent;
        while (t != null)
        {
            var mask = t.GetComponent<Mask>();
            var mask2 = t.GetComponent<RectMask2D>();
            if ((mask != null && mask.enabled) || (mask2 != null && mask2.enabled))
            {
                return t as RectTransform;
            }
            if (t == scroll.transform)
            {
                break;
            }
            t = t.parent;
        }
        return ViewOf(scroll);
    }

    private static Rect RectIn(RectTransform rt, Transform space)
    {
        rt.GetWorldCorners(Corners);
        var a = space.InverseTransformPoint(Corners[0]);
        var b = space.InverseTransformPoint(Corners[2]);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    private static bool Overlap(Rect a, Rect b, float eps)
    {
        return a.xMin < b.xMax - eps && b.xMin < a.xMax - eps && a.yMin < b.yMax - eps && b.yMin < a.yMax - eps;
    }

    // Vertical length from one transform's units to another's.
    private static float ConvertY(Transform from, Transform to, float dy)
    {
        return Mathf.Abs(to.InverseTransformVector(from.TransformVector(new Vector3(0f, dy, 0f))).y);
    }

    private static bool AtTop(InventoryGui gui, ScrollRect scroll)
    {
        Canvas.ForceUpdateCanvases();
        if (scroll.verticalNormalizedPosition >= 0.999f)
        {
            return true;
        }
        var space = gui.m_crafting;
        return Mathf.Abs(RectIn(gui.m_recipeListRoot, space).yMax - RectIn(ViewOf(scroll), space).yMax) <= 1f;
    }

    // Scroll list to its end. False = list fit in the view, nothing to scroll.
    private static bool ScrollToEnd(InventoryGui gui, ScrollRect scroll)
    {
        Canvas.ForceUpdateCanvases();
        var space = gui.m_crafting;
        if (RectIn(gui.m_recipeListRoot, space).height <= RectIn(ViewOf(scroll), space).height + 1f)
        {
            return false;
        }
        scroll.verticalNormalizedPosition = 0f;
        return true;
    }

    private static Vector2 ScreenCentre(RectTransform rt)
    {
        var canvas = rt.GetComponentInParent<Canvas>();
        var root = canvas != null ? canvas.rootCanvas : null;
        var cam = root == null || root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        rt.GetWorldCorners(Corners);
        return RectTransformUtility.WorldToScreenPoint(cam, (Corners[0] + Corners[2]) * 0.5f);
    }

    // Object the mouse would hit at the centre of rt (all raycasters, like the input module ask).
    private static GameObject TopHit(RectTransform rt)
    {
        var es = EventSystem.current;
        if (es == null)
        {
            return null;
        }
        var data = new PointerEventData(es) { position = ScreenCentre(rt) };
        var results = new List<RaycastResult>();
        es.RaycastAll(data, results);
        return results.Count > 0 ? results[0].gameObject : null;
    }

    private static string PathOf(GameObject go)
    {
        if (go == null)
        {
            return "nothing";
        }
        var t = go.transform;
        var path = t.name;
        for (var i = 0; i < 3 && t.parent != null; i++)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    // Mouse click the way the input module send it: press goes to first handler from target upward, release to it,
    // click to the click handler. Return the object that got the click (null = nobody).
    private static GameObject SendClick(GameObject target, PointerEventData.InputButton button)
    {
        var es = EventSystem.current;
        if (es == null || target == null)
        {
            return null;
        }
        var screen = target.transform is RectTransform rt ? ScreenCentre(rt) : Vector2.zero;
        var data = new PointerEventData(es)
        {
            button = button, position = screen, pressPosition = screen, clickCount = 1, eligibleForClick = true,
        };
        var pressed = ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerDownHandler);
        var clicked = ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
        data.pointerPress = pressed != null ? pressed : clicked;
        data.rawPointerPress = target;
        if (data.pointerPress != null)
        {
            ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);
        }
        if (clicked != null)
        {
            ExecuteEvents.Execute(clicked, data, ExecuteEvents.pointerClickHandler);
        }
        return clicked;
    }

    // ---------- typing and focus ----------

    // Text into the field like typing it (TMP fire onValueChanged), then wait for the mod's own delayed refresh.
    private static IEnumerator Type(string text)
    {
        var field = SearchUi.Field;
        if (field == null)
        {
            yield break;
        }
        field.text = text;
        yield return Until(() => !CraftSearch.Pending, 2f);
        yield return null;
    }

    // Cursor into the field, then two more frames: controller raise the key guards.
    private static IEnumerator Focus(Checks c, string label)
    {
        var field = SearchUi.Field;
        if (field == null)
        {
            c.Check(false, $"{label}: no search field");
            yield break;
        }
        SearchUi.FocusField();
        yield return Until(() => field.isFocused, 1f);
        yield return Frames(2);
        c.Check(field.isFocused, $"{label}: the search field did not take the cursor");
    }

    private static IEnumerator Unfocus()
    {
        SearchUi.DropFocus();
        yield return Frames(5);
    }

    // "Focus key went down" on the next frame (stand in for the keyboard), then time for LateUpdate to act.
    private static IEnumerator PressFocusKey()
    {
        FocusKeyFrame = Time.frameCount + 1;
        yield return Frames(4);
        FocusKeyFrame = -1;
    }

    // ---------- game buttons ----------

    private static ZInput.ButtonDef Button(string name)
    {
        var input = ZInput.instance;
        if (input == null)
        {
            return null;
        }
        return input.m_buttons.TryGetValue(name, out var button) ? button : null;
    }

    // Game button down then up. ButtonDef.Press is what ZInput's own input callback call: every game read of
    // ZInput.GetButtonDown / GetButton see it (one frame "down", held while pressed). No keyboard event involved.
    private static IEnumerator Tap(Rig rig, string name)
    {
        var button = Button(name);
        if (button == null)
        {
            yield break;
        }
        rig.Pressed(button);
        button.Press();
        yield return Frames(2);
        button.Release();
        yield return Frames(4);
    }

    // Same, with the focus key going down on the same frame (one physical key bound to both).
    private static IEnumerator TapWithFocusKey(Rig rig, string name)
    {
        var button = Button(name);
        if (button == null)
        {
            yield break;
        }
        rig.Pressed(button);
        button.Press();
        FocusKeyFrame = Time.frameCount + 1;
        yield return Frames(2);
        button.Release();
        FocusKeyFrame = -1;
        yield return Frames(4);
    }

    private static IEnumerator Hold(Rig rig, string name, float seconds)
    {
        var button = Button(name);
        if (button == null)
        {
            yield break;
        }
        rig.Pressed(button);
        button.Press();
        yield return new WaitForSeconds(seconds);
        button.Release();
        yield return Frames(3);
    }

    private static float Flat(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // ---------- rows ----------

    private static List<InventoryGui.RecipeDataPair> Rows(InventoryGui gui) => new List<InventoryGui.RecipeDataPair>(gui.m_availableRecipes);

    private static bool Named(InventoryGui.RecipeDataPair row)
    {
        return row.Recipe != null && row.Recipe.m_item != null && row.Recipe.m_item.m_itemData != null
               && row.Recipe.m_item.m_itemData.m_shared != null;
    }

    private static ItemDrop.ItemData.SharedData Shared(InventoryGui.RecipeDataPair row) => row.Recipe.m_item.m_itemData.m_shared;

    private static bool Same(InventoryGui.RecipeDataPair a, InventoryGui.RecipeDataPair b)
    {
        return ReferenceEquals(a.Recipe, b.Recipe) && ReferenceEquals(a.ItemData, b.ItemData);
    }

    private static bool SameOrder(List<InventoryGui.RecipeDataPair> a, List<InventoryGui.RecipeDataPair> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }
        for (var i = 0; i < a.Count; i++)
        {
            if (!Same(a[i], b[i]))
            {
                return false;
            }
        }
        return true;
    }

    private static bool Contains(List<InventoryGui.RecipeDataPair> rows, InventoryGui.RecipeDataPair row)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (Same(rows[i], row))
            {
                return true;
            }
        }
        return false;
    }

    // Every row of part is in whole, in the same order.
    private static bool IsSubsequence(List<InventoryGui.RecipeDataPair> part, List<InventoryGui.RecipeDataPair> whole)
    {
        var j = 0;
        for (var i = 0; i < part.Count; i++)
        {
            while (j < whole.Count && !Same(whole[j], part[i]))
            {
                j++;
            }
            if (j >= whole.Count)
            {
                return false;
            }
            j++;
        }
        return true;
    }

    private static string RowName(InventoryGui.RecipeDataPair row)
    {
        if (!Named(row))
        {
            return "(foreign row)";
        }
        return row.ItemData != null ? $"{row.Recipe.m_item.name}[q{row.ItemData.m_quality}]" : row.Recipe.m_item.name;
    }

    private static string Names(List<InventoryGui.RecipeDataPair> rows, int max = 10)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < rows.Count && i < max; i++)
        {
            sb.Append(i > 0 ? ", " : "").Append(RowName(rows[i]));
        }
        if (rows.Count > max)
        {
            sb.Append(", ... (").Append(rows.Count).Append(')');
        }
        return sb.ToString();
    }

    private static int IndexOf(List<InventoryGui.RecipeDataPair> rows, string prefab)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (Named(rows[i]) && rows[i].Recipe.m_item.name == prefab)
            {
                return i;
            }
        }
        return -1;
    }

    private static int IndexOf(InventoryGui gui, string prefab) => IndexOf(gui.m_availableRecipes, prefab);

    private static int IndexOfItem(List<InventoryGui.RecipeDataPair> rows, ItemDrop.ItemData item)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (ReferenceEquals(rows[i].ItemData, item))
            {
                return i;
            }
        }
        return -1;
    }

    private static bool Marked(InventoryGui gui, int index)
    {
        var rows = gui.m_availableRecipes;
        if (index < 0 || index >= rows.Count || rows[index].InterfaceElement == null)
        {
            return false;
        }
        var mark = rows[index].InterfaceElement.transform.Find("selected");
        return mark != null && mark.gameObject.activeSelf;
    }

    private static int MarkedCount(InventoryGui gui)
    {
        var n = 0;
        for (var i = 0; i < gui.m_availableRecipes.Count; i++)
        {
            if (Marked(gui, i))
            {
                n++;
            }
        }
        return n;
    }

    // Rows one under the other from the top, content as tall as the rows need (or the base size).
    private static void CheckPlacement(Checks c, InventoryGui gui, string label)
    {
        var rows = gui.m_availableRecipes;
        var bad = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            var go = rows[i].InterfaceElement;
            if (go == null || !(go.transform is RectTransform rt) || !go.activeSelf
                || Mathf.Abs(rt.anchoredPosition.y + i * gui.m_recipeListSpace) > 0.01f)
            {
                bad++;
            }
        }
        c.Check(bad == 0, $"{label}: {bad} of {rows.Count} rows are not at their place in the list (gap or overlap)");
        var want = Mathf.Max(gui.m_recipeListBaseSize, rows.Count * gui.m_recipeListSpace);
        c.Check(Mathf.Abs(gui.m_recipeListRoot.rect.height - want) <= 0.5f,
            $"{label}: list content is {F(gui.m_recipeListRoot.rect.height)} tall, {rows.Count} rows need {F(want)}");
    }

    // Why this row match the term, as a player can see after clicking it: its name, internal names, or an ingredient
    // in the requirement list vanilla build for it (InventoryGui.SetupRequirementList itself; paging ignored).
    // Null = no match. Caller put gui.m_selectedRecipe back.
    private static string MatchReason(InventoryGui gui, Player player, InventoryGui.RecipeDataPair row, string term)
    {
        if (!Named(row))
        {
            return "foreign row (never filtered)";
        }
        var shared = Shared(row);
        if (Norm(Loc(shared.m_name)).Contains(term))
        {
            return "name";
        }
        if (Norm(row.Recipe.m_item.name).Contains(term))
        {
            return "internal name";
        }
        var recipeName = row.Recipe.name ?? "";
        if (recipeName.StartsWith("Recipe_", StringComparison.OrdinalIgnoreCase))
        {
            recipeName = recipeName.Substring("Recipe_".Length);
        }
        if (Norm(recipeName).Contains(term))
        {
            return "recipe name";
        }
        gui.m_selectedRecipe = row;
        var quality = row.ItemData == null ? 1 : row.ItemData.m_quality + 1;
        gui.SetupRequirementList(quality, player, true, 1);
        foreach (var req in gui.m_reqList)
        {
            if (req != null && req.m_resItem != null && Norm(Loc(req.m_resItem.m_itemData.m_shared.m_name)).Contains(term))
            {
                return "ingredient " + req.m_resItem.name;
            }
        }
        return null;
    }

    // Rows of "all" that match term (same order as all).
    private static List<InventoryGui.RecipeDataPair> Matching(Rig rig, List<InventoryGui.RecipeDataPair> all, string term)
    {
        var gui = rig.Gui;
        var saved = gui.m_selectedRecipe;
        var result = new List<InventoryGui.RecipeDataPair>();
        foreach (var row in all)
        {
            if (MatchReason(gui, rig.P, row, term) != null)
            {
                result.Add(row);
            }
        }
        gui.m_selectedRecipe = saved;
        return result;
    }

    // The shown list hold exactly the rows of "all" that match term: none wrongly kept, none wrongly dropped.
    // Return the matching rows (order of all).
    private static List<InventoryGui.RecipeDataPair> CheckFilter(Checks c, Rig rig, List<InventoryGui.RecipeDataPair> all, string term, string label)
    {
        var expected = Matching(rig, all, term);
        var now = Rows(rig.Gui);
        var wronglyKept = now.Where(r => !Contains(expected, r)).ToList();
        var wronglyDropped = expected.Where(r => !Contains(now, r)).ToList();
        c.Check(wronglyKept.Count == 0, $"{label}: {wronglyKept.Count} row(s) listed that show '{term}' nowhere: {Names(wronglyKept, 5)}");
        c.Check(wronglyDropped.Count == 0, $"{label}: {wronglyDropped.Count} matching row(s) missing: {Names(wronglyDropped, 5)}");
        return expected;
    }

    // ---------- categories ----------

    // Options of a row by the mod's own table (ItemKinds + RecipeCategory.Of): first always set, second -1 = none.
    private static void OptionsOf(InventoryGui.RecipeDataPair row, out int first, out int second)
    {
        second = -1;
        if (!Named(row))
        {
            first = RecipeCategory.Other;
            return;
        }
        var shared = Shared(row);
        RecipeCategory.Of(ItemKinds.Classify(shared), shared.m_skillType, out first, out second);
    }

    private static bool InOption(InventoryGui.RecipeDataPair row, int option)
    {
        OptionsOf(row, out var first, out var second);
        return first == option || second == option;
    }

    // Rows of the option first, the rest after, both in the order they had.
    private static List<InventoryGui.RecipeDataPair> Partition(List<InventoryGui.RecipeDataPair> rows, Func<InventoryGui.RecipeDataPair, bool> member)
    {
        var result = rows.Where(member).ToList();
        result.AddRange(rows.Where(r => !member(r)));
        return result;
    }

    private static void Recount(List<InventoryGui.RecipeDataPair> rows, int[] counts, Sprite[] icons)
    {
        foreach (var row in rows)
        {
            OptionsOf(row, out var first, out var second);
            var icon = Named(row) ? row.Recipe.m_item.m_itemData.GetIcon() : null;
            counts[first]++;
            if (icons[first] == null)
            {
                icons[first] = icon;
            }
            if (second >= 0)
            {
                counts[second]++;
                if (icons[second] == null)
                {
                    icons[second] = icon;
                }
            }
        }
    }

    private static string Histogram(int[] counts)
    {
        var sb = new StringBuilder();
        for (var o = 0; o < counts.Length; o++)
        {
            if (counts[o] > 0)
            {
                sb.Append(sb.Length > 0 ? ", " : "").Append(RecipeCategory.Id(o)).Append(' ').Append(counts[o]);
            }
        }
        return sb.ToString();
    }

    // Weapon family option -> its skill, from the saved id ("w_swords" -> Swords).
    private static bool FamilySkill(int option, out Skills.SkillType skill)
    {
        skill = Skills.SkillType.None;
        var id = RecipeCategory.Id(option);
        return id.StartsWith("w_", StringComparison.Ordinal) && Enum.TryParse(id.Substring(2), true, out skill);
    }

    // Pick an option in the menu like a player: click Sort, click the entry. ok = entry was there.
    private static IEnumerator Choose(Rig rig, int option, Box<bool> ok)
    {
        ok.Value = false;
        var button = SortButton();
        if (button == null)
        {
            yield break;
        }
        if (!SearchUi.MenuOpen)
        {
            SendClick(button.gameObject, PointerEventData.InputButton.Left);
        }
        var menu = MenuOf(rig.Gui);
        var entry = menu != null ? menu.Find(OptionPrefix + RecipeCategory.Id(option)) : null;
        if (entry == null)
        {
            SearchUi.CloseMenu();
            yield break;
        }
        ok.Value = SendClick(entry.gameObject, PointerEventData.InputButton.Left) != null;
        yield return Frames(3);
    }

    // Menu as the item describe it: Default, Name, and only the categories the full tab list has, each with its
    // count and the icon of its first row, the active one highlighted, drawn over the list inside the panel.
    private static void CheckMenu(Checks c, InventoryGui gui, List<InventoryGui.RecipeDataPair> fullList, int active, string label)
    {
        var menu = MenuOf(gui);
        if (!c.Check(SearchUi.MenuOpen && menu != null, $"{label}: the sort menu is not open"))
        {
            return;
        }
        c.Check(menu.parent == gui.m_crafting && menu.GetSiblingIndex() == menu.parent.childCount - 1,
            $"{label}: the menu is not the last child of the crafting panel (it would be drawn under something)");
        var bounds = gui.m_crafting.rect;
        var area = RectIn(menu, gui.m_crafting);
        c.Check(area.xMin >= bounds.xMin - 0.5f && area.xMax <= bounds.xMax + 0.5f && area.yMin >= bounds.yMin - 0.5f && area.yMax <= bounds.yMax + 0.5f,
            $"{label}: the menu ({F(area)}) reaches outside the crafting panel ({F(bounds)})");

        var counts = new int[RecipeCategory.Count];
        var icons = new Sprite[RecipeCategory.Count];
        Recount(fullList, counts, icons);
        var expected = new List<int>();
        for (var o = 0; o < RecipeCategory.Count; o++)
        {
            if (o == RecipeCategory.Default || o == RecipeCategory.Name || counts[o] > 0 || o == active)
            {
                expected.Add(o);
            }
        }
        var entries = new List<Transform>();
        for (var i = 0; i < menu.childCount; i++)
        {
            var child = menu.GetChild(i);
            if (child.name.StartsWith(OptionPrefix, StringComparison.Ordinal) && child.gameObject.activeSelf)
            {
                entries.Add(child);
            }
        }
        var listed = string.Join(", ", entries.Select(e => e.name.Substring(OptionPrefix.Length)).ToArray());
        var wanted = string.Join(", ", expected.Select(RecipeCategory.Id).ToArray());
        c.Check(listed == wanted, $"{label}: menu lists [{listed}], the list holds [{wanted}]");

        var problems = new List<string>();
        foreach (var entry in entries)
        {
            var id = entry.name.Substring(OptionPrefix.Length);
            var option = RecipeCategory.FromId(id);
            var category = option != RecipeCategory.Default && option != RecipeCategory.Name;
            var nameText = entry.Find("name") != null ? entry.Find("name").GetComponent<TMP_Text>() : null;
            var want = category ? RecipeCategory.Label(option) + " (" + counts[option] + ")" : RecipeCategory.Label(option);
            if (nameText == null || nameText.text != want)
            {
                problems.Add($"{id}: text '{(nameText != null ? nameText.text : "none")}', expected '{want}'");
            }
            var icon = entry.Find("icon") != null ? entry.Find("icon").GetComponent<Image>() : null;
            if (icon == null || (category ? !icon.enabled || icon.sprite != icons[option] || icon.sprite == null : icon.enabled))
            {
                problems.Add($"{id}: icon is not the one of its first recipe");
            }
            var mark = entry.Find("selected");
            if (mark == null || mark.gameObject.activeSelf != (option == active))
            {
                problems.Add($"{id}: highlight {(option == active ? "missing" : "on a row that is not the active sort")}");
            }
            if (entry.GetComponent<Button>() == null || entry.Find("Durability") == null || entry.Find("QualityLevel") == null
                || entry.Find("Durability").gameObject.activeSelf || entry.Find("QualityLevel").gameObject.activeSelf)
            {
                problems.Add($"{id}: not built like a plain recipe row (durability bar and level hidden)");
            }
            var er = RectIn((RectTransform)entry, gui.m_crafting);
            if (er.xMin < area.xMin - 0.5f || er.xMax > area.xMax + 0.5f || er.yMin < area.yMin - 0.5f || er.yMax > area.yMax + 0.5f)
            {
                problems.Add($"{id}: outside the menu");
            }
        }
        c.Check(problems.Count == 0, $"{label}: menu entries: {string.Join(", ", problems.ToArray())}");
    }

    // ---------- csearch.layout (T01) ----------

    private static IEnumerator RunLayout()
    {
        const string T = LayoutName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            yield return rig.UnlockAllRecipes(c);
            rig.ForgetSorts();
            rig.ClearCraftFilter();

            // Like right after game start: no row, vanilla layout. Then the first list ever built is the short one
            // (stonecutter), and row + layout are made by the list postfix itself (not by Activate).
            rig.ModTouched = true;
            CraftSearch.Deactivate();
            CraftSearch.Active = true;
            var firstDone = false;
            foreach (var name in AllStations)
            {
                var box = new Box<CraftingStation>();
                yield return Open(rig, name, box, 0.6f);
                if (c.Check(box.Value != null, $"station '{name}' could not be spawned (no such prefab, or no CraftingStation on it)"))
                {
                    if (!firstDone)
                    {
                        c.Check(SearchUi.Field != null, $"{name} opened first: the list build did not make the search row");
                    }
                    yield return CheckLayout(c, rig, name, box.Value);
                    if (!firstDone)
                    {
                        firstDone = true;
                        CraftSearch.Activate(); // language hook back; row is there already
                        yield return null;
                    }
                }
                yield return Close(rig);
                rig.DestroySpawned();
            }
            if (!firstDone)
            {
                CraftSearch.Activate();
            }
            yield return Reopen(rig, null, 0.6f);
            yield return CheckLayout(c, rig, "plain inventory", null);
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    private static IEnumerator CheckLayout(Checks c, Rig rig, string label, CraftingStation station)
    {
        var gui = rig.Gui;
        Canvas.ForceUpdateCanvases();
        var field = SearchUi.Field;
        var row = RowOf();
        if (!c.Check(row != null && row.gameObject.activeInHierarchy && Shown(gui), $"{label}: no search row shown"))
        {
            yield break;
        }
        var space = gui.m_crafting;
        var button = SortButton();
        c.Check(row.name == RowObject && field.name == FieldObject && field.gameObject.activeInHierarchy && field.interactable,
            $"{label}: search field missing or not usable");
        c.Check(button != null && button.gameObject.activeInHierarchy, $"{label}: Sort button missing");
        c.Check(SortLabel() == "Sort: Default", $"{label}: button reads '{SortLabel()}', expected 'Sort: Default'");
        c.Check(field.text == "" && CraftSearch.Term == "", $"{label}: search field not empty ('{field.text}')");
        var scroll = ScrollOf(gui);
        if (!c.Check(scroll != null, $"{label}: recipe list has no ScrollRect"))
        {
            yield break;
        }
        var scrollRT = (RectTransform)scroll.transform;
        var view = ViewOf(scroll);
        var clip = ClipOf(gui, scroll);
        var r = RectIn(row, space);
        var s = RectIn(scrollRT, space);
        var cl = RectIn(clip, space);
        var rowHeight = ConvertY(scrollRT.parent, space, gui.m_recipeListSpace > 1f ? gui.m_recipeListSpace : 30f);
        var gap = ConvertY(scrollRT.parent, space, RowGap);

        // One row, directly above the list, as wide as the list.
        c.Check(r.yMin >= s.yMax - 0.5f && r.yMin - s.yMax <= gap + 1f,
            $"{label}: row ({F(r)}) is not directly above the list ({F(s)})");
        c.Check(Mathf.Abs(r.xMin - s.xMin) <= 1f && Mathf.Abs(r.xMax - s.xMax) <= 1f, $"{label}: row ({F(r)}) is not as wide as the list ({F(s)})");
        c.Check(Mathf.Abs(r.height - rowHeight) <= 1f, $"{label}: row is {F(r.height)} high, one recipe row is {F(rowHeight)}");
        if (button != null)
        {
            var fr = RectIn((RectTransform)field.transform, space);
            var br = RectIn(button, space);
            c.Check(fr.xMin >= r.xMin - 0.5f && fr.xMax <= br.xMin + 0.5f && br.xMax <= r.xMax + 0.5f && fr.width > 30f && br.width > 30f
                    && fr.yMin >= r.yMin - 0.5f && fr.yMax <= r.yMax + 0.5f && br.yMin >= r.yMin - 0.5f && br.yMax <= r.yMax + 0.5f,
                $"{label}: field ({F(fr)}) and Sort button ({F(br)}) do not sit side by side inside the row ({F(r)})");
        }

        // No recipe row can be drawn in or behind the row, whatever the scroll: the list is cut at or below it.
        c.Check(cl.yMax <= r.yMin + 0.5f, $"{label}: the list is drawn up to y {F(cl.yMax)}, into the search row (bottom at {F(r.yMin)})");

        // Nothing vanilla covered.
        var covered = new List<string>();
        var controls = new List<KeyValuePair<string, Component>>
        {
            new KeyValuePair<string, Component>("Craft tab", gui.m_tabCraft),
            new KeyValuePair<string, Component>("Upgrade tab", gui.m_tabUpgrade),
            new KeyValuePair<string, Component>("repair button", gui.m_repairButton),
            new KeyValuePair<string, Component>("station icon", gui.m_craftingStationIcon),
            new KeyValuePair<string, Component>("craft button", gui.m_craftButton),
        };
        foreach (var control in controls)
        {
            if (control.Value != null && control.Value.gameObject.activeInHierarchy && control.Value.transform is RectTransform rt
                && Overlap(r, RectIn(rt, space), 0.5f))
            {
                covered.Add($"{control.Key} ({F(RectIn(rt, space))})");
            }
        }
        c.Check(covered.Count == 0, $"{label}: the row ({F(r)}) covers: {string.Join(", ", covered.ToArray())}");
        if (gui.m_craftingStationName != null && Overlap(r, RectIn(gui.m_craftingStationName.rectTransform, space), 0.5f))
        {
            // Text box can be larger than its letters: for the eyes (screenshot).
            c.Note($"{label}: the station name's text box ({F(RectIn(gui.m_craftingStationName.rectTransform, space))}) overlaps the row ({F(r)}): check the screenshot");
        }
        if (station != null)
        {
            c.Check(gui.m_craftingStationName != null && gui.m_craftingStationName.gameObject.activeInHierarchy
                    && gui.m_craftingStationName.text == Loc(station.m_name), $"{label}: station name not shown");
        }

        // First row fully visible under the row; scrolling ends at the last recipe, and a short list cannot scroll.
        scroll.verticalNormalizedPosition = 1f;
        Canvas.ForceUpdateCanvases();
        yield return null;
        var rows = gui.m_availableRecipes;
        cl = RectIn(clip, space);
        r = RectIn(row, space);
        var v = RectIn(view, space);
        var content = RectIn(gui.m_recipeListRoot, space);
        // One list slot (row + spacing) in panel units; what the rows need in all.
        var slot = ConvertY(gui.m_recipeListRoot, space, gui.m_recipeListSpace);
        var needed = rows.Count * slot;
        if (rows.Count > 0 && rows[0].InterfaceElement != null)
        {
            var first = RectIn((RectTransform)rows[0].InterfaceElement.transform, space);
            c.Check(first.yMax <= r.yMin + 0.5f && first.yMax <= cl.yMax + 0.5f && first.yMin >= cl.yMin - 0.5f && cl.yMax - first.yMax <= slot * 0.5f,
                $"{label}: first recipe row ({F(first)}) is not fully visible right under the search row (list shown {F(cl)})");
        }
        if (needed <= v.height + 0.5f)
        {
            c.Check(content.height <= v.height + 0.5f,
                $"{label}: {rows.Count} recipe(s) fit in the list but it can be scrolled {F(content.height - v.height)} into blank space");
        }
        else
        {
            c.Check(Mathf.Abs(content.height - needed) <= 1f, $"{label}: scrolling goes {F(content.height - needed)} past the last recipe");
        }
        CheckPlacement(c, gui, label);
        if (rows.Count > 0 && content.height > v.height + 0.5f)
        {
            scroll.verticalNormalizedPosition = 0f;
            Canvas.ForceUpdateCanvases();
            yield return null;
            rows = gui.m_availableRecipes;
            if (rows.Count > 0 && rows[rows.Count - 1].InterfaceElement != null)
            {
                var last = RectIn((RectTransform)rows[rows.Count - 1].InterfaceElement.transform, space);
                cl = RectIn(clip, space);
                c.Check(last.yMin >= cl.yMin - 0.5f && last.yMax <= cl.yMax + 0.5f && last.yMin - cl.yMin <= slot,
                    $"{label}: scrolled to the end, the last recipe ({F(last)}) is not at the bottom of the list ({F(cl)})");
            }
            scroll.verticalNormalizedPosition = 1f;
        }
        c.Note($"{label}: {rows.Count} rows, row {F(RectIn(row, space))}, list {F(RectIn(scrollRT, space))}, shown part {F(cl)}");
        SelfTest.Screenshot(LayoutName, label);
        yield return null;
        yield return null;
    }

    // ---------- csearch.search (T02, T03, T04) ----------

    private static IEnumerator RunSearch()
    {
        const string T = SearchName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            yield return rig.UnlockAllRecipes(c);
            rig.ForgetSorts();
            rig.ClearCraftFilter();
            var gui = rig.Gui;

            // T02: forge, "silver" letter by letter.
            var forge = new Box<CraftingStation>();
            yield return Open(rig, "forge", forge);
            if (!c.Check(forge.Value != null && SearchUi.Field != null, "setup: forge or search field missing"))
            {
                yield break;
            }
            var all = Rows(gui);
            c.Check(all.Count >= 10 && CraftSearch.Option == RecipeCategory.Default && SearchUi.Field.text == "",
                $"setup: forge lists {all.Count} recipes, sort '{RecipeCategory.Id(CraftSearch.Option)}', text '{SearchUi.Field.text}'");
            var previous = all;
            const string word = "silver";
            for (var n = 1; n <= word.Length; n++)
            {
                var typed = word.Substring(0, n);
                yield return Type(typed);
                var now = Rows(gui);
                c.Check(!CraftSearch.Pending && IsSubsequence(now, previous),
                    $"typing '{typed}': the list did not shrink in place ({previous.Count} -> {now.Count} rows, or rows moved)");
                previous = now;
            }
            c.Check(CraftSearch.Term == "silver" && previous.Count < all.Count && previous.Count > 0,
                $"'silver' leaves {previous.Count} of {all.Count} rows (term '{CraftSearch.Term}')");
            CheckFilter(c, rig, all, "silver", "forge 'silver'");
            c.Check(IndexOf(gui, "SwordSilver") >= 0 && IndexOf(gui, "MaceSilver") >= 0, "forge 'silver': Silver Sword or Frostner (MaceSilver) not listed");
            CheckPlacement(c, gui, "forge 'silver'");
            c.Note($"forge: {all.Count} recipes, 'silver' keeps {previous.Count}: {Names(previous, 14)}");

            // T04: case and spaces.
            yield return Type("SiLver SWord");
            var mixed = Rows(gui);
            CheckFilter(c, rig, all, "silversword", "forge 'SiLver SWord'");
            c.Check(IndexOf(gui, "SwordSilver") >= 0, "'SiLver SWord' does not list the Silver Sword");
            yield return Type("silversword");
            c.Check(SameOrder(mixed, Rows(gui)), $"'SiLver SWord' ({mixed.Count} rows) and 'silversword' ({gui.m_availableRecipes.Count} rows) give different lists");
            c.Check(CraftSearch.Term == "silversword" && RecipeTerms.Normalise("SiLver\tSWord ") == "silversword", "term not lower case without spaces");
            yield return Type("");
            c.Check(SameOrder(all, Rows(gui)), "emptying the field did not bring the full forge list back in its order");
            yield return Close(rig);
            rig.DestroySpawned();

            // T03: workbench, ingredient "wood". A wood needed only for upgrades is not shown in the Craft tab.
            var bench = new Box<CraftingStation>();
            yield return Open(rig, "piece_workbench", bench);
            if (!c.Check(bench.Value != null, "setup: workbench missing"))
            {
                yield break;
            }
            all = Rows(gui);
            c.Check(all.Count >= 10 && gui.InCraftTab(), $"setup: workbench lists {all.Count} recipes, craft tab {gui.InCraftTab()}");
            yield return Type("wood");
            var kept = CheckFilter(c, rig, all, "wood", "workbench 'wood'");
            c.Check(IndexOf(gui, "ArrowWood") >= 0, "workbench 'wood': Wood Arrow (name match) not listed");
            var byIngredient = 0;
            var upgradeOnly = new List<InventoryGui.RecipeDataPair>();
            var saved = gui.m_selectedRecipe;
            foreach (var row in all)
            {
                if (!Named(row))
                {
                    continue;
                }
                var reason = MatchReason(gui, rig.P, row, "wood");
                if (reason != null && reason.StartsWith("ingredient", StringComparison.Ordinal))
                {
                    byIngredient++;
                }
                if (reason == null && row.Recipe.m_resources != null && row.Recipe.m_resources.Any(q => q != null && q.m_resItem != null
                        && q.GetAmount(1) <= 0 && q.GetAmount(2) > 0 && Norm(Loc(q.m_resItem.m_itemData.m_shared.m_name)).Contains("wood")))
                {
                    upgradeOnly.Add(row);
                }
            }
            gui.m_selectedRecipe = saved;
            c.Check(byIngredient >= 3, $"workbench 'wood': only {byIngredient} rows kept for a wood ingredient");
            var now2 = Rows(gui);
            c.Check(!upgradeOnly.Any(u => Contains(now2, u)), $"workbench 'wood': kept although wood is needed only to upgrade: {Names(upgradeOnly, 5)}");
            c.Note($"workbench: {all.Count} recipes, 'wood' keeps {kept.Count} ({byIngredient} by ingredient); wood only for upgrades: "
                   + (upgradeOnly.Count > 0 ? Names(upgradeOnly, 5) + " (not kept)" : "no such recipe here"));
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.select (T05, T06, T11) ----------

    private static IEnumerator RunSelect()
    {
        const string T = SelectName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            yield return rig.UnlockAllRecipes(c);
            rig.ForgetSorts();
            rig.ClearCraftFilter();
            var gui = rig.Gui;
            var forge = new Box<CraftingStation>();
            yield return Open(rig, "forge", forge);
            if (!c.Check(forge.Value != null && SearchUi.Field != null, "setup: forge or search field missing"))
            {
                yield break;
            }
            var field = SearchUi.Field;
            var all = Rows(gui);
            var errors = LogWatch.Instance.ErrorCount;

            // T05: no match, and back.
            yield return Type("zzzz");
            c.Check(gui.m_availableRecipes.Count == 0, $"'zzzz' still lists {gui.m_availableRecipes.Count} row(s): {Names(Rows(gui), 5)}");
            c.Check(gui.m_selectedRecipe.Recipe == null, "'zzzz': a recipe is still selected");
            yield return Frames(2);
            c.Check(!gui.m_craftButton.interactable, "'zzzz': the craft button is still on");
            c.Check(!gui.m_recipeName.enabled && !gui.m_recipeIcon.enabled && !gui.m_recipeDecription.enabled, "'zzzz': recipe details still shown");
            var slots = 0;
            foreach (var slot in gui.m_recipeRequirementList)
            {
                var icon = slot != null ? slot.transform.Find("res_icon") : null;
                if (icon != null && icon.gameObject.activeSelf)
                {
                    slots++;
                }
            }
            c.Check(slots == 0, $"'zzzz': {slots} requirement slot(s) still shown");
            c.Check(gui.m_recipeListRoot.rect.height <= gui.m_recipeListBaseSize + 0.5f, "'zzzz': empty list still has rows' height");
            yield return Type("");
            yield return null;
            var back = Rows(gui);
            c.Check(SameOrder(all, back), $"text deleted: {back.Count} rows, the full list has {all.Count}");
            c.Check(back.Count > 0 && Same(gui.m_selectedRecipe, back[0]) && Marked(gui, 0) && MarkedCount(gui) == 1, "text deleted: the first row is not the selected one");
            c.Check(LogWatch.Instance.ErrorCount == errors, "an error of this mod was logged: " + string.Join(" | ", LogWatch.Instance.Errors().Skip(errors).Take(2).ToArray()));

            // T06: selection kept while it matches, first row otherwise.
            var sword = IndexOf(gui, "SwordSilver");
            if (c.Check(sword >= 0, "setup: no Silver Sword row at the forge"))
            {
                var swordRow = gui.m_availableRecipes[sword];
                gui.SetRecipe(sword, false);
                var scroll = ScrollOf(gui);
                var scrolled = scroll != null && ScrollToEnd(gui, scroll);
                yield return null;
                yield return Type("silver");
                yield return null;
                var index = IndexOf(gui, "SwordSilver");
                c.Check(Same(gui.m_selectedRecipe, swordRow), "'silver': the Silver Sword is no longer the selected recipe");
                c.Check(index >= 0 && Marked(gui, index) && MarkedCount(gui) == 1, "'silver': the highlight is not on the Silver Sword row");
                c.Check(scroll != null && AtTop(gui, scroll), "'silver': the list is not at the top");
                if (!scrolled)
                {
                    c.Note("the forge list fits in the view: 'list at the top' was not put to the test here");
                }
                yield return Type("bronze");
                yield return null;
                var rows = Rows(gui);
                c.Check(rows.Count > 0 && IndexOf(rows, "SwordSilver") < 0, $"setup: 'bronze' leaves {rows.Count} rows (Silver Sword among them: {IndexOf(rows, "SwordSilver") >= 0})");
                c.Check(rows.Count > 0 && Same(gui.m_selectedRecipe, rows[0]) && Marked(gui, 0) && MarkedCount(gui) == 1,
                    "'bronze': the first remaining row is not selected");
            }

            // T11: right-click clears, at once.
            yield return Type("silver");
            c.Check(gui.m_availableRecipes.Count < all.Count && field.text == "silver", "setup: 'silver' not applied before the right-click");
            SendClick(field.gameObject, PointerEventData.InputButton.Middle);
            c.Check(field.text == "silver", "a middle click cleared the field");
            SendClick(field.gameObject, PointerEventData.InputButton.Right);
            c.Check(field.text == "" && CraftSearch.Term == "", $"right-click: text still '{field.text}'");
            c.Check(SameOrder(all, Rows(gui)), $"right-click: {gui.m_availableRecipes.Count} rows, the full list has {all.Count}");
            yield return Frames(2);
            c.Check(SameOrder(all, Rows(gui)) && !CraftSearch.Pending, "right-click: full list not kept on the next frames");
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.sort (T13, T14, T16, T17) ----------

    private static IEnumerator RunSort()
    {
        const string T = SortName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            yield return rig.UnlockAllRecipes(c);
            rig.ForgetSorts();
            rig.ClearCraftFilter();
            var gui = rig.Gui;
            var forge = new Box<CraftingStation>();
            yield return Open(rig, "forge", forge);
            if (!c.Check(forge.Value != null && SearchUi.Field != null && SortButton() != null, "setup: forge, search field or Sort button missing"))
            {
                yield break;
            }
            var all = Rows(gui);
            var scroll = ScrollOf(gui);
            var ok = new Box<bool>();
            Func<InventoryGui.RecipeDataPair, bool> isWeapon = r => Named(r) && ItemKinds.Classify(Shared(r)) == ItemKind.Weapon;
            var weapons = all.Count(isWeapon);
            c.Check(weapons > 0 && weapons < all.Count && CraftSearch.Option == RecipeCategory.Default, $"setup: {weapons} weapon rows of {all.Count}");

            // T13: Weapons first, both groups in their vanilla order; label; list at the top.
            var scrolled = scroll != null && ScrollToEnd(gui, scroll);
            yield return null;
            yield return Choose(rig, RecipeCategory.Weapons, ok);
            c.Check(ok.Value, "the menu has no Weapons entry to click");
            var sorted = Rows(gui);
            c.Check(SameOrder(Partition(all, isWeapon), sorted), $"Weapons: not 'weapon rows first, each group in its Default order': {Names(sorted, 8)}");
            c.Check(CraftSearch.Option == RecipeCategory.Weapons && SortLabel() == "Sort: Weapons", $"Weapons: button reads '{SortLabel()}'");
            CheckPlacement(c, gui, "Weapons");
            c.Check(scroll != null && AtTop(gui, scroll), "Weapons: the list is not scrolled to the top");
            if (!scrolled)
            {
                c.Note("the forge list fits in the view: 'scrolled to the top' was not put to the test here");
            }
            // Known items land on the right side (prefab names; absent ones are skipped).
            var misplaced = new List<string>();
            foreach (var name in new[] { "SwordSilver", "MaceSilver", "KnifeSilver", "AtgeirBronze", "SwordBronze", "AxeBronze", "SpearBronze" })
            {
                var i = IndexOf(sorted, name);
                if (i >= weapons)
                {
                    misplaced.Add(name + " not among the weapons");
                }
            }
            foreach (var name in new[] { "ShieldSilver", "ShieldBronzeBuckler", "HelmetBronze", "ArmorBronzeChest", "BronzeNails", "PickaxeBronze", "PickaxeIron" })
            {
                var i = IndexOf(sorted, name);
                if (i >= 0 && i < weapons)
                {
                    misplaced.Add(name + " among the weapons");
                }
            }
            c.Check(misplaced.Count == 0, "Weapons: " + string.Join(", ", misplaced.ToArray()));
            c.Check(IndexOf(sorted, "SwordSilver") >= 0 && IndexOf(sorted, "SwordSilver") < weapons, "Weapons: the Silver Sword is not in the weapon block");

            // T16: search follows the sort; back to Default = same rows in vanilla order.
            yield return Type("silver");
            var silver = Matching(rig, all, "silver");
            CheckFilter(c, rig, all, "silver", "Weapons + 'silver'");
            c.Check(SameOrder(Partition(silver, isWeapon), Rows(gui)), $"Weapons + 'silver': not 'silver weapons first, then the other silver rows': {Names(Rows(gui), 10)}");
            var silverWeapons = silver.Count(isWeapon);
            c.Check(silverWeapons >= 2 && silverWeapons < silver.Count && IndexOf(gui, "SwordSilver") < silverWeapons && IndexOf(gui, "MaceSilver") < silverWeapons
                    && IndexOf(gui, "SwordSilver") >= 0 && IndexOf(gui, "MaceSilver") >= 0,
                $"Weapons + 'silver': {silverWeapons} silver weapons of {silver.Count} rows; Silver Sword at {IndexOf(gui, "SwordSilver")}, Frostner at {IndexOf(gui, "MaceSilver")}");
            yield return Choose(rig, RecipeCategory.Default, ok);
            c.Check(ok.Value && SameOrder(silver, Rows(gui)) && SortLabel() == "Sort: Default" && SearchUi.Field.text == "silver",
                $"Default + 'silver': not the same rows in vanilla order ({Names(Rows(gui), 8)}), button '{SortLabel()}'");
            yield return Type("");
            c.Check(SameOrder(all, Rows(gui)), "Default, no search: not the vanilla list");

            // T14: first weapon type listed in the menu.
            SendClick(SortButton().gameObject, PointerEventData.InputButton.Left);
            var menu = MenuOf(gui);
            var family = -1;
            if (menu != null)
            {
                for (var i = 0; i < menu.childCount && family < 0; i++)
                {
                    var child = menu.GetChild(i);
                    if (child.gameObject.activeSelf && child.name.StartsWith(OptionPrefix + "w_", StringComparison.Ordinal))
                    {
                        family = RecipeCategory.FromId(child.name.Substring(OptionPrefix.Length));
                    }
                }
            }
            if (c.Check(family > 0 && FamilySkill(family, out var skill), "the menu lists no weapon type at the forge"))
            {
                FamilySkill(family, out skill);
                Func<InventoryGui.RecipeDataPair, bool> inFamily = r => isWeapon(r) && Shared(r).m_skillType == skill;
                yield return Choose(rig, family, ok);
                var byFamily = Rows(gui);
                var n = all.Count(inFamily);
                c.Check(ok.Value && n > 0 && SameOrder(Partition(all, inFamily), byFamily),
                    $"{RecipeCategory.Id(family)}: not '{skill} first ({n}), every other row (other weapons too) after in Default order': {Names(byFamily, 8)}");
                var localized = Loc("$skill_" + skill.ToString().ToLowerInvariant());
                var label = string.IsNullOrEmpty(localized) || localized.StartsWith("[", StringComparison.Ordinal) ? null : localized;
                c.Check(label == null ? SortLabel() != null && SortLabel().StartsWith("Sort: ", StringComparison.Ordinal) : SortLabel() == "Sort: " + label,
                    $"{RecipeCategory.Id(family)}: button reads '{SortLabel()}', the game's name of the skill is '{localized}'");
                c.Check(byFamily.Skip(n).Any(isWeapon) || weapons == n, $"{RecipeCategory.Id(family)}: other weapons were moved up too");
                c.Note($"first weapon type in the menu: {RecipeCategory.Id(family)} ({n} rows), label '{SortLabel()}'");
            }
            else
            {
                SearchUi.CloseMenu();
            }

            // T17: right-click on Sort = Default. With the menu open on Default: only closes it, no rebuild.
            yield return Choose(rig, RecipeCategory.Weapons, ok);
            c.Check(CraftSearch.Option == RecipeCategory.Weapons, "setup: Weapons not chosen before the reset");
            SendClick(SortButton().gameObject, PointerEventData.InputButton.Right);
            yield return Frames(2);
            c.Check(CraftSearch.Option == RecipeCategory.Default && SortLabel() == "Sort: Default" && SameOrder(all, Rows(gui)) && !SearchUi.MenuOpen,
                $"right-click on Sort: sort '{RecipeCategory.Id(CraftSearch.Option)}', button '{SortLabel()}', vanilla order {SameOrder(all, Rows(gui))}");
            SendClick(SortButton().gameObject, PointerEventData.InputButton.Left);
            c.Check(SearchUi.MenuOpen && MenuOf(gui) != null, "setup: left click did not open the menu");
            var objects = gui.m_availableRecipes.Select(r => r.InterfaceElement).ToList();
            SendClick(SortButton().gameObject, PointerEventData.InputButton.Right);
            yield return Frames(2);
            var same = gui.m_availableRecipes.Count == objects.Count;
            for (var i = 0; same && i < objects.Count; i++)
            {
                same = ReferenceEquals(objects[i], gui.m_availableRecipes[i].InterfaceElement) && objects[i] != null;
            }
            c.Check(!SearchUi.MenuOpen && MenuOf(gui) == null, "right-click on Sort with the menu open on Default: the menu did not close");
            c.Check(same && CraftSearch.Option == RecipeCategory.Default && SortLabel() == "Sort: Default", "right-click on Sort, already Default: the list was rebuilt or the sort changed");
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.byname (T15) ----------

    private static IEnumerator RunByName()
    {
        const string T = ByNameName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            yield return rig.UnlockAllRecipes(c);
            rig.ForgetSorts();
            rig.ClearCraftFilter();
            var gui = rig.Gui;
            var forge = new Box<CraftingStation>();
            yield return Open(rig, "forge", forge);
            if (!c.Check(forge.Value != null && SearchUi.Field != null, "setup: forge or search field missing"))
            {
                yield break;
            }

            // Materials for a few recipes spread over the list: some rows craftable, most greyed.
            var all = Rows(gui);
            var level = forge.Value.GetLevel();
            var made = new List<string>();
            foreach (var start in new[] { all.Count / 4, all.Count / 2, all.Count * 3 / 4 })
            {
                for (var i = start; i < all.Count; i++)
                {
                    var row = all[i];
                    if (!Named(row) || row.CanCraft || made.Contains(row.Recipe.m_item.name) || row.Recipe.m_requireOnlyOneIngredient
                        || row.Recipe.m_minStationLevel > level || row.Recipe.m_resources == null || row.Recipe.m_resources.Length == 0
                        || row.Recipe.m_resources.Length > 4 || Shared(row).m_dlc.Length > 0
                        || row.Recipe.m_resources.Any(q => q == null || q.m_resItem == null || q.m_upgraderResource || q.GetAmount(1) > 40))
                    {
                        continue;
                    }
                    foreach (var req in row.Recipe.m_resources)
                    {
                        if (req.GetAmount(1) > 0)
                        {
                            rig.Give(req.m_resItem.name, req.GetAmount(1));
                        }
                    }
                    made.Add(row.Recipe.m_item.name);
                    break;
                }
            }
            gui.UpdateCraftingPanel();
            yield return null;
            all = Rows(gui);
            var craftable = all.Count(r => r.CanCraft);
            c.Check(craftable > 0 && craftable < all.Count, $"setup: {craftable} craftable rows of {all.Count} (materials given for: {string.Join(", ", made.ToArray())})");
            var firstGreyed = all.FindIndex(r => !r.CanCraft);
            c.Check(firstGreyed == craftable, "setup: on Default the craftable rows are not all first (vanilla order expected)");

            var ok = new Box<bool>();
            yield return Choose(rig, RecipeCategory.Name, ok);
            var rows = Rows(gui);
            c.Check(ok.Value && CraftSearch.Option == RecipeCategory.Name && SortLabel() == "Sort: Name (A-Z)", $"Name: button reads '{SortLabel()}'");
            c.Check(rows.Count == all.Count && rows.All(r => Contains(all, r)), $"Name: {rows.Count} rows, Default had {all.Count}");
            var comparer = StringComparer.CurrentCultureIgnoreCase;
            var outOfOrder = new List<string>();
            var textWrong = new List<string>();
            for (var i = 0; i < rows.Count; i++)
            {
                if (!Named(rows[i]))
                {
                    continue;
                }
                var name = Loc(Shared(rows[i]).m_name);
                var text = rows[i].InterfaceElement != null && rows[i].InterfaceElement.transform.Find("name") != null
                    ? rows[i].InterfaceElement.transform.Find("name").GetComponent<TMP_Text>()
                    : null;
                if (text == null || !text.text.StartsWith(name, StringComparison.Ordinal))
                {
                    textWrong.Add(rows[i].Recipe.m_item.name);
                }
                if (i > 0 && Named(rows[i - 1]) && comparer.Compare(Loc(Shared(rows[i - 1]).m_name), name) > 0)
                {
                    outOfOrder.Add($"'{Loc(Shared(rows[i - 1]).m_name)}' before '{name}'");
                }
            }
            c.Check(outOfOrder.Count == 0, $"Name: {outOfOrder.Count} pair(s) not in A-Z order: {string.Join(", ", outOfOrder.Take(4).ToArray())}");
            c.Check(textWrong.Count == 0, $"Name: row text is not the name the order uses for: {string.Join(", ", textWrong.Take(4).ToArray())}");
            var greyedAbove = false;
            var seenGreyed = false;
            foreach (var row in rows)
            {
                if (!row.CanCraft)
                {
                    seenGreyed = true;
                }
                else if (seenGreyed)
                {
                    greyedAbove = true;
                }
            }
            c.Check(greyedAbove, $"Name: no greyed row stands above a craftable one (craftable rows at {string.Join(", ", rows.Select((r, i) => r.CanCraft ? i.ToString() : null).Where(s => s != null).ToArray())} of {rows.Count})");
            CheckPlacement(c, gui, "Name");
            c.Note($"Name (A-Z): {rows.Count} rows, craftable ones at {string.Join(", ", rows.Select((r, i) => r.CanCraft ? i + " " + RowName(r) : null).Where(s => s != null).ToArray())}");
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.menu (T12) ----------

    private static IEnumerator RunMenu()
    {
        const string T = MenuName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            yield return rig.UnlockAllRecipes(c);
            rig.ForgetSorts();
            rig.ClearCraftFilter();
            var gui = rig.Gui;
            var forge = new Box<CraftingStation>();
            yield return Open(rig, "forge", forge, 0.8f);
            if (!c.Check(forge.Value != null && SortButton() != null, "setup: forge or Sort button missing"))
            {
                yield break;
            }
            var all = Rows(gui);
            var button = SortButton().gameObject;

            // Open with a click on Sort: content.
            c.Check(!SearchUi.MenuOpen && MenuOf(gui) == null, "setup: a menu is open before the click");
            SendClick(button, PointerEventData.InputButton.Left);
            CheckMenu(c, gui, all, RecipeCategory.Default, "forge");
            yield return Frames(3);
            c.Check(SearchUi.MenuOpen && MenuOf(gui) != null, "the menu closed by itself");
            c.Check(Chat.instance == null || !Chat.instance.HasFocus(), "menu open, no key pressed: the game holds the keys back (the player could not walk)");
            c.Check(EventSystem.current == null || EventSystem.current.currentSelectedGameObject != button, "the Sort button stays selected after the click");
            SelfTest.Screenshot(T, "menu");
            yield return null;
            yield return null;

            // Click on Sort again closes it.
            SendClick(button, PointerEventData.InputButton.Left);
            c.Check(!SearchUi.MenuOpen && MenuOf(gui) == null, "second click on Sort: the menu is still open");

            // The active one is highlighted.
            var ok = new Box<bool>();
            yield return Choose(rig, RecipeCategory.Weapons, ok);
            c.Check(ok.Value && !SearchUi.MenuOpen && MenuOf(gui) == null, "choosing Weapons did not close the menu");
            SendClick(button, PointerEventData.InputButton.Left);
            CheckMenu(c, gui, all, RecipeCategory.Weapons, "forge on Weapons");

            // Tab switch closes it.
            gui.OnTabUpgradePressed();
            c.Check(!SearchUi.MenuOpen && MenuOf(gui) == null, "Upgrade tab: the menu is still open");
            gui.OnTabCraftPressed();
            yield return null;

            // W still walks with the menu open, and the menu stays.
            SendClick(button, PointerEventData.InputButton.Left);
            yield return Frames(2);
            if (ZInput.IsGamepadActive())
            {
                c.Note("a gamepad is the active input: the game does not walk with the inventory open, 'W walks' not checked");
            }
            else if (Button("Forward") != null)
            {
                var from = rig.P.transform.position;
                yield return Hold(rig, "Forward", 0.5f);
                var moved = Flat(from, rig.P.transform.position);
                c.Check(moved > 0.2f, $"menu open, Forward held 0.5 s: the player moved {F(moved)} m (expected to walk)");
                c.Check(SearchUi.MenuOpen && Shown(gui), "walking closed the menu or the inventory");
                rig.GoHome();
                yield return Frames(2);
            }

            // B (same code as Esc) closes the menu, not the inventory.
            if (!SearchUi.MenuOpen)
            {
                SendClick(button, PointerEventData.InputButton.Left);
                yield return Frames(2);
            }
            yield return Tap(rig, "JoyButtonB");
            c.Check(!SearchUi.MenuOpen && MenuOf(gui) == null, "B with the menu open: the menu is still open");
            c.Check(Shown(gui), "B with the menu open: the inventory closed too");

            // E closes the inventory, menu gone, also after reopening.
            if (!Shown(gui))
            {
                yield return Reopen(rig, forge.Value);
            }
            SendClick(SortButton().gameObject, PointerEventData.InputButton.Left);
            yield return Frames(2);
            c.Check(SearchUi.MenuOpen, "setup: menu not open before E");
            yield return Tap(rig, "Use");
            c.Check(!Shown(gui), "E with the menu open: the inventory stayed open");
            c.Check(!SearchUi.MenuOpen && MenuOf(gui) == null, "E with the menu open: the menu is still there");
            yield return Frames(3);
            yield return Reopen(rig, forge.Value);
            c.Check(!SearchUi.MenuOpen && MenuOf(gui) == null, "inventory reopened: the menu is back");
            c.Check(SortLabel() == "Sort: Weapons", $"inventory reopened: button reads '{SortLabel()}'");

            // Tab does the same.
            SendClick(SortButton().gameObject, PointerEventData.InputButton.Left);
            yield return Frames(2);
            yield return Tap(rig, "Inventory");
            c.Check(!Shown(gui) && !SearchUi.MenuOpen && MenuOf(gui) == null, "Tab with the menu open: inventory or menu still open");
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.typing (T07) ----------

    private static IEnumerator RunTyping()
    {
        const string T = TypingName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            var gui = rig.Gui;
            var p = rig.P;
            var bench = new Box<CraftingStation>();
            yield return Open(rig, "piece_workbench", bench);
            if (!c.Check(bench.Value != null && SearchUi.Field != null, "setup: workbench or search field missing"))
            {
                yield break;
            }
            var field = SearchUi.Field;
            var gamepad = ZInput.IsGamepadActive();
            var autoPickup = Player.m_enableAutoPickup;
            var walk = p.GetWalk();

            yield return Focus(c, "workbench");
            c.Check(Chat.instance != null && Chat.instance.HasFocus(), "typing: the game does not treat the field as a text input (Chat.HasFocus is false)");
            c.Note($"while typing: UI navigation events {(EventSystem.current != null && EventSystem.current.sendNavigationEvents ? "on" : "off")}, gamepad active {gamepad}");

            // Game buttons of the typed keys: E, Tab, Space, Q, R, X, C, V, 1-8, Y on a pad. None may act.
            var from = p.transform.position;
            var failed = new List<string>();
            var missing = new List<string>();
            var keys = new List<string> { "Use", "Inventory", "Jump", "AutoRun", "Hide", "Sit", "ToggleWalk", "AutoPickup", "JoyButtonY", "TabLeft", "TabRight", "GP" };
            for (var i = 1; i <= 8; i++)
            {
                keys.Add("Hotbar" + i);
            }
            foreach (var key in keys)
            {
                if (Button(key) == null)
                {
                    missing.Add(key);
                    continue;
                }
                yield return Tap(rig, key);
                if (!Shown(gui) || !field.isFocused || p.GetCurrentCraftingStation() != bench.Value)
                {
                    failed.Add(key);
                    if (!Shown(gui))
                    {
                        yield return Reopen(rig, bench.Value);
                    }
                    yield return Focus(c, "workbench again");
                }
            }
            c.Check(failed.Count == 0, $"typing: these game buttons closed the inventory or took the cursor: {string.Join(", ", failed.ToArray())}");
            if (missing.Count > 0)
            {
                c.Note("game has no button named: " + string.Join(", ", missing.ToArray()));
            }
            // WASD held, and Space once more with the height watched.
            var maxUp = 0f;
            foreach (var key in new[] { "Forward", "Left", "Backward", "Right" })
            {
                yield return Hold(rig, key, 0.3f);
            }
            var jump = Button("Jump");
            if (jump != null)
            {
                rig.Pressed(jump);
                jump.Press();
                var until = Time.time + 0.4f;
                while (Time.time < until)
                {
                    maxUp = Mathf.Max(maxUp, p.transform.position.y - from.y);
                    yield return null;
                }
                jump.Release();
            }
            var moved = Flat(from, p.transform.position);
            c.Check(moved < 0.1f, $"typing: W A S D moved the player {F(moved)} m");
            c.Check(maxUp < 0.1f, $"typing: Space made the player jump ({F(maxUp)} m up)");
            c.Check(Player.m_enableAutoPickup == autoPickup, "typing: V toggled auto-pickup");
            c.Check(p.GetWalk() == walk && !p.m_autoRun && !p.IsSitting(), $"typing: walk toggle {p.GetWalk() != walk}, auto-run {p.m_autoRun}, sitting {p.IsSitting()}");
            c.Check(p.GetRightItem() == null && p.GetLeftItem() == null, "typing: a hotbar key put an item in the hands");
            c.Check(Shown(gui) && field.isFocused, "typing: after all keys the inventory is closed or the cursor is gone");

            // A panel button with a controller hotkey does not fire either (craft button, if it has one).
            var pad = gui.m_craftButton != null ? gui.m_craftButton.GetComponent<UIGamePad>() : null;
            if (pad != null && !string.IsNullOrEmpty(pad.m_zinputKey) && Button(pad.m_zinputKey) != null && pad.m_zinputKey != "JoyButtonB")
            {
                var hot = Button(pad.m_zinputKey);
                rig.Pressed(hot);
                hot.Press();
                var seenDown = false;
                var fired = false;
                for (var i = 0; i < 4; i++)
                {
                    yield return null;
                    if (ZInput.GetButtonDown(pad.m_zinputKey))
                    {
                        seenDown = true;
                        fired |= pad.ButtonPressed();
                    }
                }
                hot.Release();
                yield return Frames(2);
                c.Check(seenDown && !fired && gui.m_craftTimer < 0f, $"typing: craft button hotkey '{pad.m_zinputKey}' fired (seen down {seenDown}, fired {fired}, crafting {gui.m_craftTimer >= 0f})");
            }
            else
            {
                c.Note("craft button has no controller hotkey button here: panel hotkeys not put to the test");
            }

            // Control: same presses act once the field has let go, so the checks above mean something.
            yield return Unfocus();
            c.Check(Chat.instance == null || !Chat.instance.HasFocus(), "cursor out of the field: game keys still held back");
            if (!gamepad && Button("Forward") != null)
            {
                from = p.transform.position;
                yield return Hold(rig, "Forward", 0.4f);
                c.Check(Flat(from, p.transform.position) > 0.2f, $"control: Forward held with the cursor out of the field moved the player only {F(Flat(from, p.transform.position))} m");
                rig.GoHome();
                yield return Frames(2);
            }
            yield return Tap(rig, "Use");
            c.Check(!Shown(gui), "control: E with the cursor out of the field does not close the inventory (button presses of this test do not reach the game)");
            yield return Close(rig);
            rig.DestroySpawned();

            // Chest open (hand crafting list shown): E typed again and again, and held.
            var chestPrefab = ZNetScene.instance.GetPrefab("piece_chest_wood");
            var container = chestPrefab != null ? chestPrefab.GetComponent<Container>() : null;
            if (!c.Check(container != null, "setup: no piece_chest_wood prefab with a Container"))
            {
                yield break;
            }
            var pos = rig.Home + rig.HomeForward * 2f;
            pos.y = ZoneSystem.instance.GetGroundHeight(pos);
            var chestGo = Object.Instantiate(chestPrefab, pos, Quaternion.LookRotation(-rig.HomeForward));
            rig.Spawned(chestGo);
            var wear = chestGo.GetComponent<WearNTear>();
            if (wear != null)
            {
                wear.enabled = false;
            }
            yield return new WaitForSeconds(0.3f);
            var chest = chestGo.GetComponent<Container>();
            chest.GetInventory().AddItem("Wood", 1, 1, 0, 0L, "", false);
            rig.Give("Wood", 5);
            var woodName = "$item_wood";
            var woodPrefab = ObjectDB.instance.GetItemPrefab("Wood");
            if (woodPrefab != null)
            {
                woodName = woodPrefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
            }
            var mine = rig.Inv.CountItems(woodName);
            p.SetCraftingStation(null);
            gui.Show(chest);
            yield return new WaitForSecondsRealtime(0.6f);
            c.Check(Shown(gui) && gui.IsContainerOpen() && chest.GetInventory().CountItems(woodName) == 1 && mine >= 5,
                $"setup: chest open {gui.IsContainerOpen()}, wood in chest {chest.GetInventory().CountItems(woodName)}, mine {mine}");
            yield return Focus(c, "chest");
            for (var i = 0; i < 4; i++)
            {
                yield return Tap(rig, "Use");
            }
            yield return Hold(rig, "Use", 1.3f);
            c.Check(Shown(gui) && gui.IsContainerOpen() && field.isFocused, "chest open, E typed and held: the chest or the inventory closed");
            c.Check(chest.GetInventory().CountItems(woodName) == 1 && rig.Inv.CountItems(woodName) == mine,
                $"chest open, E typed and held: items were stacked into the chest (chest {chest.GetInventory().CountItems(woodName)}, mine {rig.Inv.CountItems(woodName)} of {mine})");
            yield return Unfocus();
            yield return Tap(rig, "Use");
            c.Check(!Shown(gui), "control: E with the cursor out of the field does not close the chest");
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.binds (T08) ----------

    private const string ProbeCommand = "mcsearchselftest";

    private static IEnumerator RunBinds()
    {
        const string T = BindsName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            var hits = new Box<int>();
            rig.Undo("probe command", () => Terminal.commands.Remove(ProbeCommand));
            new Terminal.ConsoleCommand(ProbeCommand, "self test probe", (Terminal.ConsoleEvent)(args => hits.Value++));
            var bench = new Box<CraftingStation>();
            yield return Open(rig, "piece_workbench", bench);
            if (!c.Check(bench.Value != null && SearchUi.Field != null && Chat.instance != null, "setup: workbench, search field or chat missing"))
            {
                yield break;
            }

            // Cursor not in the field: the call the bind loop makes runs the command.
            Chat.instance.TryRunCommand(ProbeCommand, silentFail: true, skipAllowedCheck: true);
            c.Check(hits.Value == 1, $"control: a bound command did not run with the cursor out of the field ({hits.Value})");

            yield return Focus(c, "workbench");
            // Exactly what Chat.Update does for a bound key (only caller with skipAllowedCheck).
            Chat.instance.TryRunCommand(ProbeCommand, silentFail: true, skipAllowedCheck: true);
            yield return null;
            Chat.instance.TryRunCommand(ProbeCommand, silentFail: true, skipAllowedCheck: true);
            c.Check(hits.Value == 1, $"typing: a bound command ran ({hits.Value - 1} time(s))");
            // A typed command is not a bind: still runs.
            Chat.instance.TryRunCommand(ProbeCommand);
            c.Check(hits.Value == 2, "typing: a command typed in the chat or console was blocked too");

            yield return Unfocus();
            Chat.instance.TryRunCommand(ProbeCommand, silentFail: true, skipAllowedCheck: true);
            c.Check(hits.Value == 3, "cursor out of the field again: bound commands still blocked");
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.focus (T09) ----------

    private static IEnumerator RunFocus()
    {
        const string T = FocusName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            yield return rig.UnlockAllRecipes(c);
            rig.ForgetSorts();
            rig.ClearCraftFilter();
            var gui = rig.Gui;
            var forge = new Box<CraftingStation>();
            yield return Open(rig, "forge", forge);
            if (!c.Check(forge.Value != null && SearchUi.Field != null && Chat.instance != null, "setup: forge, search field or chat missing"))
            {
                yield break;
            }
            var field = SearchUi.Field;
            var all = Rows(gui);
            foreach (var key in new[] { "escape", "return" })
            {
                var label = key == "escape" ? "Esc" : "Enter";
                if (!Shown(gui))
                {
                    yield return Reopen(rig, forge.Value);
                }
                yield return Type("bronze");
                var filtered = Rows(gui);
                c.Check(filtered.Count > 0 && filtered.Count < all.Count, $"setup: 'bronze' leaves {filtered.Count} of {all.Count} rows");
                yield return Focus(c, label);

                // What the text field itself does on that key: handle it, (Enter: submit), stop editing.
                field.ProcessEvent(Event.KeyboardEvent(key));
                if (key == "return")
                {
                    field.onSubmit.Invoke(field.text);
                }
                field.DeactivateInputField();
                c.Check(Chat.instance.HasFocus(), $"{label}: on the frame the cursor left, the game keys are no longer held back (the same {label} press would act on the inventory)");
                yield return null;
                c.Check(Chat.instance.HasFocus(), $"{label}: one frame later the game keys are not held back any more");
                yield return Until(() => !Chat.instance.HasFocus(), 0.5f);
                yield return Frames(2);
                c.Check(!Chat.instance.HasFocus(), $"{label}: game keys still held back 0.5 s after the cursor left");
                c.Check(!field.isFocused && field.text == "bronze" && CraftSearch.Term == "bronze", $"{label}: cursor still in the field or text lost ('{field.text}')");
                c.Check(SameOrder(filtered, Rows(gui)) && !CraftSearch.Pending, $"{label}: the list is no longer the 'bronze' list");
                c.Check(Shown(gui) && InventoryGui.IsVisible(), $"{label}: the inventory closed");
                var es = EventSystem.current;
                c.Check(es != null && es.currentSelectedGameObject != field.gameObject, $"{label}: the field is still the selected UI object");
                c.Check(es != null && es.sendNavigationEvents, $"{label}: UI navigation events stay off (keyboard navigation in menus would not work)");

                // The next close key closes the inventory again.
                yield return Tap(rig, key == "escape" ? "JoyButtonB" : "Inventory");
                c.Check(!Shown(gui), $"after {label}: the next close key does not close the inventory");
                yield return Frames(3);
            }
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.focuskey (T10) ----------

    private static IEnumerator RunFocusKey()
    {
        const string T = FocusKeyName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            var gui = rig.Gui;
            rig.FocusKey(new KeyboardShortcut(KeyCode.F));
            c.Check(CraftSearch.DebugFocusKey == "F", $"setup: focus key is '{CraftSearch.DebugFocusKey}'");

            // Plain inventory.
            yield return Reopen(rig, null);
            if (!c.Check(SearchUi.Field != null && Shown(gui), "setup: plain inventory or search field missing"))
            {
                yield break;
            }
            var field = SearchUi.Field;
            yield return PressFocusKey();
            c.Check(field.isFocused, "plain inventory: the focus key did not put the cursor in the field");
            c.Check(field.text == "", $"plain inventory: text '{field.text}' in the field after the focus key");
            yield return Unfocus();
            yield return Close(rig);

            // At a station, with the menu open: cursor in the field, menu closed.
            var forge = new Box<CraftingStation>();
            yield return Open(rig, "forge", forge);
            if (!c.Check(forge.Value != null && SortButton() != null, "setup: forge or Sort button missing"))
            {
                yield break;
            }
            SendClick(SortButton().gameObject, PointerEventData.InputButton.Left);
            yield return Frames(2);
            yield return PressFocusKey();
            c.Check(field.isFocused && !SearchUi.MenuOpen, $"forge: focus key with the menu open: cursor in field {field.isFocused}, menu open {SearchUi.MenuOpen}");
            // Cursor already in the field: the key is plain text, nothing else happens.
            yield return PressFocusKey();
            c.Check(field.isFocused && Shown(gui), "forge: focus key while typing took the cursor away or closed the inventory");
            yield return Unfocus();

            // Console open: its field has the keyboard, ours must not take it.
            var console = Console.instance;
            if (console != null && console.m_chatWindow != null)
            {
                var wasOpen = console.m_chatWindow.gameObject.activeSelf;
                rig.Undo("console window", () =>
                {
                    console.m_chatWindow.gameObject.SetActive(wasOpen);
                    var es = EventSystem.current;
                    if (es != null && console.m_input != null && es.currentSelectedGameObject == console.m_input.gameObject)
                    {
                        es.SetSelectedGameObject(null);
                    }
                });
                console.m_chatWindow.gameObject.SetActive(true);
                yield return Frames(2);
                c.Check(Console.IsVisible(), "setup: console window not shown");
                yield return PressFocusKey();
                c.Check(!field.isFocused, "console open: the focus key moved the cursor to our field");
                console.m_chatWindow.gameObject.SetActive(wasOpen);
                if (EventSystem.current != null && console.m_input != null && EventSystem.current.currentSelectedGameObject == console.m_input.gameObject)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                }
                yield return Frames(3);
            }
            else
            {
                c.Check(false, "setup: no console window");
            }

            // Use on the same key (Use rebound to F): the press closes the inventory, no cursor in the field.
            if (!Shown(gui))
            {
                yield return Reopen(rig, forge.Value);
            }
            if (c.Check(Button("Use") != null, "setup: no Use button"))
            {
                yield return TapWithFocusKey(rig, "Use");
                c.Check(!Shown(gui), "Use on the focus key: the inventory did not close");
                c.Check(!field.isFocused && (Chat.instance == null || !Chat.instance.HasFocus()), "Use on the focus key: the field took the keyboard while the inventory closed");
                yield return Reopen(rig, forge.Value);
                c.Check(!field.isFocused, "Use on the focus key: cursor in the field after reopening");
                yield return Tap(rig, "Inventory");
                c.Check(!Shown(gui), "Use on the focus key: Tab does not close the inventory after reopening");
                yield return Frames(3);
            }

            // Inventory on the same key (Inventory rebound to F): the press opens the inventory without the cursor,
            // the next press closes it.
            yield return Close(rig);
            if (c.Check(Button("Inventory") != null, "setup: no Inventory button"))
            {
                yield return Frames(3);
                yield return TapWithFocusKey(rig, "Inventory");
                c.Check(Shown(gui), "Inventory on the focus key: the press did not open the inventory");
                c.Check(!field.isFocused, "Inventory on the focus key: the press that opened the inventory put the cursor in the field");
                yield return TapWithFocusKey(rig, "Inventory");
                c.Check(!Shown(gui), "Inventory on the focus key: the second press did not close the inventory");
                c.Check(!field.isFocused && (Chat.instance == null || !Chat.instance.HasFocus()), "Inventory on the focus key: the field has the keyboard after closing");
            }

            // Empty setting: no key focuses the field.
            rig.FocusKey(KeyboardShortcut.Empty);
            c.Check(CraftSearch.DebugFocusKey == "None", $"empty FocusSearchKey: key is '{CraftSearch.DebugFocusKey}'");
            yield return Reopen(rig, forge.Value);
            yield return PressFocusKey();
            c.Check(!field.isFocused && !CraftSearch.FocusKeyDown(), "empty FocusSearchKey: a key still puts the cursor in the field");
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.badkey (T34) ----------

    private static IEnumerator RunBadKey()
    {
        const string T = BadKeyName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            var gui = rig.Gui;
            var log = LogWatch.Instance;
            const string warning = "the game cannot read";
            var errors = log.ErrorCount;
            var mark = log.KeptWarnings;

            rig.FocusKey(new KeyboardShortcut(KeyCode.F13));
            var lines = log.Warnings().Skip(mark).Where(w => w.IndexOf(warning, StringComparison.Ordinal) >= 0).ToList();
            c.Check(lines.Count == 1 && lines[0].Contains("F13") && lines[0].Contains("search field is off"),
                $"FocusSearchKey = F13: {lines.Count} warning(s): {string.Join(" | ", lines.ToArray())}");
            c.Check(CraftSearch.DebugFocusKey == "None", $"FocusSearchKey = F13: the key is still '{CraftSearch.DebugFocusKey}'");

            var bench = new Box<CraftingStation>();
            yield return Open(rig, "piece_workbench", bench);
            if (!c.Check(bench.Value != null && SearchUi.Field != null && SortButton() != null, "setup: workbench, search field or Sort button missing"))
            {
                yield break;
            }
            var field = SearchUi.Field;
            rig.TeachRecipesOf(bench.Value);
            gui.UpdateCraftingPanel();
            yield return null;
            var all = Rows(gui);
            yield return PressFocusKey();
            c.Check(!field.isFocused && !CraftSearch.FocusKeyDown(), "FocusSearchKey = F13: a key press still puts the cursor in the field");
            yield return Frames(10);
            c.Check(log.WarningsWith(warning, mark) == 1, $"FocusSearchKey = F13: the warning is repeated ({log.WarningsWith(warning, mark)} lines)");
            c.Check(log.ErrorCount == errors, "FocusSearchKey = F13: an error was logged: " + string.Join(" | ", log.Errors().Skip(errors).Take(2).ToArray()));

            // Field (clicked) and menu still work.
            SendClick(field.gameObject, PointerEventData.InputButton.Left);
            yield return Until(() => field.isFocused, 0.5f);
            c.Check(field.isFocused, "FocusSearchKey = F13: a click no longer puts the cursor in the field");
            yield return Type("wood");
            CheckFilter(c, rig, all, "wood", "F13, workbench 'wood'");
            c.Check(gui.m_availableRecipes.Count < all.Count, $"FocusSearchKey = F13: typing does not filter ({gui.m_availableRecipes.Count} of {all.Count} rows)");
            yield return Unfocus();
            yield return Type("");
            SendClick(SortButton().gameObject, PointerEventData.InputButton.Left);
            c.Check(SearchUi.MenuOpen && MenuOf(gui) != null, "FocusSearchKey = F13: the sort menu does not open");
            // And an entry of it still sorts (then the sort the bench opened with is put back).
            var optionBefore = CraftSearch.Option;
            var chosen = new Box<bool>();
            yield return Choose(rig, RecipeCategory.Name, chosen);
            c.Check(chosen.Value && CraftSearch.Option == RecipeCategory.Name && SortLabel() == "Sort: Name (A-Z)" && gui.m_availableRecipes.Count == all.Count && !SearchUi.MenuOpen,
                $"FocusSearchKey = F13: choosing Name in the menu: entry clicked {chosen.Value}, button '{SortLabel()}', {gui.m_availableRecipes.Count} of {all.Count} rows");
            yield return Choose(rig, optionBefore, chosen);
            SearchUi.CloseMenu();

            // Other keys the game cannot read: same, one warning each.
            mark = log.KeptWarnings;
            rig.FocusKey(new KeyboardShortcut(KeyCode.Mouse5));
            c.Check(log.WarningsWith(warning, mark) == 1 && CraftSearch.DebugFocusKey == "None", $"FocusSearchKey = Mouse5: {log.WarningsWith(warning, mark)} warning(s), key '{CraftSearch.DebugFocusKey}'");
            mark = log.KeptWarnings;
            rig.FocusKey(new KeyboardShortcut(KeyCode.F, KeyCode.F13));
            c.Check(log.WarningsWith(warning, mark) == 1 && CraftSearch.DebugFocusKey == "None", $"FocusSearchKey = F + F13: {log.WarningsWith(warning, mark)} warning(s), key '{CraftSearch.DebugFocusKey}'");

            // Back to F: no new warning, the key works again.
            mark = log.KeptWarnings;
            rig.FocusKey(new KeyboardShortcut(KeyCode.F));
            c.Check(log.WarningsWith(warning, mark) == 0 && CraftSearch.DebugFocusKey == "F", $"FocusSearchKey = F again: {log.WarningsWith(warning, mark)} warning(s), key '{CraftSearch.DebugFocusKey}'");
            yield return PressFocusKey();
            c.Check(field.isFocused, "FocusSearchKey = F again: the key does not put the cursor in the field");
            c.Check(log.ErrorCount == errors, "an error was logged: " + string.Join(" | ", log.Errors().Skip(errors).Take(2).ToArray()));
            yield return Unfocus();
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.memory (T18) ----------

    private static IEnumerator RunMemory()
    {
        const string T = MemoryName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            yield return rig.UnlockAllRecipes(c);
            rig.ForgetSorts();
            rig.ClearCraftFilter();
            var gui = rig.Gui;
            var p = rig.P;
            var ok = new Box<bool>();
            Func<InventoryGui.RecipeDataPair, bool> isWeapon = r => InOption(r, RecipeCategory.Weapons);

            // Forge = Weapons, workbench = Name, plain inventory = first category it lists.
            var forge = new Box<CraftingStation>();
            yield return Open(rig, "forge", forge);
            if (!c.Check(forge.Value != null && SortButton() != null, "setup: forge or Sort button missing"))
            {
                yield break;
            }
            var forgeAll = Rows(gui);
            yield return Choose(rig, RecipeCategory.Weapons, ok);
            c.Check(ok.Value && CraftSearch.Option == RecipeCategory.Weapons, "setup: Weapons not chosen at the forge");
            yield return Close(rig);

            var bench = new Box<CraftingStation>();
            yield return Open(rig, "piece_workbench", bench, 0.4f, 4f);
            if (!c.Check(bench.Value != null, "setup: workbench missing"))
            {
                yield break;
            }
            c.Check(CraftSearch.Option == RecipeCategory.Default && SortLabel() == "Sort: Default", $"workbench opens on '{SortLabel()}' after Weapons at the forge");
            var benchAll = Rows(gui);
            yield return Choose(rig, RecipeCategory.Name, ok);
            c.Check(ok.Value && CraftSearch.Option == RecipeCategory.Name, "setup: Name not chosen at the workbench");
            var benchByName = Rows(gui);
            yield return Close(rig);

            yield return Reopen(rig, null);
            c.Check(CraftSearch.Option == RecipeCategory.Default && SortLabel() == "Sort: Default", $"plain inventory opens on '{SortLabel()}'");
            var handAll = Rows(gui);
            var counts = new int[RecipeCategory.Count];
            Recount(handAll, counts, new Sprite[RecipeCategory.Count]);
            var hand = counts[RecipeCategory.Tools] > 0 ? RecipeCategory.Tools : Enumerable.Range(RecipeCategory.Weapons, RecipeCategory.Count - RecipeCategory.Weapons).FirstOrDefault(o => counts[o] > 0);
            if (c.Check(hand >= RecipeCategory.Weapons, $"setup: hand crafting lists no category ({handAll.Count} rows)"))
            {
                yield return Choose(rig, hand, ok);
                c.Check(ok.Value && CraftSearch.Option == hand, "setup: category not chosen in the plain inventory");
            }
            yield return Close(rig);

            // Saved with the character: one part per station type.
            var forgeKey = SortMemory.StationKey(forge.Value);
            var benchKey = SortMemory.StationKey(bench.Value);
            p.m_customData.TryGetValue(SortMemory.Key, out var stored);
            var parts = (stored ?? "").Split('|').OrderBy(s => s, StringComparer.Ordinal).ToArray();
            var want = new[] { forgeKey + "=weapons", benchKey + "=name", "none=" + RecipeCategory.Id(hand) }.OrderBy(s => s, StringComparer.Ordinal).ToArray();
            c.Check(parts.SequenceEqual(want), $"character data '{SortMemory.Key}' is '{stored}', expected the parts {string.Join(" | ", want)}");
            c.Note($"station names used as keys: forge '{forgeKey}', workbench '{benchKey}', hand crafting 'none'; saved: '{stored}'");

            // Each opens with its own sort.
            yield return Reopen(rig, forge.Value);
            c.Check(SortLabel() == "Sort: Weapons" && SameOrder(Partition(forgeAll, isWeapon), Rows(gui)), $"forge reopened: '{SortLabel()}', weapons first {SameOrder(Partition(forgeAll, isWeapon), Rows(gui))}");
            yield return Close(rig);
            yield return Reopen(rig, bench.Value);
            c.Check(SortLabel() == "Sort: Name (A-Z)" && SameOrder(benchByName, Rows(gui)), $"workbench reopened: '{SortLabel()}'");
            yield return Close(rig);
            yield return Reopen(rig, null);
            c.Check(SortLabel() == "Sort: " + RecipeCategory.Label(hand) && CraftSearch.Option == hand, $"plain inventory reopened: '{SortLabel()}'");
            yield return Close(rig);

            // RememberSort off (inventory closed): every station opens on Default, a pick lasts until closing.
            RememberSortOverride = false;
            CraftSearch.OnRememberSortChanged();
            yield return Reopen(rig, forge.Value);
            c.Check(SortLabel() == "Sort: Default" && SameOrder(forgeAll, Rows(gui)), $"RememberSort off, forge: '{SortLabel()}', vanilla order {SameOrder(forgeAll, Rows(gui))}");
            yield return Choose(rig, RecipeCategory.Weapons, ok);
            c.Check(SortLabel() == "Sort: Weapons" && SameOrder(Partition(forgeAll, isWeapon), Rows(gui)), "RememberSort off: a pick does not apply");
            yield return Close(rig);
            yield return Reopen(rig, forge.Value);
            c.Check(SortLabel() == "Sort: Default" && SameOrder(forgeAll, Rows(gui)), $"RememberSort off, forge reopened after a pick: '{SortLabel()}'");
            yield return Close(rig);
            yield return Reopen(rig, bench.Value);
            c.Check(SortLabel() == "Sort: Default" && SameOrder(benchAll, Rows(gui)), $"RememberSort off, workbench: '{SortLabel()}'");
            yield return Close(rig);
            p.m_customData.TryGetValue(SortMemory.Key, out var whileOff);
            c.Check(whileOff == stored, $"RememberSort off changed the saved sorts: '{whileOff}'");

            // Back on, right after using the workbench: its saved sort without visiting another station first.
            RememberSortOverride = true;
            CraftSearch.OnRememberSortChanged();
            yield return Reopen(rig, bench.Value);
            c.Check(SortLabel() == "Sort: Name (A-Z)" && SameOrder(benchByName, Rows(gui)), $"RememberSort on again, workbench (used last): '{SortLabel()}'");
            yield return Close(rig);
            yield return Reopen(rig, forge.Value);
            c.Check(SortLabel() == "Sort: Weapons", $"RememberSort on again, forge: '{SortLabel()}'");
            yield return Close(rig);

            // Off right after using the forge, then on: forge Default, then Weapons.
            RememberSortOverride = false;
            CraftSearch.OnRememberSortChanged();
            yield return Reopen(rig, forge.Value);
            c.Check(SortLabel() == "Sort: Default" && SameOrder(forgeAll, Rows(gui)), $"RememberSort off right after the forge: forge opens on '{SortLabel()}'");
            yield return Close(rig);
            RememberSortOverride = true;
            CraftSearch.OnRememberSortChanged();
            yield return Reopen(rig, forge.Value);
            c.Check(SortLabel() == "Sort: Weapons" && SameOrder(Partition(forgeAll, isWeapon), Rows(gui)), $"RememberSort on right after the forge: forge opens on '{SortLabel()}'");

            // Off with the forge open on Weapons: vanilla order at once.
            RememberSortOverride = false;
            CraftSearch.OnRememberSortChanged();
            c.Check(CraftSearch.Option == RecipeCategory.Default && SortLabel() == "Sort: Default" && SameOrder(forgeAll, Rows(gui)),
                $"RememberSort off with the forge open: '{SortLabel()}', vanilla order {SameOrder(forgeAll, Rows(gui))}");
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.keeptext (T19) ----------

    private static IEnumerator RunKeepText()
    {
        const string T = KeepTextName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            yield return rig.UnlockAllRecipes(c);
            rig.ForgetSorts();
            rig.ClearCraftFilter();
            var gui = rig.Gui;
            var forge = new Box<CraftingStation>();
            yield return Open(rig, "forge", forge);
            if (!c.Check(forge.Value != null && SearchUi.Field != null, "setup: forge or search field missing"))
            {
                yield break;
            }
            var field = SearchUi.Field;
            var all = Rows(gui);

            // Default: cleared when the inventory closes.
            yield return Type("silver");
            var filtered = Rows(gui);
            c.Check(filtered.Count > 0 && filtered.Count < all.Count, $"setup: 'silver' leaves {filtered.Count} of {all.Count} rows");
            yield return Close(rig);
            c.Check(field.text == "" && CraftSearch.Term == "", $"inventory closed: the field still holds '{field.text}'");
            yield return Reopen(rig, forge.Value);
            c.Check(field.text == "" && SameOrder(all, Rows(gui)), $"forge reopened: text '{field.text}', {gui.m_availableRecipes.Count} of {all.Count} rows");

            // KeepSearchText: kept for the same station type only.
            KeepSearchTextOverride = true;
            yield return Type("silver");
            yield return Close(rig);
            c.Check(field.text == "silver", $"KeepSearchText on, inventory closed: field holds '{field.text}'");
            yield return Reopen(rig, forge.Value);
            c.Check(field.text == "silver" && CraftSearch.Term == "silver" && SameOrder(filtered, Rows(gui)),
                $"KeepSearchText on, forge reopened: text '{field.text}', {gui.m_availableRecipes.Count} rows ({filtered.Count} expected)");
            yield return Close(rig);
            var bench = new Box<CraftingStation>();
            yield return Open(rig, "piece_workbench", bench, 0.4f, 4f);
            if (c.Check(bench.Value != null, "setup: workbench missing"))
            {
                var benchRows = Rows(gui);
                c.Check(field.text == "" && CraftSearch.Term == "", $"KeepSearchText on, other station: field holds '{field.text}'");
                yield return Type("wood");
                c.Check(gui.m_availableRecipes.Count < benchRows.Count, "setup: 'wood' does not filter the workbench list");
                yield return Close(rig);
                yield return Reopen(rig, null);
                c.Check(field.text == "" && CraftSearch.Term == "", $"KeepSearchText on, plain inventory after the workbench: field holds '{field.text}'");
            }
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.upgrade (T20) ----------

    private static IEnumerator RunUpgrade()
    {
        const string T = UpgradeName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            yield return rig.UnlockAllRecipes(c);
            rig.ForgetSorts();
            rig.ClearCraftFilter();
            var gui = rig.Gui;
            var atgeir = rig.Give("AtgeirBronze", 1);
            var sword = rig.Give("SwordSilver", 1);
            var armor = rig.Give("ArmorWolfChest", 1);
            c.Check(atgeir != null && sword != null && armor != null, $"setup: items given: AtgeirBronze {atgeir != null}, SwordSilver {sword != null}, ArmorWolfChest {armor != null}");
            var armorRecipe = armor != null ? ObjectDB.instance.GetRecipe(armor) : null;
            c.Note("ArmorWolfChest is made at: " + (armorRecipe == null ? "no recipe" : armorRecipe.m_craftingStation != null ? armorRecipe.m_craftingStation.m_name : "no station (by hand)"));

            var forge = new Box<CraftingStation>();
            yield return Open(rig, "forge", forge);
            if (!c.Check(forge.Value != null && SearchUi.Field != null && SortButton() != null, "setup: forge, search field or Sort button missing"))
            {
                yield break;
            }
            var craftRows = Rows(gui);
            gui.OnTabUpgradePressed();
            yield return Frames(2);
            var all = Rows(gui);
            c.Check(gui.InUpradeTab() && all.Count > 0 && all.All(r => r.ItemData != null) && RowOf() != null && RowOf().gameObject.activeInHierarchy,
                $"Upgrade tab: {all.Count} rows, search row shown {RowOf() != null}");
            c.Check(IndexOfItem(all, sword) >= 0 && IndexOfItem(all, atgeir) >= 0, $"setup: Upgrade tab lists {Names(all)} (Silver Sword and Bronze Atgeir expected)");
            c.Note($"forge Upgrade tab: {Names(all)}; wolf chest listed: {IndexOfItem(all, armor) >= 0}");

            // Menu: only the categories of the upgrade rows.
            SendClick(SortButton().gameObject, PointerEventData.InputButton.Left);
            CheckMenu(c, gui, all, RecipeCategory.Default, "Upgrade tab");
            var craftCounts = new int[RecipeCategory.Count];
            var upgradeCounts = new int[RecipeCategory.Count];
            Recount(craftRows, craftCounts, new Sprite[RecipeCategory.Count]);
            Recount(all, upgradeCounts, new Sprite[RecipeCategory.Count]);
            var menu = MenuOf(gui);
            var leaked = new List<string>();
            for (var o = RecipeCategory.Weapons; o < RecipeCategory.Count && menu != null; o++)
            {
                if (craftCounts[o] > 0 && upgradeCounts[o] == 0 && menu.Find(OptionPrefix + RecipeCategory.Id(o)) != null)
                {
                    leaked.Add(RecipeCategory.Id(o));
                }
            }
            c.Check(leaked.Count == 0, "Upgrade tab: the menu lists categories only the Craft tab has: " + string.Join(", ", leaked.ToArray()));
            SearchUi.CloseMenu();

            // Search: name, or an ingredient of the NEXT level.
            yield return Type("silver");
            CheckFilter(c, rig, all, "silver", "Upgrade tab 'silver'");
            c.Check(IndexOfItem(Rows(gui), sword) >= 0 && IndexOfItem(Rows(gui), atgeir) < 0, $"Upgrade tab 'silver': {Names(Rows(gui))} (Silver Sword yes, Bronze Atgeir no)");
            yield return Type("");

            // Sort applies: a weapon type, then Name.
            var ok = new Box<bool>();
            if (FamilySkill(RecipeCategory.FromId("w_swords"), out var swords))
            {
                Func<InventoryGui.RecipeDataPair, bool> isSword = r => Named(r) && ItemKinds.Classify(Shared(r)) == ItemKind.Weapon && Shared(r).m_skillType == swords;
                yield return Choose(rig, RecipeCategory.FromId("w_swords"), ok);
                c.Check(ok.Value && SameOrder(Partition(all, isSword), Rows(gui)) && IndexOfItem(Rows(gui), sword) == 0,
                    $"Upgrade tab, Swords: {Names(Rows(gui))}");
            }
            yield return Choose(rig, RecipeCategory.Armor, ok);
            if (ok.Value)
            {
                c.Check(SameOrder(Partition(all, r => InOption(r, RecipeCategory.Armor)), Rows(gui)), $"Upgrade tab, Armor: {Names(Rows(gui))}");
            }
            else
            {
                c.Note("no armor row in the forge's Upgrade tab: Armor sort not tried here");
            }
            yield return Choose(rig, RecipeCategory.Default, ok);
            c.Check(SameOrder(all, Rows(gui)), "Upgrade tab, Default: not the vanilla order");
            gui.OnTabCraftPressed();
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.potential (T32) ----------

    private static IEnumerator RunPotential()
    {
        const string T = PotentialName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            yield return rig.UnlockAllRecipes(c);
            rig.ForgetSorts();
            rig.ClearCraftFilter();
            var gui = rig.Gui;

            // One weapon and one armor piece that can be refined, with their idols (lowest idol prefab name first).
            Recipe weaponRecipe = null, armorRecipe = null;
            Piece.Requirement weaponIdol = null, armorIdol = null;
            foreach (var recipe in ObjectDB.instance.m_recipes.OrderBy(IdolName, StringComparer.Ordinal))
            {
                if (recipe == null || !recipe.m_enabled || recipe.m_item == null || recipe.m_resources == null
                    || recipe.m_item.m_itemData.m_shared.m_maxQuality <= 1 || recipe.m_item.m_itemData.m_shared.m_dlc.Length > 0)
                {
                    continue;
                }
                var idol = recipe.m_resources.FirstOrDefault(q => q != null && q.m_upgraderResource && q.m_resItem != null);
                if (idol == null)
                {
                    continue;
                }
                RecipeCategory.Of(ItemKinds.Classify(recipe.m_item.m_itemData.m_shared), recipe.m_item.m_itemData.m_shared.m_skillType, out var first, out _);
                if (first == RecipeCategory.Weapons && weaponRecipe == null)
                {
                    weaponRecipe = recipe;
                    weaponIdol = idol;
                }
                else if (first == RecipeCategory.Armor && armorRecipe == null)
                {
                    armorRecipe = recipe;
                    armorIdol = idol;
                }
            }
            if (!c.Check(weaponRecipe != null && armorRecipe != null, $"setup: no weapon ({weaponRecipe != null}) or armor ({armorRecipe != null}) recipe with an idol requirement"))
            {
                yield break;
            }
            var weapon = rig.Give(weaponRecipe.m_item.name, 1);
            var armor = rig.Give(armorRecipe.m_item.name, 1);
            rig.Give(weaponIdol.m_resItem.name, 5);
            rig.Give(armorIdol.m_resItem.name, 5);
            var idols = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(IdolsGuid);
            c.Note($"weapon {weaponRecipe.m_item.name} + {weaponIdol.m_resItem.name}, armor {armorRecipe.m_item.name} + {armorIdol.m_resItem.name}; Idol Upgrades (MC) loaded: {idols}");
            if (!c.Check(weapon != null && armor != null, "setup: items not given"))
            {
                yield break;
            }

            var station = new Box<CraftingStation>();
            yield return Open(rig, "UpgradeStation", station, 0.8f);
            if (!c.Check(station.Value != null && station.Value.m_upgrader && SearchUi.Field != null && SortButton() != null,
                    "setup: Forge of Potential (UpgradeStation), search field or Sort button missing"))
            {
                yield break;
            }
            var all = Rows(gui);
            var row = RowOf();
            var scroll = ScrollOf(gui);
            c.Check(!gui.m_tabCraft.gameObject.activeSelf && gui.InUpradeTab(), "Forge of Potential: Craft tab shown or not on the Upgrade tab");
            c.Check(row != null && row.gameObject.activeInHierarchy && scroll != null
                    && RectIn(row, gui.m_crafting).yMin >= RectIn((RectTransform)scroll.transform, gui.m_crafting).yMax - 0.5f
                    && RectIn(ClipOf(gui, scroll), gui.m_crafting).yMax <= RectIn(row, gui.m_crafting).yMin + 0.5f,
                "Forge of Potential: the search row is not shown above the list");
            c.Check(IndexOfItem(all, weapon) >= 0 && IndexOfItem(all, armor) >= 0, $"setup: Forge of Potential lists {Names(all)}");
            SelfTest.Screenshot(T, "forge-of-potential");
            yield return null;
            yield return null;

            // Idol name as the requirement list shows it; a word of it; the item's own name.
            var field = SearchUi.Field;
            var weaponIdolName = Loc(weaponIdol.m_resItem.m_itemData.m_shared.m_name);
            yield return Type(weaponIdolName);
            var kept = CheckFilter(c, rig, all, Norm(field.text), $"Forge of Potential '{field.text}'");
            c.Check(IndexOfItem(Rows(gui), weapon) >= 0, $"Forge of Potential '{field.text}': the weapon that needs this idol is not listed ({Names(Rows(gui))})");
            c.Note($"idol name '{weaponIdolName}' keeps {kept.Count} of {all.Count} rows: {Names(kept)}");
            var words = weaponIdolName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var word = words.OrderByDescending(w => w.Length).FirstOrDefault() ?? weaponIdolName;
            yield return Type(word);
            CheckFilter(c, rig, all, Norm(field.text), $"Forge of Potential '{field.text}'");
            c.Check(IndexOfItem(Rows(gui), weapon) >= 0, $"Forge of Potential '{field.text}': the weapon is not listed");
            yield return Type("idol");
            CheckFilter(c, rig, all, "idol", "Forge of Potential 'idol'");
            var weaponName = Loc(weapon.m_shared.m_name);
            yield return Type(weaponName);
            CheckFilter(c, rig, all, Norm(field.text), $"Forge of Potential '{field.text}'");
            c.Check(IndexOfItem(Rows(gui), weapon) >= 0, $"Forge of Potential '{field.text}': that item is not listed");
            yield return Type("");
            c.Check(SameOrder(all, Rows(gui)), "Forge of Potential: full list not back");

            // Menu and category sorts.
            SendClick(SortButton().gameObject, PointerEventData.InputButton.Left);
            CheckMenu(c, gui, all, RecipeCategory.Default, "Forge of Potential");
            SearchUi.CloseMenu();
            var ok = new Box<bool>();
            yield return Choose(rig, RecipeCategory.Armor, ok);
            c.Check(ok.Value && SortLabel() == "Sort: Armor" && SameOrder(Partition(all, r => InOption(r, RecipeCategory.Armor)), Rows(gui))
                    && InOption(Rows(gui)[0], RecipeCategory.Armor), $"Forge of Potential, Armor: {Names(Rows(gui))}");
            yield return Choose(rig, RecipeCategory.Weapons, ok);
            c.Check(ok.Value && SortLabel() == "Sort: Weapons" && SameOrder(Partition(all, r => InOption(r, RecipeCategory.Weapons)), Rows(gui))
                    && InOption(Rows(gui)[0], RecipeCategory.Weapons), $"Forge of Potential, Weapons: {Names(Rows(gui))}");
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // Sort key for the idol pick: recipes with the lowest idol prefab name first ("Upgrader0Armor"...), others last.
    private static string IdolName(Recipe recipe)
    {
        if (recipe == null || recipe.m_resources == null)
        {
            return "~";
        }
        foreach (var req in recipe.m_resources)
        {
            if (req != null && req.m_upgraderResource && req.m_resItem != null)
            {
                return req.m_resItem.name;
            }
        }
        return "~";
    }

    // ---------- csearch.food (T21) ----------

    private static IEnumerator RunFood()
    {
        const string T = FoodName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            yield return rig.UnlockAllRecipes(c);
            rig.ForgetSorts();
            rig.ClearCraftFilter();
            var gui = rig.Gui;
            var ok = new Box<bool>();
            foreach (var name in new[] { "piece_cauldron", "piece_MeadCauldron", "piece_preptable" })
            {
                var box = new Box<CraftingStation>();
                yield return Open(rig, name, box);
                if (!c.Check(box.Value != null && SortButton() != null, $"setup: station '{name}' or Sort button missing"))
                {
                    yield return Close(rig);
                    rig.DestroySpawned();
                    continue;
                }
                var all = Rows(gui);
                var counts = new int[RecipeCategory.Count];
                Recount(all, counts, new Sprite[RecipeCategory.Count]);
                c.Check(all.Count > 0, $"{name}: no recipe listed");
                SendClick(SortButton().gameObject, PointerEventData.InputButton.Left);
                CheckMenu(c, gui, all, RecipeCategory.Default, name);
                SearchUi.CloseMenu();
                c.Note($"{name}: {all.Count} rows: {Histogram(counts)}");
                var odd = all.Where(r => !InOption(r, RecipeCategory.Food) && !InOption(r, RecipeCategory.Meads) && !InOption(r, RecipeCategory.Materials)).ToList();
                if (odd.Count > 0)
                {
                    c.Note($"{name}: not under Food, Meads and potions or Materials: " + string.Join(", ", odd.Take(12).Select(r =>
                    {
                        OptionsOf(r, out var first, out _);
                        return RowName(r) + " -> " + RecipeCategory.Id(first);
                    }).ToArray()));
                }
                var bases = all.Where(r => Named(r) && r.Recipe.m_item.name.StartsWith("MeadBase", StringComparison.Ordinal)).ToList();
                if (bases.Count > 0)
                {
                    OptionsOf(bases[0], out var first, out _);
                    c.Note($"{name}: {bases.Count} mead bases, e.g. {RowName(bases[0])} ({Shared(bases[0]).m_itemType}) -> {RecipeCategory.Id(first)}");
                }
                // Choosing a category moves its rows first.
                var pick = new[] { RecipeCategory.Food, RecipeCategory.Meads, RecipeCategory.Materials }.FirstOrDefault(o => counts[o] > 0 && counts[o] < all.Count);
                if (pick == 0)
                {
                    pick = Enumerable.Range(RecipeCategory.Weapons, RecipeCategory.Count - RecipeCategory.Weapons).FirstOrDefault(o => counts[o] > 0);
                }
                if (c.Check(pick >= RecipeCategory.Weapons, $"{name}: no category to choose"))
                {
                    yield return Choose(rig, pick, ok);
                    c.Check(ok.Value && SameOrder(Partition(all, r => InOption(r, pick)), Rows(gui)) && SortLabel() == "Sort: " + RecipeCategory.Label(pick),
                        $"{name}, {RecipeCategory.Id(pick)}: its rows are not first with the rest in vanilla order ({Names(Rows(gui), 6)}), button '{SortLabel()}'");
                    CheckPlacement(c, gui, name);
                }
                SelfTest.Screenshot(T, name);
                yield return null;
                yield return null;
                yield return Close(rig);
                rig.DestroySpawned();
            }
            // Fishing baits: where they are made and what they count as.
            var baits = new List<string>();
            foreach (var recipe in ObjectDB.instance.m_recipes)
            {
                if (recipe == null || recipe.m_item == null || !recipe.m_item.name.StartsWith("FishingBait", StringComparison.Ordinal))
                {
                    continue;
                }
                var shared = recipe.m_item.m_itemData.m_shared;
                RecipeCategory.Of(ItemKinds.Classify(shared), shared.m_skillType, out var first, out _);
                baits.Add($"{recipe.m_item.name} at {(recipe.m_craftingStation != null ? recipe.m_craftingStation.m_name : "hand")} ({shared.m_itemType}) -> {RecipeCategory.Id(first)}");
            }
            c.Note("fishing baits: " + (baits.Count > 0 ? string.Join("; ", baits.ToArray()) : "no recipe"));
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.tools (T22) ----------

    private static IEnumerator RunTools()
    {
        const string T = ToolsName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            yield return rig.UnlockAllRecipes(c);
            rig.ForgetSorts();
            rig.ClearCraftFilter();
            var gui = rig.Gui;

            // The table itself: tools and torch under Tools and light only; tankard not under Weapons or a weapon type.
            // ("Tankard under Other" is not met by the mod: that check live alone in csearch.bug.tankard.)
            foreach (var pair in new[]
                     {
                         new KeyValuePair<string, ItemKind>("Hammer", ItemKind.Tool), new KeyValuePair<string, ItemKind>("Hoe", ItemKind.Tool),
                         new KeyValuePair<string, ItemKind>("Cultivator", ItemKind.Tool), new KeyValuePair<string, ItemKind>("PickaxeAntler", ItemKind.SkillTool),
                         new KeyValuePair<string, ItemKind>("Torch", ItemKind.Torch),
                     })
            {
                var prefab = ObjectDB.instance.GetItemPrefab(pair.Key);
                var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (!c.Check(drop != null, $"no item prefab '{pair.Key}'"))
                {
                    continue;
                }
                var shared = drop.m_itemData.m_shared;
                var kind = ItemKinds.Classify(shared);
                RecipeCategory.Of(kind, shared.m_skillType, out var first, out var second);
                c.Check(kind == pair.Value && first == RecipeCategory.Tools && second == -1,
                    $"{pair.Key} ({shared.m_itemType}, {shared.m_skillType}, {shared.m_animationState}) counts as {kind} -> {RecipeCategory.Id(first)}{(second >= 0 ? " + " + RecipeCategory.Id(second) : "")}, expected {pair.Value} -> tools only");
                var recipe = ObjectDB.instance.GetRecipe(drop.m_itemData);
                c.Note($"{pair.Key} is made at: {(recipe == null ? "no recipe" : recipe.m_craftingStation != null ? recipe.m_craftingStation.m_name : "no station (by hand)")}");
            }
            var tankardPrefab = ObjectDB.instance.GetItemPrefab("Tankard");
            var tankard = tankardPrefab != null ? tankardPrefab.GetComponent<ItemDrop>() : null;
            if (c.Check(tankard != null, "no item prefab 'Tankard'"))
            {
                var shared = tankard.m_itemData.m_shared;
                RecipeCategory.Of(ItemKinds.Classify(shared), shared.m_skillType, out var first, out var second);
                c.Check(first != RecipeCategory.Weapons && second == -1,
                    $"Tankard ({shared.m_itemType}, {shared.m_skillType}, {shared.m_animationState}) -> {RecipeCategory.Id(first)}{(second >= 0 ? " + " + RecipeCategory.Id(second) : "")}: counts under Weapons or a weapon type");
                var recipe = ObjectDB.instance.GetRecipe(tankard.m_itemData);
                c.Note($"Tankard is made at: {(recipe == null ? "no recipe" : recipe.m_craftingStation != null ? recipe.m_craftingStation.m_name : "no station (by hand)")}");
            }

            // Workbench then plain inventory: Tools and light first, tools not counted under any weapon type, and the
            // Debug row lines say the same.
            var ok = new Box<bool>();
            var bench = new Box<CraftingStation>();
            foreach (var place in new[] { "piece_workbench", null })
            {
                var label = place ?? "plain inventory";
                CraftList.Clear(); // row dump of this list logged again
                LogWatch.Instance.TakeDebug();
                LogWatch.Instance.KeepDebug = true;
                rig.Undo("debug lines", () => LogWatch.Instance.KeepDebug = false);
                if (place != null)
                {
                    yield return Open(rig, place, bench);
                    if (!c.Check(bench.Value != null, "setup: workbench missing"))
                    {
                        continue;
                    }
                }
                else
                {
                    yield return Reopen(rig, null);
                }
                LogWatch.Instance.KeepDebug = false;
                var dump = string.Join("\n", LogWatch.Instance.TakeDebug().Where(l => l.StartsWith("Crafting list at station", StringComparison.Ordinal)).ToArray());
                if (!c.Check(SortButton() != null, $"{label}: Sort button missing"))
                {
                    continue;
                }
                var all = Rows(gui);
                var counts = new int[RecipeCategory.Count];
                Recount(all, counts, new Sprite[RecipeCategory.Count]);
                var tools = all.Where(r => InOption(r, RecipeCategory.Tools)).ToList();
                c.Note($"{label}: {all.Count} rows, Tools and light: {Names(tools, 12)}");
                if (!c.Check(tools.Count > 0, $"{label}: no tool or torch row listed"))
                {
                    yield return Close(rig);
                    continue;
                }
                yield return Choose(rig, RecipeCategory.Tools, ok);
                var rows = Rows(gui);
                c.Check(ok.Value && SortLabel() == "Sort: Tools and light" && SameOrder(Partition(all, r => InOption(r, RecipeCategory.Tools)), rows),
                    $"{label}, Tools and light: tools are not first with the rest in vanilla order ({Names(rows, 8)}), button '{SortLabel()}'");
                foreach (var name in new[] { "Hammer", "Hoe", "Cultivator", "PickaxeAntler", "Torch" })
                {
                    var i = IndexOf(rows, name);
                    if (i < 0)
                    {
                        continue;
                    }
                    c.Check(i < tools.Count, $"{label}: {name} is listed at {i}, not among the {tools.Count} tool rows on top");
                    OptionsOf(rows[i], out _, out var second);
                    c.Check(second == -1, $"{label}: {name} also counts under {RecipeCategory.Id(second)}");
                }
                var tankardRow = IndexOf(rows, "Tankard");
                if (tankardRow >= 0)
                {
                    OptionsOf(rows[tankardRow], out var first, out var second);
                    c.Check(first != RecipeCategory.Weapons && second == -1,
                        $"{label}: Tankard listed under {RecipeCategory.Id(first)}{(second >= 0 ? " + " + RecipeCategory.Id(second) : "")} (Weapons or a weapon type)");
                    c.Note($"{label}: the Tankard is listed here, under {RecipeCategory.Label(first)}");
                }
                // Debug row lines (first build of this list after a clear).
                c.Check(dump.Length > 0, $"{label}: no 'Crafting list at station' debug lines logged");
                foreach (var pair in new[]
                         {
                             new KeyValuePair<string, string>("PickaxeAntler", "-> SkillTool -> tools"), new KeyValuePair<string, string>("Torch", "-> Torch -> tools"),
                             new KeyValuePair<string, string>("Hammer", "-> Tool -> tools"),
                         })
                {
                    if (IndexOf(all, pair.Key) < 0)
                    {
                        continue;
                    }
                    var line = dump.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith(pair.Key + " ", StringComparison.Ordinal));
                    c.Check(line != null && line.TrimEnd().EndsWith(pair.Value, StringComparison.Ordinal), $"{label}: debug line of {pair.Key} is '{line}', expected to end with '{pair.Value}'");
                }
                yield return Close(rig);
            }
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.bug.tankard (T22, real bug) ----------

    // REAL BUG, alone here so csearch.tools stay green for the rest of T22. README ("Tankards go under Other") and
    // T22 want the Tankard under Other. Game 1.0.17: Tankard = OneHandedWeapon, skill Swords, animation Torch (not
    // Feaster), so shared ItemKinds.Classify say Torch and the row land under Tools and light. Fail until mod fixed.
    private static IEnumerator RunBugTankard()
    {
        const string T = BugTankardName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            yield return rig.UnlockAllRecipes(c);
            rig.ForgetSorts();
            rig.ClearCraftFilter();
            var gui = rig.Gui;
            var prefab = ObjectDB.instance.GetItemPrefab("Tankard");
            var tankard = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (!c.Check(tankard != null, "no item prefab 'Tankard'"))
            {
                yield break;
            }
            var shared = tankard.m_itemData.m_shared;
            var kind = ItemKinds.Classify(shared);
            RecipeCategory.Of(kind, shared.m_skillType, out var first, out var second);
            c.Check(first == RecipeCategory.Other && second == -1,
                $"Tankard ({shared.m_itemType}, {shared.m_skillType}, {shared.m_animationState}) counts as {kind} -> {RecipeCategory.Id(first)}, expected other");

            // At the workbench (where the game make it): sort Other must put the Tankard row in the top block.
            var bench = new Box<CraftingStation>();
            yield return Open(rig, "piece_workbench", bench);
            if (c.Check(bench.Value != null && SortButton() != null, "setup: workbench or Sort button missing"))
            {
                var all = Rows(gui);
                if (IndexOf(all, "Tankard") < 0)
                {
                    c.Note("the workbench does not list the Tankard: only the category table was checked");
                }
                else
                {
                    var others = all.Count(r => InOption(r, RecipeCategory.Other));
                    var ok = new Box<bool>();
                    yield return Choose(rig, RecipeCategory.Other, ok);
                    var at = IndexOf(Rows(gui), "Tankard");
                    OptionsOf(all[IndexOf(all, "Tankard")], out var rowFirst, out _);
                    c.Check(ok.Value && at >= 0 && at < others,
                        $"workbench: Tankard listed under {RecipeCategory.Label(rowFirst)}, expected Other (menu has an Other entry: {ok.Value}, Other rows: {others}, Tankard at row {at} on sort '{SortLabel()}')");
                }
            }
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.craft (T23, M02) ----------

    private static bool ContainsBytes(byte[] data, string text)
    {
        if (data == null)
        {
            return false;
        }
        var find = Encoding.UTF8.GetBytes(text);
        for (var i = 0; i + find.Length <= data.Length; i++)
        {
            var j = 0;
            while (j < find.Length && data[i + j] == find[j])
            {
                j++;
            }
            if (j == find.Length)
            {
                return true;
            }
        }
        return false;
    }

    // Press craft like a click on the button, then skip the timer (next UpdateRecipe runs DoCrafting).
    private static IEnumerator CraftNow(Checks c, InventoryGui gui, string label)
    {
        SendClick(gui.m_craftButton.gameObject, PointerEventData.InputButton.Left);
        yield return null;
        if (c.Check(gui.m_craftTimer >= 0f, $"{label}: the craft did not start"))
        {
            gui.m_craftTimer = 1000f;
        }
        yield return Frames(3);
    }

    // Crafted item is a plain item: no data of this mod on it, nor in what a dropped copy stores for other players.
    private static void CheckPlainItem(Checks c, Rig rig, ItemDrop.ItemData item, Box<ItemDrop> dropped, string label)
    {
        if (!c.Check(item != null, $"{label}: crafted item not found in the inventory"))
        {
            return;
        }
        c.Check(!item.m_customData.Keys.Any(k => k.StartsWith("MC.UX.Crafting", StringComparison.Ordinal)), $"{label}: the crafted item carries data of this mod: {string.Join(", ", item.m_customData.Keys.ToArray())}");
        c.Check(item.m_crafterID == rig.P.GetPlayerID() && item.m_crafterName == rig.P.GetPlayerName(), $"{label}: crafter is '{item.m_crafterName}'");
        var pos = rig.Home - rig.HomeForward * 6f + Vector3.up;
        var drop = ItemDrop.DropItem(item, 1, pos, Quaternion.identity);
        dropped.Value = drop;
        if (!c.Check(drop != null, $"{label}: could not drop a copy"))
        {
            return;
        }
        rig.Spawned(drop.gameObject);
        var view = drop.GetComponent<ZNetView>();
        var zdo = view != null ? view.GetZDO() : null;
        var data = zdo != null ? zdo.GetByteArray(ZDOVars.s_itemData) : null;
        c.Check(data != null && data.Length > 0 && !ContainsBytes(data, "MC.UX.Crafting"), $"{label}: the dropped item's saved data ({(data != null ? data.Length : 0)} bytes) is missing or names this mod");
        c.Check(drop.m_itemData.m_shared.m_name == item.m_shared.m_name && drop.m_itemData.m_stack == 1 && drop.m_itemData.m_quality == item.m_quality,
            $"{label}: the dropped copy is not the same plain item");
    }

    private static IEnumerator RunCraft()
    {
        const string T = CraftName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            yield return rig.UnlockAllRecipes(c);
            rig.ForgetSorts();
            rig.ClearCraftFilter();
            var gui = rig.Gui;
            var bench = new Box<CraftingStation>();
            yield return Open(rig, "piece_workbench", bench);
            if (!c.Check(bench.Value != null && SearchUi.Field != null && SortButton() != null, "setup: workbench, search field or Sort button missing"))
            {
                yield break;
            }
            var field = SearchUi.Field;
            var arrowAt = IndexOf(gui, "ArrowWood");
            if (!c.Check(arrowAt >= 0, "setup: the workbench does not list Wood Arrows (ArrowWood)"))
            {
                yield break;
            }
            var recipe = gui.m_availableRecipes[arrowAt].Recipe;
            var arrowName = recipe.m_item.m_itemData.m_shared.m_name;
            foreach (var req in recipe.m_resources)
            {
                if (req != null && req.m_resItem != null && !req.m_upgraderResource && req.GetAmount(1) > 0)
                {
                    rig.Give(req.m_resItem.name, req.GetAmount(1) * 8);
                }
            }
            gui.UpdateCraftingPanel();
            yield return null;
            var all = Rows(gui);
            var ok = new Box<bool>();
            yield return Choose(rig, RecipeCategory.Name, ok);
            yield return Type("arrow");
            var filtered = Rows(gui);
            CheckFilter(c, rig, all, "arrow", "workbench 'arrow'");
            c.Check(ok.Value && filtered.Count > 0 && filtered.Count < all.Count, $"setup: Name + 'arrow' leaves {filtered.Count} of {all.Count} rows");

            // One craft.
            arrowAt = IndexOf(gui, "ArrowWood");
            gui.SetRecipe(arrowAt, false);
            yield return Frames(2);
            c.Check(gui.m_craftButton.interactable, "setup: Wood Arrows cannot be crafted (materials given, workbench usable)");
            var before = rig.Inv.CountItems(arrowName);
            rig.NoCraftBonus();
            yield return CraftNow(c, gui, "craft x1");
            c.Check(rig.Inv.CountItems(arrowName) == before + recipe.m_amount, $"craft x1: {rig.Inv.CountItems(arrowName) - before} arrows made, {recipe.m_amount} expected");
            c.Check(field.text == "arrow" && CraftSearch.Term == "arrow" && CraftSearch.Option == RecipeCategory.Name && SortLabel() == "Sort: Name (A-Z)",
                $"after the craft: text '{field.text}', button '{SortLabel()}'");
            CheckFilter(c, rig, all, "arrow", "after the craft");
            c.Check(SameOrder(filtered, Rows(gui)), $"after the craft: the list is not the same filtered and sorted list ({Names(Rows(gui), 6)})");
            c.Check(IndexOf(gui, "ArrowWood") >= 0 && ReferenceEquals(gui.m_selectedRecipe.Recipe, recipe), "after the craft: Wood Arrows no longer selected");

            // M02: what was crafted is a plain item.
            var dropped = new Box<ItemDrop>();
            CheckPlainItem(c, rig, rig.Inv.GetAllItems().FirstOrDefault(i => i.m_shared.m_name == arrowName), dropped, "crafted arrows");
            yield return null;

            // x5 (Shift held = game button AltPlace).
            before = rig.Inv.CountItems(arrowName);
            var shift = Button("AltPlace");
            if (shift != null)
            {
                rig.Pressed(shift);
                shift.Press();
                yield return Frames(2);
            }
            if (shift == null || !ZInput.GetButton("AltPlace"))
            {
                c.Note("AltPlace button not held: multi-craft asked the way a long press on a touch screen does");
                gui.m_touchMultiCrafting = true;
            }
            rig.NoCraftBonus();
            SendClick(gui.m_craftButton.gameObject, PointerEventData.InputButton.Left);
            yield return null;
            if (shift != null)
            {
                shift.Release();
            }
            c.Check(gui.m_craftTimer >= 0f && gui.m_multiCrafting, $"craft x5: started {gui.m_craftTimer >= 0f}, as a x5 craft {gui.m_multiCrafting}");
            if (gui.m_craftTimer >= 0f)
            {
                gui.m_craftTimer = 1000f;
            }
            yield return Frames(3);
            c.Check(rig.Inv.CountItems(arrowName) == before + recipe.m_amount * gui.m_multiCraftAmount,
                $"craft x5: {rig.Inv.CountItems(arrowName) - before} arrows made, {recipe.m_amount * gui.m_multiCraftAmount} expected");
            c.Check(field.text == "arrow" && SameOrder(filtered, Rows(gui)), "after the x5 craft: the list is not the same filtered and sorted list");

            // Typing during a craft: the craft keeps its item, the list waits, then updates.
            arrowAt = IndexOf(gui, "ArrowWood");
            gui.SetRecipe(arrowAt, false);
            yield return Frames(2);
            before = rig.Inv.CountItems(arrowName);
            rig.NoCraftBonus();
            SendClick(gui.m_craftButton.gameObject, PointerEventData.InputButton.Left);
            yield return null;
            c.Check(gui.m_craftTimer >= 0f, "third craft did not start");
            var objects = gui.m_availableRecipes.Select(r => r.InterfaceElement).ToList();
            field.text = "wood";
            yield return new WaitForSecondsRealtime(0.35f);
            var stillCrafting = gui.m_craftTimer >= 0f;
            c.Check(stillCrafting, "the craft ended within 0.35 s (shortened by another mod?): 'typing during a craft' could not be checked");
            if (stillCrafting)
            {
                var untouched = gui.m_availableRecipes.Count == objects.Count;
                for (var i = 0; untouched && i < objects.Count; i++)
                {
                    untouched = objects[i] != null && ReferenceEquals(objects[i], gui.m_availableRecipes[i].InterfaceElement);
                }
                c.Check(untouched && CraftSearch.Pending && CraftSearch.Term == "wood", $"typing during a craft: list rebuilt {!untouched}, refresh waiting {CraftSearch.Pending}");
                c.Check(ReferenceEquals(gui.m_craftRecipe, recipe) && ReferenceEquals(gui.m_selectedRecipe.Recipe, recipe), "typing during a craft: the craft or the selection changed item");
                gui.m_craftTimer = 1000f;
            }
            yield return Frames(4);
            c.Check(rig.Inv.CountItems(arrowName) == before + recipe.m_amount, $"typing during a craft: {rig.Inv.CountItems(arrowName) - before} arrows made, {recipe.m_amount} expected");
            yield return Until(() => !CraftSearch.Pending, 1f);
            c.Check(!CraftSearch.Pending && field.text == "wood", "after the craft: the new search was not applied");
            // Order: Name, on the full list as it is now (craftable flags do not matter for Name).
            CheckFilter(c, rig, all, "wood", "after the craft, 'wood'");
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.nocost (T24) ----------

    private static IEnumerator RunNoCost()
    {
        const string T = NoCostName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            rig.ForgetSorts();
            rig.ClearCraftFilter();
            var gui = rig.Gui;
            rig.NoCost(true);
            c.Check(rig.P.NoCostCheat(), "setup: nocost not on");
            var bench = new Box<CraftingStation>();
            yield return Open(rig, "piece_workbench", bench);
            if (!c.Check(bench.Value != null && SearchUi.Field != null && SortButton() != null, "setup: workbench, search field or Sort button missing"))
            {
                yield break;
            }
            var all = Rows(gui);
            // With nocost every enabled recipe is listed, whatever its station.
            var missing = 0;
            var total = 0;
            foreach (var recipe in ObjectDB.instance.m_recipes)
            {
                if (recipe == null || !recipe.m_enabled || recipe.m_item == null || recipe.m_noCraftOnlyUpgrade)
                {
                    continue;
                }
                var dlc = recipe.m_item.m_itemData.m_shared.m_dlc;
                if (dlc.Length > 0 && !DLCMan.instance.IsDLCInstalled(dlc))
                {
                    continue;
                }
                total++;
                if (!all.Any(r => ReferenceEquals(r.Recipe, recipe)))
                {
                    missing++;
                }
            }
            c.Check(total > 50 && missing == 0, $"nocost: {missing} of {total} recipes of the game are not listed ({all.Count} rows)");
            var other = all.Count(r => Named(r) && r.Recipe.m_craftingStation != null && r.Recipe.m_craftingStation.m_name != bench.Value.m_name);
            c.Check(other > 0, "nocost: the workbench lists no recipe of another station");

            yield return Type("silver");
            var silver = CheckFilter(c, rig, all, "silver", "nocost 'silver'");
            c.Check(silver.Count > 0 && silver.Count < all.Count && IndexOf(gui, "SwordSilver") >= 0, $"nocost 'silver': {silver.Count} of {all.Count} rows");
            var ok = new Box<bool>();
            yield return Choose(rig, RecipeCategory.Weapons, ok);
            c.Check(ok.Value && SameOrder(Partition(silver, r => InOption(r, RecipeCategory.Weapons)), Rows(gui)), $"nocost, Weapons + 'silver': {Names(Rows(gui), 8)}");
            yield return Type("");
            c.Check(SameOrder(Partition(all, r => InOption(r, RecipeCategory.Weapons)), Rows(gui)) && SortLabel() == "Sort: Weapons", "nocost, Weapons: weapons are not first with the rest in vanilla order");
            CheckPlacement(c, gui, "nocost");
            yield return Choose(rig, RecipeCategory.Default, ok);
            c.Check(SameOrder(all, Rows(gui)), "nocost, Default: not the vanilla list");
            c.Note($"nocost at the workbench: {all.Count} rows ({other} of other stations), 'silver' {silver.Count}");
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.console (T25) ----------

    // Vanilla list comparers (InventoryGui.UpdateRecipeList): craftable first, list weight, then name token
    // (Original) or item weight then shown name (Weight), then higher quality first.
    private static int VanillaCompare(InventoryGui.RecipeDataPair a, InventoryGui.RecipeDataPair b, bool byWeight)
    {
        var n = b.CanCraft.CompareTo(a.CanCraft);
        if (n == 0)
        {
            n = a.Recipe.m_listSortWeight.CompareTo(b.Recipe.m_listSortWeight);
        }
        var sa = Shared(a);
        var sb = Shared(b);
        if (n == 0 && byWeight)
        {
            n = sa.m_weight.CompareTo(sb.m_weight);
            if (n == 0)
            {
                n = Loc(sa.m_name).CompareTo(Loc(sb.m_name));
            }
        }
        else if (n == 0)
        {
            n = sa.m_name.CompareTo(sb.m_name);
        }
        if (n == 0 && a.ItemData != null && b.ItemData != null)
        {
            n = b.ItemData.m_quality.CompareTo(a.ItemData.m_quality);
        }
        return n;
    }

    private static int OutOfOrder(List<InventoryGui.RecipeDataPair> rows, bool byWeight)
    {
        var bad = 0;
        for (var i = 1; i < rows.Count; i++)
        {
            if (Named(rows[i - 1]) && Named(rows[i]) && VanillaCompare(rows[i - 1], rows[i], byWeight) > 0)
            {
                bad++;
            }
        }
        return bad;
    }

    private static IEnumerator RunConsole()
    {
        const string T = ConsoleName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            yield return rig.UnlockAllRecipes(c);
            rig.ForgetSorts();
            rig.ClearCraftFilter();
            rig.KeepCraftSort();
            var gui = rig.Gui;
            var console = Console.instance;
            var forge = new Box<CraftingStation>();
            yield return Open(rig, "forge", forge);
            if (!c.Check(forge.Value != null && SearchUi.Field != null && console != null, "setup: forge, search field or console missing"))
            {
                yield break;
            }
            var all = Rows(gui);
            c.Check(OutOfOrder(all, false) == 0, $"setup: the Default list is not in the game's own order ({OutOfOrder(all, false)} pairs)");

            // filtercraft bronze + our 'sword': both apply.
            console.TryRunCommand("filtercraft bronze");
            c.Check(Player.s_FilterCraft.Count == 1 && Player.s_FilterCraft[0] == "bronze", $"setup: console filter is [{string.Join(",", Player.s_FilterCraft.ToArray())}]");
            yield return Type("sword");
            var saved = gui.m_selectedRecipe;
            var expected = new List<InventoryGui.RecipeDataPair>();
            foreach (var row in all)
            {
                if (!Named(row))
                {
                    continue;
                }
                var shared = Shared(row);
                var vanilla = row.Recipe.m_item.name.ToLower().Contains("bronze") || shared.m_name.ToLower().Contains("bronze") || Loc(shared.m_name).ToLower().Contains("bronze");
                if (vanilla && MatchReason(gui, rig.P, row, "sword") != null)
                {
                    expected.Add(row);
                }
            }
            gui.m_selectedRecipe = saved;
            var both = Rows(gui);
            c.Check(SameOrder(expected, both), $"'filtercraft bronze' + 'sword': listed {Names(both, 6)}, rows matching both: {Names(expected, 6)}");
            c.Check(IndexOf(both, "SwordBronze") >= 0, "'filtercraft bronze' + 'sword': the Bronze Sword is not listed");
            c.Note($"'filtercraft bronze' + 'sword': {Names(both, 8)}");

            // filtercraft (no argument) clears it: our search alone again.
            console.TryRunCommand("filtercraft");
            c.Check(Player.s_FilterCraft.Count == 0, "setup: 'filtercraft' did not clear the console filter");
            gui.UpdateCraftingPanel();
            yield return null;
            CheckFilter(c, rig, all, "sword", "filter cleared, 'sword'");
            c.Check(gui.m_availableRecipes.Count >= both.Count, "filter cleared: fewer rows than with the console filter");
            yield return Type("");
            c.Check(SameOrder(all, Rows(gui)), "filter cleared, empty search: not the full list");

            // sortcraft Weight with our sort on Default: the game's weight order, untouched.
            console.TryRunCommand("sortcraft Weight");
            c.Check(rig.P.TryGetUniqueKeyValue("sortcraft", out var value) && value == "Weight", "setup: 'sortcraft Weight' not stored");
            gui.UpdateCraftingPanel();
            yield return null;
            var byWeight = Rows(gui);
            c.Check(byWeight.Count == all.Count && OutOfOrder(byWeight, true) == 0 && CraftSearch.Option == RecipeCategory.Default,
                $"'sortcraft Weight' on Default: {OutOfOrder(byWeight, true)} pair(s) not in the game's weight order");
            if (SameOrder(all, byWeight))
            {
                c.Note("the weight order equals the normal order at this forge: the 'sortcraft Weight' check is weak here");
            }
            // And a category on top of it: stable from the weight order.
            var ok = new Box<bool>();
            yield return Choose(rig, RecipeCategory.Weapons, ok);
            c.Check(ok.Value && SameOrder(Partition(byWeight, r => InOption(r, RecipeCategory.Weapons)), Rows(gui)), "'sortcraft Weight' + Weapons: weapons first is not taken from the weight order");
            yield return Choose(rig, RecipeCategory.Default, ok);

            // sortcraft (no argument) resets.
            console.TryRunCommand("sortcraft");
            c.Check(!rig.P.TryGetUniqueKeyValue("sortcraft", out _), "setup: 'sortcraft' did not reset");
            gui.UpdateCraftingPanel();
            yield return null;
            c.Check(SameOrder(all, Rows(gui)) && OutOfOrder(Rows(gui), false) == 0, "'sortcraft' reset: not the normal order");
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.language (T26) ----------

    // What Localization.SetLanguage does for the game's own words: load the language with the game's loader, drop the
    // Localize cache, tell everybody (OnLanguageChange). Not done: saving the choice in the player's settings, and
    // dropping all words first (words other mods added at start would be gone for the rest of the session).
    private static bool SwitchLanguage(string language)
    {
        var loc = Localization.instance;
        // English first, then the language on top: how the game builds its word list at start.
        var loaded = loc.SetupLanguage("English");
        if (language != "English")
        {
            loaded = loc.SetupLanguage(language);
        }
        var gui = InventoryGui.instance;
        var row = RowOf();
        // Only public door to the cache: it also re-reads registered texts under the given object (ours has none).
        loc.ReLocalizeAll(row != null ? row : gui.m_crafting);
        var changed = Localization.OnLanguageChange;
        if (changed != null)
        {
            changed();
        }
        return loaded;
    }

    private static IEnumerator RunLanguage()
    {
        const string T = LanguageName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            yield return rig.UnlockAllRecipes(c);
            rig.ForgetSorts();
            rig.ClearCraftFilter();
            var gui = rig.Gui;
            var original = Localization.instance.GetSelectedLanguage();
            var other = original == "German" ? "French" : "German";
            var forge = new Box<CraftingStation>();
            yield return Open(rig, "forge", forge);
            if (!c.Check(forge.Value != null && SearchUi.Field != null && SortButton() != null, "setup: forge, search field or Sort button missing"))
            {
                yield break;
            }
            var field = SearchUi.Field;
            var all = Rows(gui);
            var swordAt = IndexOf(all, "SwordSilver");
            var family = RecipeCategory.FromId("w_swords");
            var ok = new Box<bool>();
            yield return Choose(rig, family, ok);
            if (!c.Check(ok.Value && swordAt >= 0, "setup: no Swords entry in the menu or no Silver Sword at the forge"))
            {
                yield break;
            }
            var swordToken = Shared(all[swordAt]).m_name;
            var oldSkill = Loc("$skill_swords");
            var oldName = Loc(swordToken);
            var oldLabel = SortLabel();
            c.Check(oldLabel == "Sort: " + oldSkill, $"setup: button reads '{oldLabel}' in {original}");

            // Language change, as the settings screen does it (not saved).
            rig.Undo("language", () => SwitchLanguage(original));
            var loaded = SwitchLanguage(other);
            yield return Frames(2);
            var newSkill = Loc("$skill_swords");
            var newName = Loc(swordToken);
            c.Check(loaded && (newSkill != oldSkill || newName != oldName), $"setup: {other} not loaded ('{newSkill}', '{newName}')");
            c.Check(SortLabel() == "Sort: " + newSkill, $"{other}: button reads '{SortLabel()}', the skill is called '{newSkill}'");

            // Reopen the forge.
            yield return Close(rig);
            yield return Reopen(rig, forge.Value);
            c.Check(SortLabel() == "Sort: " + newSkill && CraftSearch.Option == family, $"{other}, forge reopened: button reads '{SortLabel()}'");
            SendClick(SortButton().gameObject, PointerEventData.InputButton.Left);
            var menu = MenuOf(gui);
            if (c.Check(menu != null, $"{other}: menu did not open"))
            {
                var counts = new int[RecipeCategory.Count];
                Recount(all, counts, new Sprite[RecipeCategory.Count]);
                var wrong = new List<string>();
                for (var i = 0; i < menu.childCount; i++)
                {
                    var entry = menu.GetChild(i);
                    if (!entry.gameObject.activeSelf || !entry.name.StartsWith(OptionPrefix, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    var id = entry.name.Substring(OptionPrefix.Length);
                    var option = RecipeCategory.FromId(id);
                    var text = entry.Find("name") != null ? entry.Find("name").GetComponent<TMP_Text>().text : "";
                    string want;
                    if (FamilySkill(option, out var skill))
                    {
                        var word = Loc("$skill_" + skill.ToString().ToLowerInvariant());
                        want = (string.IsNullOrEmpty(word) || word.StartsWith("[", StringComparison.Ordinal) ? null : word + " (" + counts[option] + ")");
                    }
                    else
                    {
                        want = EnglishLabel(id) + (option == RecipeCategory.Default || option == RecipeCategory.Name ? "" : " (" + counts[option] + ")");
                    }
                    if (want != null && text != want)
                    {
                        wrong.Add($"{id}: '{text}', expected '{want}'");
                    }
                }
                c.Check(wrong.Count == 0, $"{other}: menu labels: {string.Join("; ", wrong.ToArray())}");
            }
            SearchUi.CloseMenu();

            // Search uses the new names.
            yield return Type(newName);
            CheckFilter(c, rig, all, Norm(field.text), $"{other} '{field.text}'");
            c.Check(IndexOf(gui, "SwordSilver") >= 0, $"{other}: typing the new name '{newName}' does not list the Silver Sword");
            if (Norm(newName) != Norm(oldName))
            {
                yield return Type(oldName);
                var byOldName = CheckFilter(c, rig, all, Norm(field.text), $"{other} '{field.text}' (old name)");
                c.Note($"{other}: the old name '{oldName}' now keeps {byOldName.Count} row(s)");
            }
            yield return Type("");
            var hint = field.placeholder as TMP_Text;
            c.Note($"{original} -> {other}: Swords '{oldSkill}' -> '{newSkill}', Silver Sword '{oldName}' -> '{newName}', hint text in the empty field: '{(hint != null ? hint.text : "none")}'");

            // And back.
            SwitchLanguage(original);
            yield return Frames(2);
            c.Check(SortLabel() == oldLabel, $"back to {original}: button reads '{SortLabel()}'");
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // English labels of the options that never follow the game language (ids are the saved ones).
    private static string EnglishLabel(string id)
    {
        switch (id)
        {
            case "default": return "Default";
            case "name": return "Name (A-Z)";
            case "weapons": return "Weapons";
            case "shields": return "Shields";
            case "armor": return "Armor";
            case "helmets": return "Helmets";
            case "chest": return "Chest armor";
            case "legs": return "Leg armor";
            case "capes": return "Capes";
            case "ammo": return "Ammo";
            case "tools": return "Tools and light";
            case "food": return "Food";
            case "meads": return "Meads and potions";
            case "trinkets": return "Trinkets";
            case "utility": return "Utility";
            case "materials": return "Materials";
            case "fish": return "Fish";
            case "trophies": return "Trophies";
            case "other": return "Other";
            default: return id;
        }
    }

    // ---------- csearch.click (T27) ----------

    private static IEnumerator RunClick()
    {
        const string T = ClickName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            var gui = rig.Gui;
            yield return Reopen(rig, null, 0.8f);
            if (!c.Check(SearchUi.Field != null && SortButton() != null && Shown(gui), "setup: plain inventory, search field or Sort button missing"))
            {
                yield break;
            }
            var field = SearchUi.Field;
            c.Check(gui.ActiveGroup != 3 && rig.P.GetCurrentCraftingStation() == null, $"setup: active panel group is {gui.ActiveGroup} (the player grid's expected)");
            Canvas.ForceUpdateCanvases();

            // Field: what is under the mouse at its centre, who gets the press and the click, then the cursor.
            var hit = TopHit((RectTransform)field.transform);
            c.Check(hit != null && hit.transform.IsChildOf(field.transform), $"at the centre of the search field the mouse hits '{PathOf(hit)}'");
            var target = hit != null && hit.transform.IsChildOf(field.transform) ? hit : field.gameObject;
            c.Check(ExecuteEvents.GetEventHandler<IPointerDownHandler>(target) == field.gameObject && ExecuteEvents.GetEventHandler<IPointerClickHandler>(target) == field.gameObject,
                "a press on the field is not handled by the field itself");
            var frame = Time.frameCount;
            SendClick(target, PointerEventData.InputButton.Left);
            yield return Until(() => field.isFocused, 0.5f);
            c.Check(field.isFocused && Time.frameCount - frame <= 3, $"click on the field: cursor in the field {field.isFocused}, after {Time.frameCount - frame} frame(s)");
            yield return Frames(2);
            c.Check(Chat.instance != null && Chat.instance.HasFocus() && Shown(gui), "click on the field: typing would not be safe (keys not held back) or the inventory closed");
            yield return Unfocus();

            // Sort button: menu at once.
            var button = SortButton();
            hit = TopHit(button);
            c.Check(hit != null && hit.transform.IsChildOf(button), $"at the centre of the Sort button the mouse hits '{PathOf(hit)}'");
            target = hit != null && hit.transform.IsChildOf(button) ? hit : button.gameObject;
            c.Check(ExecuteEvents.GetEventHandler<IPointerClickHandler>(target) == button.gameObject, "a click on the Sort button is not handled by the button itself");
            SendClick(target, PointerEventData.InputButton.Left);
            c.Check(SearchUi.MenuOpen && MenuOf(gui) != null, "click on Sort in the plain inventory: the menu did not open at once");
            yield return Frames(2);
            c.Check(SearchUi.MenuOpen, "click on Sort in the plain inventory: the menu closed again");
            // An entry is clickable too.
            var menu = MenuOf(gui);
            var entry = menu != null ? menu.Find(OptionPrefix + "name") as RectTransform : null;
            if (c.Check(entry != null, "menu has no Name entry"))
            {
                hit = TopHit(entry);
                c.Check(hit != null && hit.transform.IsChildOf(entry), $"at the centre of the menu entry the mouse hits '{PathOf(hit)}'");
                SendClick(hit != null && hit.transform.IsChildOf(entry) ? hit : entry.gameObject, PointerEventData.InputButton.Left);
                c.Check(CraftSearch.Option == RecipeCategory.Name && !SearchUi.MenuOpen, "click on the Name entry did not choose it");
            }
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.gamepad (T28) ----------

    private static IEnumerator RunGamepad()
    {
        const string T = GamepadName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            yield return rig.UnlockAllRecipes(c);
            rig.ForgetSorts();
            rig.ClearCraftFilter();
            var gui = rig.Gui;
            var forge = new Box<CraftingStation>();
            yield return Open(rig, "forge", forge);
            if (!c.Check(forge.Value != null && SearchUi.Field != null && SortButton() != null, "setup: forge, search field or Sort button missing"))
            {
                yield break;
            }
            var field = SearchUi.Field;
            var all = Rows(gui);

            // Remembered sort applies when the station is opened.
            var ok = new Box<bool>();
            yield return Choose(rig, RecipeCategory.Weapons, ok);
            yield return Close(rig);
            yield return Reopen(rig, forge.Value);
            var sorted = Rows(gui);
            c.Check(SortLabel() == "Sort: Weapons" && SameOrder(Partition(all, r => InOption(r, RecipeCategory.Weapons)), sorted), "forge reopened: the remembered sort is not applied");

            // Our controls: out of D-pad reach, no controller hotkey, no key hint.
            SendClick(SortButton().gameObject, PointerEventData.InputButton.Left);
            var row = RowOf();
            var menu = MenuOf(gui);
            c.Check(menu != null, "setup: menu not open");
            var reachable = new List<string>();
            var extras = 0;
            foreach (var root in new Transform[] { row, menu })
            {
                if (root == null)
                {
                    continue;
                }
                foreach (var s in root.GetComponentsInChildren<Selectable>(true))
                {
                    if (s.navigation.mode != Navigation.Mode.None)
                    {
                        reachable.Add(s.name);
                    }
                }
                extras += root.GetComponentsInChildren<UIGamePad>(true).Length + root.GetComponentsInChildren<UIInputHint>(true).Length;
            }
            c.Check(reachable.Count == 0, "controls that D-pad navigation can select: " + string.Join(", ", reachable.ToArray()));
            c.Check(extras == 0, $"{extras} controller hotkey or key hint component(s) left on our controls");

            // From the vanilla controls, every direction: never lands on ours.
            var sources = new List<Selectable> { gui.m_craftButton, gui.m_tabCraft, gui.m_tabUpgrade, gui.m_repairButton };
            if (sorted.Count > 0)
            {
                sources.Add(sorted[0].InterfaceElement != null ? sorted[0].InterfaceElement.GetComponent<Selectable>() : null);
                sources.Add(sorted[sorted.Count - 1].InterfaceElement != null ? sorted[sorted.Count - 1].InterfaceElement.GetComponent<Selectable>() : null);
            }
            var landed = new List<string>();
            var tried = 0;
            foreach (var source in sources)
            {
                if (source == null || !source.gameObject.activeInHierarchy)
                {
                    continue;
                }
                tried++;
                var targets = new[] { source.FindSelectableOnUp(), source.FindSelectableOnDown(), source.FindSelectableOnLeft(), source.FindSelectableOnRight() };
                var names = new[] { "up", "down", "left", "right" };
                for (var i = 0; i < targets.Length; i++)
                {
                    if (targets[i] != null && IsOurs(targets[i].transform, gui))
                    {
                        landed.Add($"{source.name} {names[i]} -> {targets[i].name}");
                    }
                }
            }
            c.Check(tried >= 3 && landed.Count == 0, $"D-pad from {tried} vanilla controls lands on ours: {string.Join(", ", landed.ToArray())}");

            // What the game selects by itself for a controller (default element of each panel group) is not ours.
            var defaults = new List<string>();
            var notes = new List<string>();
            for (var g = 0; g < gui.m_uiGroups.Length; g++)
            {
                var group = gui.m_uiGroups[g];
                if (group == null || group.m_defaultElement == null)
                {
                    continue;
                }
                var first = group.m_defaultElement.GetComponentInChildren<Selectable>(false);
                notes.Add($"group {g}: {group.m_defaultElement.name} -> {(first != null ? first.name : "nothing")}");
                if (first != null && IsOurs(first.transform, gui))
                {
                    defaults.Add($"group {g} ({group.name}) selects {first.name}");
                }
            }
            c.Check(defaults.Count == 0, "controller default selection lands on our controls: " + string.Join(", ", defaults.ToArray()));
            c.Note("controller default elements: " + string.Join("; ", notes.ToArray()));

            // B with the menu open: menu closes, inventory stays. Menu was opened by the click above on this very
            // frame (no yield since): wait two frames first, like csearch.menu. No hand press B in the frame of the
            // click, and the controller only hold B back from the frame after it saw the menu open (FocusGuard).
            yield return Frames(2);
            c.Check(SearchUi.MenuOpen && MenuOf(gui) != null && Shown(gui), $"setup: before B: menu open {SearchUi.MenuOpen}, inventory open {Shown(gui)}");
            yield return Tap(rig, "JoyButtonB");
            c.Check(!SearchUi.MenuOpen && MenuOf(gui) == null && Shown(gui), $"B with the menu open: menu open {SearchUi.MenuOpen}, inventory open {Shown(gui)}");

            // D-pad list navigation as in the game, on the sorted list.
            if (!Shown(gui))
            {
                yield return Reopen(rig, forge.Value);
            }
            gui.SetRecipe(0, false);
            yield return Frames(2);
            c.Check(gui.ActiveGroup == 3, $"setup: crafting group not the active one ({gui.ActiveGroup})");
            yield return Tap(rig, "JoyDPadDown");
            var rowsNow = Rows(gui);
            c.Check(rowsNow.Count > 1 && Same(gui.m_selectedRecipe, rowsNow[1]) && Marked(gui, 1), "D-pad down does not select the second row of the sorted list");
            yield return Tap(rig, "JoyDPadUp");
            c.Check(rowsNow.Count > 1 && Same(gui.m_selectedRecipe, rowsNow[0]) && Marked(gui, 0), "D-pad up does not go back to the first row");
            c.Check(Shown(gui) && !field.isFocused && !SearchUi.MenuOpen, "D-pad opened or focused one of our controls");

            // B while typing: cursor leaves, inventory stays; next B closes it.
            yield return Focus(c, "forge");
            yield return Tap(rig, "JoyButtonB");
            c.Check(!field.isFocused && Shown(gui), $"B while typing: cursor in field {field.isFocused}, inventory open {Shown(gui)}");
            yield return Frames(3);
            c.Check(Chat.instance == null || !Chat.instance.HasFocus(), "B while typing: game keys still held back afterwards");
            yield return Tap(rig, "JoyButtonB");
            c.Check(!Shown(gui), "second B: the inventory did not close");
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.toggle (T29, T30, T33, M03) ----------

    private sealed class Layout
    {
        internal Vector2 ScrollMax, ViewMin, ViewMax, ClipMin, ClipMax, BarMax;
        internal float BaseSize, Top;

        internal static Layout Take(InventoryGui gui)
        {
            var scroll = ScrollOf(gui);
            var scrollRT = (RectTransform)scroll.transform;
            var view = ViewOf(scroll);
            var clip = ClipOf(gui, scroll);
            var bar = gui.m_recipeListScroll != null ? gui.m_recipeListScroll.transform as RectTransform : null;
            Canvas.ForceUpdateCanvases();
            return new Layout
            {
                ScrollMax = scrollRT.offsetMax, ViewMin = view.offsetMin, ViewMax = view.offsetMax, ClipMin = clip.offsetMin, ClipMax = clip.offsetMax,
                BarMax = bar != null ? bar.offsetMax : Vector2.zero, BaseSize = gui.m_recipeListBaseSize, Top = RectIn(scrollRT, gui.m_crafting).yMax,
            };
        }

        internal bool SameAs(Layout o)
        {
            const float e = 0.01f;
            return (ScrollMax - o.ScrollMax).magnitude <= e && (ViewMin - o.ViewMin).magnitude <= e && (ViewMax - o.ViewMax).magnitude <= e
                   && (ClipMin - o.ClipMin).magnitude <= e && (ClipMax - o.ClipMax).magnitude <= e && (BarMax - o.BarMax).magnitude <= e
                   && Mathf.Abs(BaseSize - o.BaseSize) <= e && Mathf.Abs(Top - o.Top) <= e;
        }

        public override string ToString() => $"list top {F(Top)}, scroll {ScrollMax}, view {ViewMin}/{ViewMax}, clip {ClipMin}/{ClipMax}, bar {BarMax}, base {F(BaseSize)}";
    }

    private static bool AnyRowObject(InventoryGui gui)
    {
        foreach (var t in gui.m_crafting.GetComponentsInChildren<Transform>(true))
        {
            if ((t.name == RowObject || t.name == MenuObject) && t.gameObject.activeSelf)
            {
                return true;
            }
        }
        return false;
    }

    private static IEnumerator RunToggle()
    {
        const string T = ToggleName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            yield return rig.UnlockAllRecipes(c);
            rig.ForgetSorts();
            rig.ClearCraftFilter();
            var gui = rig.Gui;
            var p = rig.P;
            var forge = new Box<CraftingStation>();
            yield return Open(rig, "forge", forge);
            if (!c.Check(forge.Value != null && SearchUi.Field != null && SortButton() != null && ScrollOf(gui) != null, "setup: forge, search row or list missing"))
            {
                yield break;
            }
            var all = Rows(gui);
            var ok = new Box<bool>();
            Func<InventoryGui.RecipeDataPair, bool> isWeapon = r => InOption(r, RecipeCategory.Weapons);
            yield return Choose(rig, RecipeCategory.Weapons, ok);
            c.Check(ok.Value && p.m_customData.TryGetValue(SortMemory.Key, out var check) && check.Contains("=weapons"), "setup: Weapons not saved for the forge");
            p.m_customData.TryGetValue(SortMemory.Key, out var stored);
            yield return Type("silver");
            c.Check(gui.m_availableRecipes.Count < all.Count, "setup: 'silver' not applied");
            yield return Focus(c, "forge");
            var on = Layout.Take(gui);
            // Strip the row takes from the list: one row + gap, in panel units and in list content units.
            var strip = (gui.m_recipeListSpace > 1f ? gui.m_recipeListSpace : 30f) + RowGap;
            var shift = ConvertY(ScrollOf(gui).transform.parent, gui.m_crafting, strip);
            var contentShift = ConvertY(ScrollOf(gui).transform.parent, gui.m_recipeListRoot, strip);

            // Off while typing, inventory open (what OnDeactivated does; the framework then removes the patches).
            rig.ModTouched = true;
            CraftSearch.Deactivate();
            c.Check(SearchUi.Field == null && !AnyRowObject(gui) && !SearchUi.MenuOpen, "off: the search row is still there");
            c.Check(SameOrder(all, Rows(gui)), $"off: the list is not the full vanilla list at once ({gui.m_availableRecipes.Count} of {all.Count} rows)");
            var off = Layout.Take(gui);
            c.Check(Mathf.Abs(off.Top - on.Top - shift) <= 0.6f && Mathf.Abs(off.BaseSize - on.BaseSize - contentShift) <= 0.6f,
                $"off: the list did not get its full height back (top {F(on.Top)} -> {F(off.Top)}, expected +{F(shift)}; base size {F(on.BaseSize)} -> {F(off.BaseSize)})");
            var scrollOff = ScrollOf(gui);
            c.Check(RectIn(ClipOf(gui, scrollOff), gui.m_crafting).yMax >= off.Top - 2f, "off: the shown part of the list did not grow back to the list's top");
            CheckPlacement(c, gui, "off");
            yield return Frames(3);
            c.Check(Shown(gui), "off: the inventory closed");
            c.Check(Chat.instance == null || !Chat.instance.HasFocus(), "off: game keys still held back");
            c.Check(EventSystem.current == null || EventSystem.current.sendNavigationEvents, "off: UI navigation events stay off");
            c.Check(SameOrder(all, Rows(gui)) && !AnyRowObject(gui), "off: row or filter came back on the next frames");
            yield return Tap(rig, "Use");
            c.Check(!Shown(gui), "off: E does not close the inventory");
            yield return Frames(3);

            // Still off: station opened again = vanilla panel, saved sorts untouched (the character without the mod).
            yield return Reopen(rig, forge.Value);
            c.Check(SearchUi.Field == null && !AnyRowObject(gui) && SameOrder(all, Rows(gui)) && Layout.Take(gui).SameAs(off),
                $"off, forge reopened: not the vanilla panel (row {AnyRowObject(gui)}, vanilla list {SameOrder(all, Rows(gui))})");
            p.m_customData.TryGetValue(SortMemory.Key, out var whileOff);
            c.Check(whileOff == stored, $"off: saved sorts changed ('{stored}' -> '{whileOff}')");
            yield return Tap(rig, "JoyButtonB");
            c.Check(!Shown(gui), "off: the close key does not close the inventory");
            yield return Frames(3);
            yield return Reopen(rig, forge.Value);

            // On with the station open: row back with the remembered sort, no reopening.
            CraftSearch.Activate();
            yield return null;
            c.Check(SearchUi.Field != null && RowOf() != null && RowOf().gameObject.activeInHierarchy, "on: no search row");
            c.Check(SortLabel() == "Sort: Weapons" && CraftSearch.Option == RecipeCategory.Weapons, $"on: button reads '{SortLabel()}'");
            c.Check(SameOrder(Partition(all, isWeapon), Rows(gui)), "on: the remembered sort is not applied to the open list");
            c.Check(SearchUi.Field != null && SearchUi.Field.text == "" && CraftSearch.Term == "", "on: the search field is not empty");
            var on2 = Layout.Take(gui);
            c.Check(on2.SameAs(on), $"on: layout differs from before ({on2} / {on})");

            // Off with the menu open: menu gone with the row. Layout back exactly (no drift).
            SendClick(SortButton().gameObject, PointerEventData.InputButton.Left);
            c.Check(SearchUi.MenuOpen && MenuOf(gui) != null, "setup: menu not open");
            CraftSearch.Deactivate();
            c.Check(!SearchUi.MenuOpen && MenuOf(gui) == null && !AnyRowObject(gui), "off with the menu open: menu or row still there");
            c.Check(SameOrder(all, Rows(gui)), "off with the menu open: not the vanilla list");
            var off2 = Layout.Take(gui);
            c.Check(off2.SameAs(off), $"off again: layout is not the same as the first time ({off2} / {off})");
            yield return Close(rig);

            // Off with the inventory closed (MC Mods panel), then a station: vanilla. On, reopen: row and sort.
            yield return Reopen(rig, forge.Value);
            c.Check(SearchUi.Field == null && !AnyRowObject(gui) && SameOrder(all, Rows(gui)) && Layout.Take(gui).SameAs(off), "off before opening: not the vanilla panel");
            yield return Close(rig);
            CraftSearch.Activate();
            yield return Reopen(rig, forge.Value);
            c.Check(SearchUi.Field != null && SortLabel() == "Sort: Weapons" && SameOrder(Partition(all, isWeapon), Rows(gui)) && Layout.Take(gui).SameAs(on),
                $"on before opening: row {SearchUi.Field != null}, button '{SortLabel()}'");
            p.m_customData.TryGetValue(SortMemory.Key, out var after);
            c.Check(after == stored, $"saved sorts changed by the toggles ('{stored}' -> '{after}')");
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.repair (C01) ----------

    // How many of these items have a row in the list, and how many of those rows show a durability bar.
    private static void DurabilityBars(List<InventoryGui.RecipeDataPair> rows, List<ItemDrop.ItemData> items, out int found, out int bars)
    {
        found = 0;
        bars = 0;
        foreach (var item in items)
        {
            var i = IndexOfItem(rows, item);
            if (i < 0 || rows[i].InterfaceElement == null)
            {
                continue;
            }
            found++;
            var bar = rows[i].InterfaceElement.transform.Find("Durability");
            if (bar != null && bar.gameObject.activeSelf)
            {
                bars++;
            }
        }
    }

    private static IEnumerator RunRepair()
    {
        const string T = RepairName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            var other = FeatureRegistry.Find(RepairGuid);
            if (!c.Check(BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(RepairGuid) && other != null && other.Value.IsActive,
                    "One Click Repair All (MC) is not loaded or not active: nothing checked"))
            {
                yield break;
            }
            yield return rig.UnlockAllRecipes(c);
            rig.ForgetSorts();
            rig.ClearCraftFilter();
            var gui = rig.Gui;
            var forge = new Box<CraftingStation>();
            yield return Open(rig, "forge", forge);
            if (!c.Check(forge.Value != null && SearchUi.Field != null && SortButton() != null, "setup: forge, search field or Sort button missing"))
            {
                yield break;
            }
            var all = Rows(gui);
            var level = forge.Value.GetLevel();

            // Three damaged weapons this forge can repair, sharing a word to search for.
            Func<InventoryGui.RecipeDataPair, bool> repairable = r => Named(r) && ItemKinds.Classify(Shared(r)) == ItemKind.Weapon && Shared(r).m_useDurability
                                                                    && Shared(r).m_canBeReparied && Shared(r).m_dlc.Length == 0 && r.Recipe.m_minStationLevel <= level
                                                                    && r.Recipe.m_craftingStation != null && r.Recipe.m_craftingStation.m_name == forge.Value.m_name;
            string term = null;
            var picks = new List<InventoryGui.RecipeDataPair>();
            foreach (var word in new[] { "silver", "bronze", "iron", "copper" })
            {
                picks = all.Where(r => repairable(r) && Norm(Loc(Shared(r).m_name)).Contains(word)).Take(3).ToList();
                if (picks.Count >= 2)
                {
                    term = word;
                    break;
                }
            }
            if (!c.Check(term != null, $"setup: no two repairable weapons with a common word at a level {level} forge"))
            {
                yield break;
            }
            var items = new List<ItemDrop.ItemData>();
            foreach (var pick in picks)
            {
                var item = rig.Give(pick.Recipe.m_item.name, 1);
                if (item != null)
                {
                    item.m_durability = 1f;
                    items.Add(item);
                }
            }
            c.Check(items.Count == picks.Count && items.Count >= 2, $"setup: {items.Count} damaged items given ({Names(picks)})");
            gui.UpdateCraftingPanel();
            yield return null;
            all = Rows(gui);
            var ok = new Box<bool>();
            Func<InventoryGui.RecipeDataPair, bool> isWeapon = r => InOption(r, RecipeCategory.Weapons);
            yield return Choose(rig, RecipeCategory.Weapons, ok);
            yield return Type(term);
            var expected = Partition(Matching(rig, all, term), isWeapon);
            c.Check(ok.Value && SameOrder(expected, Rows(gui)) && expected.Count < all.Count, $"setup: Weapons + '{term}' not applied");
            yield return Frames(2);
            c.Check(gui.m_repairButton != null && gui.m_repairButton.gameObject.activeInHierarchy && gui.m_repairButton.interactable, "setup: repair button not usable");

            // One click.
            SendClick(gui.m_repairButton.gameObject, PointerEventData.InputButton.Left);
            yield return Frames(3);
            var left = items.Count(i => i.m_durability < i.GetMaxDurability() - 0.01f);
            c.Check(left == 0, $"one click on repair: {left} of {items.Count} items still damaged");
            c.Check(SearchUi.Field.text == term && CraftSearch.Term == term && CraftSearch.Option == RecipeCategory.Weapons && SortLabel() == "Sort: Weapons",
                $"after the repair: text '{SearchUi.Field.text}', button '{SortLabel()}'");
            c.Check(SameOrder(expected, Rows(gui)), $"after the repair: the list is not the same filtered and sorted list ({Names(Rows(gui), 6)})");

            // Upgrade tab: durability bars of those rows are gone (filter and sort still on).
            gui.OnTabUpgradePressed();
            yield return Frames(2);
            var rows = Rows(gui);
            DurabilityBars(rows, items, out var found, out var bars);
            c.Check(found == items.Count && bars == 0, $"Upgrade tab: {found} of {items.Count} repaired items listed, {bars} still show a durability bar");
            c.Check(CraftSearch.Term == term && CraftSearch.Option == RecipeCategory.Weapons && Matching(rig, rows, term).Count == rows.Count
                    && SameOrder(Partition(rows, isWeapon), rows), "Upgrade tab: filter or sort lost");

            // Repair pressed ON the Upgrade tab, filter and sort on: the bars of the damaged rows are shown (control),
            // one click, the repair mod rebuilds the list: bars gone, filter and sort still on.
            foreach (var item in items)
            {
                item.m_durability = 1f;
            }
            gui.UpdateCraftingPanel();
            yield return Frames(2);
            rows = Rows(gui);
            DurabilityBars(rows, items, out found, out bars);
            var barsReady = c.Check(gui.InUpradeTab() && found == items.Count && bars == items.Count,
                $"setup, Upgrade tab: {found} of {items.Count} damaged items listed, {bars} show a durability bar");
            c.Check(gui.m_repairButton.gameObject.activeInHierarchy && gui.m_repairButton.interactable, "setup, Upgrade tab: repair button not usable");
            SendClick(gui.m_repairButton.gameObject, PointerEventData.InputButton.Left);
            yield return Frames(3);
            rows = Rows(gui);
            DurabilityBars(rows, items, out found, out bars);
            left = items.Count(i => i.m_durability < i.GetMaxDurability() - 0.01f);
            c.Check(barsReady && left == 0 && found == items.Count && bars == 0,
                $"repair clicked on the Upgrade tab: {left} of {items.Count} items still damaged, {found} listed, {bars} still show a durability bar");
            c.Check(gui.InUpradeTab() && SearchUi.Field.text == term && CraftSearch.Term == term && CraftSearch.Option == RecipeCategory.Weapons && SortLabel() == "Sort: Weapons"
                    && Matching(rig, rows, term).Count == rows.Count && SameOrder(Partition(rows, isWeapon), rows),
                $"repair clicked on the Upgrade tab: filter or sort lost (text '{SearchUi.Field.text}', button '{SortLabel()}', {Names(rows, 6)})");
            gui.OnTabCraftPressed();
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.hidden (T35) ----------

    // Right after the game closed the inventory under a focused field: nothing of ours holds the keyboard.
    private static IEnumerator CheckReleased(Checks c, Rig rig, string label)
    {
        var gui = rig.Gui;
        var field = SearchUi.Field;
        yield return Until(() => !Shown(gui), 1.5f);
        c.Check(!Shown(gui), $"{label}: the inventory is still open");
        c.Check(!SearchUi.MenuOpen && MenuOf(gui) == null, $"{label}: the sort menu is still there");
        c.Check(field != null && !field.isFocused, $"{label}: the cursor is still in the field");
        var es = EventSystem.current;
        c.Check(es != null && (field == null || es.currentSelectedGameObject != field.gameObject), $"{label}: the field is still the selected UI object");
        c.Check(es != null && es.sendNavigationEvents, $"{label}: UI navigation events stay off");
        yield return Frames(3);
        c.Check(Chat.instance == null || !Chat.instance.HasFocus(), $"{label}: 3 frames after the inventory closed the game keys are still held back");
        var held = false;
        for (var i = 0; i < 12; i++)
        {
            yield return null;
            held |= Chat.instance != null && Chat.instance.HasFocus();
        }
        c.Check(!held, $"{label}: the game keys were held back again afterwards");
    }

    private static IEnumerator RunHidden()
    {
        const string T = HiddenName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            rig.ForgetSorts();
            rig.ClearCraftFilter();
            var gui = rig.Gui;
            var p = rig.P;
            var errors = LogWatch.Instance.ErrorCount;
            var bench = new Box<CraftingStation>();
            yield return Open(rig, "piece_workbench", bench);
            if (!c.Check(bench.Value != null && SearchUi.Field != null && SortButton() != null, "setup: workbench, search field or Sort button missing"))
            {
                yield break;
            }
            var field = SearchUi.Field;
            rig.TeachRecipesOf(bench.Value);
            gui.UpdateCraftingPanel();
            yield return null;
            var ok = new Box<bool>();
            yield return Choose(rig, RecipeCategory.Name, ok);
            c.Check(ok.Value, "setup: Name not chosen");
            var all = Rows(gui);

            // Teleport while typing (KeepSearchText off): the game hides the inventory every frame.
            yield return Type("wood");
            yield return Focus(c, "before the teleport");
            yield return Until(() => p.m_teleportCooldown >= 2f, 4f);
            var started = p.TeleportTo(p.transform.position, p.transform.rotation, false);
            if (c.Check(started && p.IsTeleporting(), "setup: the game refused the teleport"))
            {
                yield return CheckReleased(c, rig, "teleport");
                c.Check(field.text == "" && CraftSearch.Term == "", $"teleport: the search was not cleared ('{field.text}')");
                yield return Until(() => !p.IsTeleporting(), 8f);
                c.Check(!p.IsTeleporting(), "setup: teleport did not end");
                yield return Frames(5);
                if (!ZInput.IsGamepadActive() && Button("Forward") != null)
                {
                    var from = p.transform.position;
                    yield return Hold(rig, "Forward", 0.4f);
                    c.Check(Flat(from, p.transform.position) > 0.2f, $"after the teleport: Forward moves the player only {F(Flat(from, p.transform.position))} m");
                }
                rig.GoHome();
                yield return Frames(3);
            }
            yield return Reopen(rig, bench.Value);
            c.Check(Shown(gui) && SortLabel() == "Sort: Name (A-Z)" && CraftSearch.Option == RecipeCategory.Name && field.text == "" && SameOrder(all, Rows(gui)),
                $"workbench reopened after the teleport: button '{SortLabel()}', text '{field.text}'");

            // Station destroyed under the open window (KeepSearchText on): closed by Player.UpdateStations.
            KeepSearchTextOverride = true;
            yield return Type("wood");
            var filtered = Rows(gui);
            c.Check(filtered.Count > 0 && filtered.Count < all.Count, "setup: 'wood' not applied");
            yield return Focus(c, "before the station is destroyed");
            rig.DestroySpawned();
            yield return CheckReleased(c, rig, "station destroyed");
            c.Check(field.text == "wood", $"station destroyed, KeepSearchText on: the field holds '{field.text}'");
            yield return Frames(5);
            var again = new Box<CraftingStation>();
            yield return Open(rig, "piece_workbench", again);
            if (c.Check(again.Value != null, "setup: second workbench missing"))
            {
                c.Check(field.text == "wood" && CraftSearch.Term == "wood" && SortLabel() == "Sort: Name (A-Z)" && SameOrder(filtered, Rows(gui)),
                    $"same kind of station opened again: text '{field.text}', button '{SortLabel()}', same filtered list {SameOrder(filtered, Rows(gui))}");
            }
            c.Check(LogWatch.Instance.ErrorCount == errors, "an error of this mod was logged: " + string.Join(" | ", LogWatch.Instance.Errors().Skip(errors).Take(2).ToArray()));
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.rightclick (T36) ----------

    // Own small test: may fail until the mod is fixed. GuiInputField.OnPointerClick turns the UI navigation events off
    // for any mouse button (keyboard and mouse play); a right-click (our "clear") never gives the field the cursor, so
    // our own "focus lost" clean-up never runs. They come back only on deselect, submit, or when the field is disabled:
    // the test tells whether closing the inventory does that.
    private static IEnumerator RunRightClick()
    {
        const string T = RightClickName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            var gui = rig.Gui;
            var bench = new Box<CraftingStation>();
            yield return Open(rig, "piece_workbench", bench);
            var es = EventSystem.current;
            if (!c.Check(bench.Value != null && SearchUi.Field != null && es != null, "setup: workbench, search field or UI event system missing"))
            {
                yield break;
            }
            var field = SearchUi.Field;
            if (!es.sendNavigationEvents)
            {
                c.Note("UI navigation events were already off before the test: turned on for it");
                es.sendNavigationEvents = true;
            }
            yield return Type("wood");
            c.Check(!field.isFocused && field.text == "wood", "setup: text typed, cursor not in the field");
            SendClick(field.gameObject, PointerEventData.InputButton.Right);
            yield return Frames(3);
            c.Check(field.text == "" && !field.isFocused, $"setup: right-click did not clear the field ('{field.text}') or gave it the cursor");
            var afterClick = es.sendNavigationEvents;
            yield return Close(rig);
            // Hide animation done (a panel turned off by it would give navigation back).
            yield return new WaitForSecondsRealtime(1f);
            c.Note($"UI navigation events: {(afterClick ? "on" : "OFF")} right after the right-click, {(es.sendNavigationEvents ? "on" : "OFF")} 1 s after the inventory closed; "
                   + $"gamepad active {ZInput.IsGamepadActive()} (the game only turns them off in keyboard and mouse play)");
            c.Check(es.sendNavigationEvents,
                "after a right-click on the search field (cursor not in it) and closing the inventory, the UI navigation events are off: D-pad and arrow keys no longer move through menu buttons (pause menu, settings)");
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.keys (real key events) ----------

    private static void SendKeys(params UnityEngine.InputSystem.Key[] down)
    {
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null)
        {
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(down));
        }
    }

    // Key down for a few frames, then up: a state event on the real keyboard device, read by the game like a press
    // (ZInput.GetKeyDown, bound buttons). No character is typed (text comes from another event queue).
    private static IEnumerator PressKey(UnityEngine.InputSystem.Key key)
    {
        SendKeys(key);
        yield return Frames(3);
        SendKeys();
        yield return Frames(3);
    }

    private static IEnumerator RunKeys()
    {
        const string T = KeysName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            var gui = rig.Gui;
            const KeyCode bound = KeyCode.ScrollLock;
            if (!c.Check(UnityEngine.InputSystem.Keyboard.current != null && Chat.instance != null, "no keyboard device or no chat: real key presses cannot be sent"))
            {
                yield break;
            }
            rig.Undo("keys up", () => SendKeys());
            var hits = new Box<int>();
            rig.Undo("probe command", () => Terminal.commands.Remove(ProbeCommand));
            new Terminal.ConsoleCommand(ProbeCommand, "self test probe", (Terminal.ConsoleEvent)(args => hits.Value++));
            var hadBind = Terminal.m_binds.TryGetValue(bound, out var oldBind);
            rig.Undo("bind", () =>
            {
                if (hadBind)
                {
                    Terminal.m_binds[bound] = oldBind;
                }
                else
                {
                    Terminal.m_binds.Remove(bound);
                }
            });
            Terminal.m_binds[bound] = new List<string> { ProbeCommand };
            rig.FocusKey(new KeyboardShortcut(KeyCode.F));

            var bench = new Box<CraftingStation>();
            yield return Open(rig, "piece_workbench", bench);
            if (!c.Check(bench.Value != null && SearchUi.Field != null && SortButton() != null, "setup: workbench, search field or Sort button missing"))
            {
                yield break;
            }
            var field = SearchUi.Field;

            // Control: the bound key runs its command when the cursor is not in the field. No = the key events of this
            // test do not reach the game (window without keyboard focus): nothing below would mean anything.
            yield return PressKey(UnityEngine.InputSystem.Key.ScrollLock);
            if (!c.Check(hits.Value >= 1, "a key press sent to the game's keyboard device was not seen by the game: the game window must have the keyboard focus while this test runs (not a fault of the mod; nothing was checked)"))
            {
                yield break;
            }

            // T08: bound key while typing: nothing runs.
            var before = hits.Value;
            yield return Focus(c, "workbench");
            yield return PressKey(UnityEngine.InputSystem.Key.ScrollLock);
            c.Check(hits.Value == before, $"typing: the key bound in the console ran its command ({hits.Value - before} time(s))");
            c.Check(field.isFocused && Shown(gui), "typing: the bound key took the cursor or closed the inventory");
            yield return Unfocus();
            yield return PressKey(UnityEngine.InputSystem.Key.ScrollLock);
            c.Check(hits.Value > before, "cursor out of the field: the bound key no longer runs its command");

            // T10: the real focus key puts the cursor in the field.
            yield return PressKey(UnityEngine.InputSystem.Key.F);
            c.Check(field.isFocused && Shown(gui), $"F pressed: cursor in the field {field.isFocused}, inventory open {Shown(gui)}");
            yield return Unfocus();

            // T12: Esc with the menu open closes the menu only; the next Esc closes the inventory.
            if (!Shown(gui))
            {
                yield return Reopen(rig, bench.Value);
            }
            SendClick(SortButton().gameObject, PointerEventData.InputButton.Left);
            yield return Frames(2);
            c.Check(SearchUi.MenuOpen, "setup: menu not open before Esc");
            yield return PressKey(UnityEngine.InputSystem.Key.Escape);
            c.Check(!SearchUi.MenuOpen && MenuOf(gui) == null, "Esc with the menu open: the menu is still open");
            c.Check(Shown(gui), "Esc with the menu open: the inventory closed too");
            c.Check(!Menu.IsVisible(), "Esc with the menu open: the pause menu opened");
            if (Shown(gui))
            {
                yield return PressKey(UnityEngine.InputSystem.Key.Escape);
                c.Check(!Shown(gui), "second Esc: the inventory did not close");
            }
            yield return Frames(3);
            if (Menu.IsVisible() && Menu.instance != null)
            {
                c.Note("the pause menu opened on the last Esc: closed it");
                Menu.instance.Hide();
            }
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.log (T31) ----------

    // Own small test, last: looks back on the whole session (every test above opened every kind of station).
    private static IEnumerator RunLog()
    {
        const string T = LogName;
        yield return null;
        var log = LogWatch.Instance;
        var errors = log.Errors();
        var warnings = log.Warnings();
        var layout = warnings.Where(w => w.IndexOf("Crafting list layout not recognised", StringComparison.Ordinal) >= 0).ToList();
        SelfTest.Note(T, $"since the mod started: {log.ErrorCount} error line(s) and {log.WarningCount} warning line(s) of or about this mod"
                         + (warnings.Count > 0 ? "; warnings: " + string.Join(" | ", warnings.Take(4).Select(FirstLine).ToArray()) : ""));
        if (errors.Count == 0 && log.ErrorCount == 0 && layout.Count == 0)
        {
            SelfTest.Pass(T, "no error or exception naming this mod in the log, no 'Crafting list layout not recognised' warning");
        }
        else
        {
            SelfTest.Fail(T, $"{log.ErrorCount} error line(s), {layout.Count} layout warning(s): " + string.Join(" | ", errors.Concat(layout).Take(4).Select(FirstLine).ToArray()));
        }
    }

    private static string FirstLine(string text)
    {
        var cut = text.IndexOf('\n');
        var line = cut > 0 ? text.Substring(0, cut).TrimEnd() : text;
        return line.Length > 300 ? line.Substring(0, 300) : line;
    }

    // ---------- csearch.mp.station (M01, M02) ----------

    // ZDO as the server has it: sent, and nothing newer waiting here.
    private static bool SentToServer(ZDO zdo)
    {
        var man = ZDOMan.instance;
        if (man == null || zdo == null)
        {
            return false;
        }
        foreach (var peer in man.m_peers)
        {
            if (peer.m_zdos.TryGetValue(zdo.m_uid, out var info) && info.m_dataRevision >= zdo.DataRevision)
            {
                return true;
            }
        }
        return false;
    }

    // Client joined to a dedicated server (which never loads this client-only mod): the row, search, sort and a
    // craft work as in single player; the station and the dropped item reach the server as plain game objects.
    private static IEnumerator RunMpStation()
    {
        const string T = MpStationName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            var gui = rig.Gui;
            var net = ZNet.instance;
            if (!c.Check(net != null && !net.IsServer() && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected, "not a client connected to a server"))
            {
                yield break;
            }
            c.Note($"scenario '{SelfTest.Scenario}': client on a dedicated server; this mod is client-only and is not loaded by the server");
            rig.ForgetSorts();
            rig.ClearCraftFilter();
            var errors = LogWatch.Instance.ErrorCount;
            var bench = new Box<CraftingStation>();
            yield return Open(rig, "piece_workbench", bench, 0.8f);
            if (!c.Check(bench.Value != null && SearchUi.Field != null && SortButton() != null, "setup: workbench, search field or Sort button missing"))
            {
                yield break;
            }
            var field = SearchUi.Field;
            var taught = rig.TeachRecipesOf(bench.Value);
            gui.UpdateCraftingPanel();
            yield return null;
            var row = RowOf();
            var scroll = ScrollOf(gui);
            c.Check(row != null && row.gameObject.activeInHierarchy && scroll != null && SortLabel() == "Sort: Default"
                    && RectIn(row, gui.m_crafting).yMin >= RectIn((RectTransform)scroll.transform, gui.m_crafting).yMax - 0.5f, "the search row is not shown above the list");
            var benchView = bench.Value.GetComponentInParent<ZNetView>();
            var benchZdo = benchView != null ? benchView.GetZDO() : null;
            yield return Until(() => SentToServer(benchZdo), 10f);
            c.Check(SentToServer(benchZdo), "the workbench was not sent to the server within 10 s");

            var arrowAt = IndexOf(gui, "ArrowWood");
            if (!c.Check(arrowAt >= 0, $"setup: the workbench does not list Wood Arrows ({gui.m_availableRecipes.Count} rows, {taught} recipes taught)"))
            {
                yield break;
            }
            var recipe = gui.m_availableRecipes[arrowAt].Recipe;
            var arrowName = recipe.m_item.m_itemData.m_shared.m_name;
            foreach (var req in recipe.m_resources)
            {
                if (req != null && req.m_resItem != null && !req.m_upgraderResource && req.GetAmount(1) > 0)
                {
                    rig.Give(req.m_resItem.name, req.GetAmount(1) * 2);
                }
            }
            gui.UpdateCraftingPanel();
            yield return null;
            var all = Rows(gui);
            c.Check(all.Count >= 10, $"setup: workbench lists {all.Count} recipes");

            // Search.
            yield return Type("wood");
            var wood = CheckFilter(c, rig, all, "wood", "server, 'wood'");
            c.Check(wood.Count > 0 && wood.Count < all.Count && IndexOf(gui, "ArrowWood") >= 0, $"server, 'wood': {wood.Count} of {all.Count} rows");
            yield return Type("");

            // Sort: a category, then Name.
            var ok = new Box<bool>();
            var counts = new int[RecipeCategory.Count];
            Recount(all, counts, new Sprite[RecipeCategory.Count]);
            var pick = Enumerable.Range(RecipeCategory.Weapons, RecipeCategory.Count - RecipeCategory.Weapons).FirstOrDefault(o => counts[o] > 0 && counts[o] < all.Count);
            if (c.Check(pick >= RecipeCategory.Weapons, "setup: no category to choose"))
            {
                yield return Choose(rig, pick, ok);
                c.Check(ok.Value && SameOrder(Partition(all, r => InOption(r, pick)), Rows(gui)) && SortLabel() == "Sort: " + RecipeCategory.Label(pick),
                    $"server, {RecipeCategory.Id(pick)}: its rows are not first with the rest in vanilla order");
            }
            yield return Choose(rig, RecipeCategory.Name, ok);
            var byName = Rows(gui);
            var comparer = StringComparer.CurrentCultureIgnoreCase;
            var unordered = 0;
            for (var i = 1; i < byName.Count; i++)
            {
                if (Named(byName[i - 1]) && Named(byName[i]) && comparer.Compare(Loc(Shared(byName[i - 1]).m_name), Loc(Shared(byName[i]).m_name)) > 0)
                {
                    unordered++;
                }
            }
            c.Check(ok.Value && byName.Count == all.Count && unordered == 0, $"server, Name: {unordered} pair(s) not in A-Z order");
            rig.P.m_customData.TryGetValue(SortMemory.Key, out var stored);
            c.Check(stored == SortMemory.StationKey(bench.Value) + "=name", $"server: saved sort in the character is '{stored}'");

            // Craft with a filter on, on the server.
            yield return Type("arrow");
            var filtered = Rows(gui);
            arrowAt = IndexOf(gui, "ArrowWood");
            gui.SetRecipe(arrowAt, false);
            yield return Frames(2);
            c.Check(gui.m_craftButton.interactable, "setup: Wood Arrows cannot be crafted");
            var before = rig.Inv.CountItems(arrowName);
            rig.NoCraftBonus();
            yield return CraftNow(c, gui, "server craft");
            c.Check(rig.Inv.CountItems(arrowName) == before + recipe.m_amount, $"server craft: {rig.Inv.CountItems(arrowName) - before} arrows made, {recipe.m_amount} expected");
            c.Check(field.text == "arrow" && SameOrder(filtered, Rows(gui)) && SortLabel() == "Sort: Name (A-Z)", "server craft: filter or sort lost");

            // Hand-off: what another player gets is a plain item, and the server has it.
            var dropped = new Box<ItemDrop>();
            CheckPlainItem(c, rig, rig.Inv.GetAllItems().FirstOrDefault(i => i.m_shared.m_name == arrowName), dropped, "server, crafted arrows");
            if (dropped.Value != null)
            {
                var view = dropped.Value.GetComponent<ZNetView>();
                var zdo = view != null ? view.GetZDO() : null;
                yield return Until(() => SentToServer(zdo), 10f);
                c.Check(SentToServer(zdo), "the dropped arrow was not sent to the server within 10 s");
            }
            c.Check(net != null && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected, "the connection to the server was lost");
            c.Check(LogWatch.Instance.ErrorCount == errors, "an error of this mod was logged: " + string.Join(" | ", LogWatch.Instance.Errors().Skip(errors).Take(2).ToArray()));
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // ---------- csearch.mp.remember / keeptext / focuskey (real settings) ----------

    // The three tests below change the REAL settings (ConfigEntry.Value, same event a config file edit or
    // ConfigurationManager raises). Only in a multiplayer run: there the config files are throwaway copies. They need
    // nothing from the server.
    private static bool ThrowawayConfig(Checks c)
    {
        return c.Check(SelfTest.IsMultiplayerRun, "not a multiplayer run: the config file is the player's own, nothing changed");
    }

    // T18: RememberSort flipped for real.
    private static IEnumerator RunMpRemember()
    {
        const string T = MpRememberName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            if (!ThrowawayConfig(c))
            {
                yield break;
            }
            var gui = rig.Gui;
            var entry = Plugin.RememberSort;
            var old = entry.Value;
            rig.Undo("RememberSort setting", () => entry.Value = old);
            ClearOverrides(); // the settings themselves decide here
            entry.Value = true;
            rig.ForgetSorts();
            rig.ClearCraftFilter();
            var bench = new Box<CraftingStation>();
            yield return Open(rig, "piece_workbench", bench);
            if (!c.Check(bench.Value != null && SearchUi.Field != null && SortButton() != null, "setup: workbench, search field or Sort button missing"))
            {
                yield break;
            }
            rig.TeachRecipesOf(bench.Value);
            gui.UpdateCraftingPanel();
            yield return null;
            var all = Rows(gui);
            var ok = new Box<bool>();
            yield return Choose(rig, RecipeCategory.Name, ok);
            var byName = Rows(gui);
            c.Check(ok.Value && all.Count >= 10 && SortLabel() == "Sort: Name (A-Z)" && !SameOrder(all, byName), $"setup: Name not applied ({all.Count} rows)");

            // Off with the station open: vanilla order at once.
            entry.Value = false;
            c.Check(CraftSearch.Option == RecipeCategory.Default && SortLabel() == "Sort: Default" && SameOrder(all, Rows(gui)),
                $"RememberSort = false with the station open: button '{SortLabel()}', vanilla order {SameOrder(all, Rows(gui))}");
            // A pick lasts until the inventory closes.
            yield return Choose(rig, RecipeCategory.Name, ok);
            c.Check(SameOrder(byName, Rows(gui)), "RememberSort = false: a pick does not apply");
            yield return Close(rig);
            yield return Reopen(rig, bench.Value);
            c.Check(SortLabel() == "Sort: Default" && SameOrder(all, Rows(gui)), $"RememberSort = false, reopened after a pick: button '{SortLabel()}'");

            // On again with the station open: the saved sort is back at once.
            entry.Value = true;
            c.Check(CraftSearch.Option == RecipeCategory.Name && SortLabel() == "Sort: Name (A-Z)" && SameOrder(byName, Rows(gui)),
                $"RememberSort = true again: button '{SortLabel()}', saved sort applied {SameOrder(byName, Rows(gui))}");
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // T19: KeepSearchText flipped for real.
    private static IEnumerator RunMpKeepText()
    {
        const string T = MpKeepTextName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            if (!ThrowawayConfig(c))
            {
                yield break;
            }
            var gui = rig.Gui;
            var entry = Plugin.KeepSearchText;
            var old = entry.Value;
            rig.Undo("KeepSearchText setting", () => entry.Value = old);
            ClearOverrides();
            entry.Value = false;
            rig.ForgetSorts();
            rig.ClearCraftFilter();
            var bench = new Box<CraftingStation>();
            yield return Open(rig, "piece_workbench", bench);
            if (!c.Check(bench.Value != null && SearchUi.Field != null, "setup: workbench or search field missing"))
            {
                yield break;
            }
            var field = SearchUi.Field;
            rig.TeachRecipesOf(bench.Value);
            gui.UpdateCraftingPanel();
            yield return null;
            var all = Rows(gui);
            yield return Type("wood");
            var filtered = Rows(gui);
            c.Check(filtered.Count > 0 && filtered.Count < all.Count, $"setup: 'wood' leaves {filtered.Count} of {all.Count} rows");
            yield return Close(rig);
            yield return Reopen(rig, bench.Value);
            c.Check(field.text == "" && SameOrder(all, Rows(gui)), $"KeepSearchText = false: reopened with text '{field.text}', {gui.m_availableRecipes.Count} of {all.Count} rows");

            entry.Value = true;
            yield return Type("wood");
            yield return Close(rig);
            yield return Reopen(rig, bench.Value);
            c.Check(field.text == "wood" && CraftSearch.Term == "wood" && SameOrder(filtered, Rows(gui)),
                $"KeepSearchText = true: reopened with text '{field.text}', {gui.m_availableRecipes.Count} rows ({filtered.Count} expected)");
            yield return Close(rig);
            yield return Reopen(rig, null);
            c.Check(field.text == "" && CraftSearch.Term == "", $"KeepSearchText = true, plain inventory after the workbench: field holds '{field.text}'");
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }

    // T34, T10: FocusSearchKey set for real: unreadable key, empty, back to F.
    private static IEnumerator RunMpFocusKey()
    {
        const string T = MpFocusKeyName;
        if (!Ready(T))
        {
            yield break;
        }
        var c = new Checks(T);
        Rig rig = null;
        try
        {
            rig = new Rig(T);
            if (!ThrowawayConfig(c))
            {
                yield break;
            }
            var gui = rig.Gui;
            var log = LogWatch.Instance;
            const string warning = "the game cannot read";
            var entry = Plugin.FocusSearchKey;
            var old = entry.Value;
            rig.Undo("FocusSearchKey setting", () => entry.Value = old);
            entry.Value = new KeyboardShortcut(KeyCode.F);
            var errors = log.ErrorCount;
            var mark = log.KeptWarnings;

            entry.Value = new KeyboardShortcut(KeyCode.F13);
            var lines = log.Warnings().Skip(mark).Where(w => w.IndexOf(warning, StringComparison.Ordinal) >= 0).ToList();
            c.Check(lines.Count == 1 && lines[0].Contains("F13") && lines[0].Contains("search field is off"),
                $"FocusSearchKey = F13: {lines.Count} warning(s): {string.Join(" | ", lines.ToArray())}");
            c.Check(CraftSearch.DebugFocusKey == "None", $"FocusSearchKey = F13: the key is still '{CraftSearch.DebugFocusKey}'");
            yield return Reopen(rig, null);
            if (!c.Check(SearchUi.Field != null && Shown(gui), "setup: plain inventory or search field missing"))
            {
                yield break;
            }
            var field = SearchUi.Field;
            yield return PressFocusKey();
            yield return Frames(10);
            c.Check(!field.isFocused, "FocusSearchKey = F13: a key press still puts the cursor in the field");
            c.Check(log.WarningsWith(warning, mark) == 1 && log.ErrorCount == errors, $"FocusSearchKey = F13: {log.WarningsWith(warning, mark)} warning(s), {log.ErrorCount - errors} error(s) after some frames");

            // Empty: off, without a warning.
            mark = log.KeptWarnings;
            entry.Value = KeyboardShortcut.Empty;
            c.Check(CraftSearch.DebugFocusKey == "None" && log.WarningsWith(warning, mark) == 0, $"FocusSearchKey empty: key '{CraftSearch.DebugFocusKey}', {log.WarningsWith(warning, mark)} warning(s)");
            yield return PressFocusKey();
            c.Check(!field.isFocused, "FocusSearchKey empty: a key press still puts the cursor in the field");

            // Back to F: works again, no warning.
            entry.Value = new KeyboardShortcut(KeyCode.F);
            c.Check(CraftSearch.DebugFocusKey == "F" && log.WarningsWith(warning, mark) == 0, $"FocusSearchKey = F again: key '{CraftSearch.DebugFocusKey}', {log.WarningsWith(warning, mark)} warning(s)");
            yield return PressFocusKey();
            c.Check(field.isFocused, "FocusSearchKey = F again: the key does not put the cursor in the field");
            c.Check(log.ErrorCount == errors, "an error was logged: " + string.Join(" | ", log.Errors().Skip(errors).Take(2).ToArray()));
            yield return Unfocus();
            yield return Close(rig);
            c.Complete();
        }
        finally
        {
            c.Report();
            if (rig != null)
            {
                rig.Done();
            }
        }
    }
#endif
}
