#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using MC.Shared;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace MC.Crafting.RepairOneClickAllMod;

// Debug build only (whole file gone in Release). In-world self tests, run by world probe (tools/Test-InWorld.ps1
// -Mod Repair.OneClickAll) and by the multiplayer probes (tools/Test-Multiplayer.ps1):
//   repair.bench           T01  5 worn workbench items (one put on): one click fix all, station effect once, one
//                               summary "Repaired a, b, c +2", button grey and no glow after
//   repair.stations        T02  workbench click leave bronze gear alone, forge click fix all bronze gear
//   repair.level           T03  item whose recipe need station level 2 stay worn at level 1 station, others fixed
//   repair.single          T04  one repairable item: same result as the vanilla press itself (text, effect, count)
//   repair.none            T05  nothing repairable: button shown, grey, no glow; click and direct call change nothing
//   repair.nocost          T06  nocost, no station: button show in plain inventory, one click fix all, no repair effect
//   repair.nocost-station  T07  nocost at workbench fix forge gear too; nocost off = no button in plain inventory
//   repair.skill           T08  Crafting skill after one click = after same number of vanilla presses
//   repair.upgrade-tab     T09  Upgrade tab: durability bar of item fixed by an extra press gone in the same frame
//   repair.pad             T10  what a pad press call (Button.OnSubmit, UIGamePad.Update with the press forced)
//   repair.toggle          T11  feature off through framework = one item per click, on again = all, no restart
//   repair.crossbow        T13  loaded crossbow fixed by an extra press still loaded when held again
//   repair.cost            C02  fake cost mod say no after 3 repairs (two styles): stop at once, reason top-left
//   repair.blocked         C03  fake mod block extra presses with no word: stop, no top-left, vanilla message stay
//   repair.handoff         M02  fixed items carry no data of me; ground drop and chest save them as plain full items
//   repair.clean           T14  no error line from me (or naming MC.Crafting) since the mod went on. Run last.
//   repair.mp.bulk / repair.mp.vanilla-server  M01  client on dedicated server: one click fix all, makes as many
//                               network objects and RPCs as a one item vanilla click, server run no copy of me
//   repair.mp.handoff      M02  same as repair.handoff on a dedicated server + item data really sent to the server
// Me never write config: nocost = Player.SetNoPlacementCost, the call the console command makes (flag not saved, rig
// put it back), feature off = Plugin.TestBlocker, other mods = my own Harmony patches under <guid>.selftest. Careful: no method name here may contain the name of the vanilla repair
// method or of the "have repairable items" method (cost mods read stack traces, see BulkRepair).
// Rig put back: items given, durability of other worn items, equipment, skill, known items/recipes/stations, nocost,
// crafting tab, window, stations and chest it spawn.
internal static class SelfTests
{
    private const string BenchName = "repair.bench";
    private const string StationsName = "repair.stations";
    private const string LevelName = "repair.level";
    private const string SingleName = "repair.single";
    private const string NoneName = "repair.none";
    private const string NoCostName = "repair.nocost";
    private const string NoCostStationName = "repair.nocost-station";
    private const string SkillName = "repair.skill";
    private const string UpgradeTabName = "repair.upgrade-tab";
    private const string PadName = "repair.pad";
    private const string ToggleName = "repair.toggle";
    private const string CrossbowName = "repair.crossbow";
    private const string CostName = "repair.cost";
    private const string BlockedName = "repair.blocked";
    private const string HandoffName = "repair.handoff";
    private const string CleanName = "repair.clean";
    private const string MpBulkName = "repair.mp.bulk";
    private const string MpVanillaName = "repair.mp.vanilla-server";
    private const string MpHandoffName = "repair.mp.handoff";

    // Server half. Me = client mod (BepInProcess valheim.exe): dedicated server never load me, so this step is not
    // there and the server probe answer "no step". Client test read that as "server run no copy of this mod".
    private const string ServerStep = "repair.mp.server-has-mod";

    // Scenario name of tools/Test-Multiplayer.ps1: server run no MC mod at all.
    private const string VanillaServerScenario = "vanilla-server";

    private const string BenchPrefab = "piece_workbench";
    private const string ForgePrefab = "forge";
    private const string ChestPrefab = "piece_chest_wood";

    private const string CrossbowGuid = "MC.Combat.Crossbow.StaysLoaded";
    private const string CrossbowStamp = CrossbowGuid + ".Loaded";

    // Items to give, best first. A test takes the first ones whose recipe really fit (checked in the running game),
    // so a name that is wrong here, or a recipe another mod changed, cost a fallback and not a failed run.
    private static readonly string[] BenchGear = { "AxeFlint", "Club", "KnifeFlint", "ShieldWood", "Hoe", "SpearFlint", "AxeStone", "Bow", "PickaxeAntler" };
    private static readonly string[] BenchArmor = { "ArmorRagsChest", "ArmorRagsLegs", "HelmetLeather", "CapeDeerHide", "ArmorLeatherLegs" };
    private static readonly string[] ForgeGear =
    {
        "SwordBronze", "AxeBronze", "ShieldBronzeBuckler", "ArmorBronzeChest", "MaceBronze", "SpearBronze", "AtgeirBronze", "PickaxeBronze",
        "ArmorBronzeLegs", "HelmetBronze", "KnifeCopper",
    };

    internal static void Register()
    {
        // Called from OnActivated: a throw here would turn the feature off. Tests are never worth that.
        try
        {
            ErrorWatch.Install();
            RegisterAll();
        }
        catch (Exception e)
        {
            Log.Warning($"Self tests not registered: {e.Message}");
        }
    }

    private static void RegisterAll()
    {
        SelfTest.Register(BenchName, RunBench);
        SelfTest.Register(StationsName, RunStations);
        SelfTest.Register(LevelName, RunLevel);
        SelfTest.Register(SingleName, RunSingle);
        SelfTest.Register(NoneName, RunNone);
        SelfTest.Register(NoCostName, RunNoCost);
        SelfTest.Register(NoCostStationName, RunNoCostStation);
        SelfTest.Register(SkillName, RunSkill);
        SelfTest.Register(UpgradeTabName, RunUpgradeTab);
        SelfTest.Register(PadName, RunPad);
        SelfTest.Register(ToggleName, RunToggle);
        SelfTest.Register(CrossbowName, RunCrossbow);
        SelfTest.Register(CostName, RunCost);
        SelfTest.Register(BlockedName, RunBlocked);
        SelfTest.Register(HandoffName, () => RunHandoff(HandoffName, false));
        // Last: it look back at every error line since the mod went on.
        SelfTest.Register(CleanName, RunClean);
        SelfTest.RegisterMultiplayer(MpBulkName, SelfTest.Modded, () => RunMpBulk(MpBulkName));
        SelfTest.RegisterMultiplayer(MpVanillaName, VanillaServerScenario, () => RunMpBulk(MpVanillaName));
        SelfTest.RegisterMultiplayer(MpHandoffName, SelfTest.Modded, () => RunHandoff(MpHandoffName, true));
        SelfTest.RegisterServerStep(ServerStep, ServerHasMod);
    }

    // Feature off (also inside repair.toggle, while that test still run): names leave the list, hooks stay as the
    // running test set them. Error watch stay on: it count the whole session.
    internal static void Unregister()
    {
        SelfTest.Unregister(BenchName);
        SelfTest.Unregister(StationsName);
        SelfTest.Unregister(LevelName);
        SelfTest.Unregister(SingleName);
        SelfTest.Unregister(NoneName);
        SelfTest.Unregister(NoCostName);
        SelfTest.Unregister(NoCostStationName);
        SelfTest.Unregister(SkillName);
        SelfTest.Unregister(UpgradeTabName);
        SelfTest.Unregister(PadName);
        SelfTest.Unregister(ToggleName);
        SelfTest.Unregister(CrossbowName);
        SelfTest.Unregister(CostName);
        SelfTest.Unregister(BlockedName);
        SelfTest.Unregister(HandoffName);
        SelfTest.Unregister(CleanName);
        SelfTest.UnregisterMultiplayer(MpBulkName);
        SelfTest.UnregisterMultiplayer(MpVanillaName);
        SelfTest.UnregisterMultiplayer(MpHandoffName);
        SelfTest.UnregisterServerStep(ServerStep);
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

    // Me listen to the BepInEx log (every source, "Unity Log" too) from the first time the feature go on. Me count
    // error lines that come from my logger or name me or MC.Crafting (what TESTING item T14 look for by hand).
    // "[selftest] FAIL" lines are test verdicts, not errors: skipped. Log events come from any thread: lock.
    private sealed class ErrorWatch : ILogListener
    {
        private static readonly object Gate = new object();
        private static ErrorWatch _installed;
        private static int _count;
        private static string _first;
        private static float _since;

        internal static void Install()
        {
            if (_installed != null)
            {
                return;
            }
            _installed = new ErrorWatch();
            _since = Time.realtimeSinceStartup;
            BepInEx.Logging.Logger.Listeners.Add(_installed);
        }

        internal static bool Installed => _installed != null;

        internal static float Since => _since;

        internal static int Count(out string first)
        {
            lock (Gate)
            {
                first = _first;
                return _count;
            }
        }

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            try
            {
                if (eventArgs == null || (eventArgs.Level & (LogLevel.Error | LogLevel.Fatal)) == 0)
                {
                    return;
                }
                var text = eventArgs.Data as string ?? eventArgs.Data?.ToString() ?? "";
                if (text.IndexOf(SelfTest.Prefix, StringComparison.Ordinal) >= 0)
                {
                    return;
                }
                var source = eventArgs.Source?.SourceName ?? "";
                if (source != ModInfo.Name
                    && text.IndexOf(ModInfo.Name, StringComparison.OrdinalIgnoreCase) < 0
                    && text.IndexOf("MC.Crafting", StringComparison.Ordinal) < 0)
                {
                    return;
                }
                lock (Gate)
                {
                    _count++;
                    if (_first == null)
                    {
                        var cut = text.IndexOf('\n');
                        _first = source + ": " + (cut > 0 ? text.Substring(0, cut).TrimEnd() : text);
                    }
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
    }

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static bool Full(ItemDrop.ItemData item) => item.m_durability >= item.GetMaxDurability();

    // Full after a trip through a save: the game writes durability with 2 decimals.
    private static bool NearFull(ItemDrop.ItemData item) => Mathf.Abs(item.m_durability - item.GetMaxDurability()) < 0.011f;

    private static string PrefabName(ItemDrop.ItemData item) => item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared.m_name;

    private static string Names(IEnumerable<ItemDrop.ItemData> items) =>
        string.Join(", ", items.Select(i => Localization.instance.Localize(i.m_shared.m_name)).ToArray());

    private static string Durabilities(IEnumerable<ItemDrop.ItemData> items) =>
        string.Join(", ", items.Select(i => $"{Localization.instance.Localize(i.m_shared.m_name)} {F(i.m_durability)}/{F(i.GetMaxDurability())}").ToArray());

    // What the screen must say after a click that fixed these items, in this order. Built another way than the mod
    // do: names translated one by one, then put in the vanilla "$msg_repaired" text. One item = the vanilla message.
    private static string RepairedText(IList<ItemDrop.ItemData> items)
    {
        var loc = Localization.instance;
        var words = string.Join(", ", items.Take(3).Select(i => loc.Localize(i.m_shared.m_name)).ToArray());
        if (items.Count > 3)
        {
            words += " +" + (items.Count - 3);
        }
        return loc.Localize("$msg_repaired").Replace("$1", words);
    }

    // Why vanilla would not repair this item at this station at that level, read from the recipe. Null = it would.
    private static string RecipeProblem(ItemDrop.ItemData item, CraftingStation station, int stationLevel)
    {
        var shared = item.m_shared;
        if (!shared.m_useDurability)
        {
            return "does not use durability";
        }
        if (!shared.m_canBeReparied)
        {
            return "cannot be repaired at all";
        }
        var recipe = ObjectDB.instance.GetRecipe(item);
        if (recipe == null)
        {
            return "has no recipe";
        }
        var craft = recipe.m_craftingStation != null ? recipe.m_craftingStation.m_name : null;
        var repair = recipe.m_repairStation != null ? recipe.m_repairStation.m_name : null;
        if (craft != station.m_name && repair != station.m_name)
        {
            return $"belongs to station '{craft ?? repair ?? "none"}'";
        }
        if (recipe.m_minStationLevel > stationLevel)
        {
            return $"needs station level {recipe.m_minStationLevel}";
        }
        return null;
    }

    // Something vanilla can repair somewhere (uses durability, repairable).
    private static bool Repairable(ItemDrop.ItemData item) => item.m_shared.m_useDurability && item.m_shared.m_canBeReparied;

    // Repairable gear whose recipe name another station than this one (vanilla leave it alone here, whatever the level).
    private static bool OtherStationGear(ItemDrop.ItemData item, CraftingStation station)
    {
        if (!Repairable(item))
        {
            return false;
        }
        var recipe = ObjectDB.instance.GetRecipe(item);
        if (recipe == null)
        {
            return false;
        }
        var craft = recipe.m_craftingStation != null ? recipe.m_craftingStation.m_name : null;
        var repair = recipe.m_repairStation != null ? recipe.m_repairStation.m_name : null;
        return (craft != null || repair != null) && craft != station.m_name && repair != station.m_name;
    }

    // Item of this station whose recipe ask station level 2 or more (vanilla refuse it at a plain station).
    private static bool NeedsLevel2(ItemDrop drop, CraftingStation station)
    {
        if (drop == null)
        {
            return false;
        }
        var data = drop.m_itemData;
        var shared = data.m_shared;
        if (!shared.m_useDurability || !shared.m_canBeReparied || !string.IsNullOrEmpty(shared.m_dlc))
        {
            return false;
        }
        var recipe = ObjectDB.instance.GetRecipe(data);
        if (recipe == null || recipe.m_minStationLevel < 2)
        {
            return false;
        }
        return (recipe.m_craftingStation != null && recipe.m_craftingStation.m_name == station.m_name)
               || (recipe.m_repairStation != null && recipe.m_repairStation.m_name == station.m_name);
    }

    // Effect prefabs one vanilla repair make at this station.
    private static int EnabledEffects(EffectList list)
    {
        var n = 0;
        if (list != null && list.m_effectPrefabs != null)
        {
            foreach (var e in list.m_effectPrefabs)
            {
                if (e != null && e.m_enabled && e.m_prefab != null)
                {
                    n++;
                }
            }
        }
        return n;
    }

    // Effect prefabs of one vanilla repair that are a sound (game sound prefab carry an AudioSource). Zero = the
    // "one repair sound" checks would count objects nobody can hear.
    private static int SoundEffects(EffectList list)
    {
        var n = 0;
        if (list != null && list.m_effectPrefabs != null)
        {
            foreach (var e in list.m_effectPrefabs)
            {
                if (e != null && e.m_enabled && e.m_prefab != null && e.m_prefab.GetComponentInChildren<AudioSource>(true) != null)
                {
                    n++;
                }
            }
        }
        return n;
    }

    // Custom data of an item as text, to see that nothing was added or changed.
    private static string DataOf(ItemDrop.ItemData item) =>
        item.m_customData == null || item.m_customData.Count == 0
            ? "(none)"
            : string.Join("|", item.m_customData.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value).ToArray());

    private static bool HasOwnData(ItemDrop.ItemData item) =>
        item.m_customData != null && item.m_customData.Keys.Any(k => k.StartsWith(ModInfo.Guid, StringComparison.Ordinal));

    private static bool Owned(MethodBase method) =>
        method != null && (Harmony.GetPatchInfo(method)?.Owners.Contains(ModInfo.Guid) ?? false);

    // ---------- hooks of my own test patches (static: Harmony patch methods are static) ----------

    // Effect list me watch (station's own repair effects) and how often vanilla played it during the click.
    private static EffectList _watch;
    private static int _watchCalls;
    // Objects made from any station's repair effect prefabs during the click (= repair sounds really started).
    private static int _repairSounds;
    private static HashSet<string> _repairEffectNames;

    // Fake cost mod, style 1 (RepairRequiresCoins, RepairCost): gate in front of the vanilla repair. -1 = off.
    private static int _gateAllow = -1;
    private static int _gateCalls;
    // Fake cost mod, style 2 (RepairRequiresMats): "can repair" answer changed while one repair runs. -1 = off.
    private static int _checkAllow = -1;
    private static int _checkPaid;
    private static int _checkRefused;
    private static bool _inPress;
    // Vanilla repairs started (the click's own + the mod's extra ones) since the fake mod was set.
    private static int _presses;
    // What the fake mod tell the player when it say no. Null = say nothing.
    private static string _reason;

    // Pad button forced down once for this pad component.
    private static UIGamePad _padTarget;
    private static bool _padArmed;
    private static int _padFired;

    private static int _rpcCalls;

    private static void ResetHooks()
    {
        _watch = null;
        _watchCalls = 0;
        _repairSounds = 0;
        _gateAllow = -1;
        _gateCalls = 0;
        _checkAllow = -1;
        _checkPaid = 0;
        _checkRefused = 0;
        _inPress = false;
        _presses = 0;
        _reason = null;
        _padTarget = null;
        _padArmed = false;
        _padFired = 0;
        _rpcCalls = 0;
    }

    // Postfix on EffectList.Create, only while one of my tests run.
    private static void CountEffect(EffectList __instance, GameObject[] __result)
    {
        if (ReferenceEquals(__instance, _watch))
        {
            _watchCalls++;
        }
        if (__result == null || _repairEffectNames == null)
        {
            return;
        }
        foreach (var go in __result)
        {
            if (go != null && _repairEffectNames.Contains(go.name.Replace("(Clone)", "")))
            {
                _repairSounds++;
            }
        }
    }

    // Prefix in front of the vanilla repair, like a cost mod that charge there.
    private static bool CostGate()
    {
        if (_gateAllow < 0)
        {
            return true;
        }
        _gateCalls++;
        if (_gateCalls <= _gateAllow)
        {
            return true;
        }
        if (_reason != null && Player.m_localPlayer != null)
        {
            Player.m_localPlayer.Message(MessageHud.MessageType.Center, _reason);
        }
        return false;
    }

    private static void PressBegin()
    {
        _inPress = true;
        _presses++;
    }

    private static void PressEnd() => _inPress = false;

    // Postfix on the vanilla "can repair this item" check, like a cost mod that charge there: only while one repair
    // runs, never while the game only ask "anything left?".
    private static void CostCheck(ref bool __result)
    {
        if (_checkAllow < 0 || !_inPress || !__result)
        {
            return;
        }
        if (_checkPaid < _checkAllow)
        {
            _checkPaid++;
            return;
        }
        _checkRefused++;
        if (_reason != null && Player.m_localPlayer != null)
        {
            Player.m_localPlayer.Message(MessageHud.MessageType.Center, _reason);
        }
        __result = false;
    }

    // Postfix on UIGamePad.ButtonPressed: say "pad button down" once, for the repair button's pad component only.
    private static void PadPress(UIGamePad __instance, ref bool __result)
    {
        if (_padArmed && ReferenceEquals(__instance, _padTarget))
        {
            _padArmed = false;
            _padFired++;
            __result = true;
        }
    }

    private static void CountRpc() => _rpcCalls++;

    private static HashSet<string> RepairEffectNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prefab in ZNetScene.instance.m_prefabs)
        {
            if (prefab == null)
            {
                continue;
            }
            var station = prefab.GetComponentInChildren<CraftingStation>(true);
            if (station == null || station.m_repairItemDoneEffects == null || station.m_repairItemDoneEffects.m_effectPrefabs == null)
            {
                continue;
            }
            foreach (var e in station.m_repairItemDoneEffects.m_effectPrefabs)
            {
                if (e != null && e.m_prefab != null)
                {
                    names.Add(e.m_prefab.name);
                }
            }
        }
        return names;
    }

    // Message HUD state before a click: centre messages add 2 fade entries each, top-left ones join a queue. Both
    // only drain in MessageHud.Update, so counts read in the same frame as the click are exact.
    private struct Tape
    {
        internal int Buffer;
        internal int Queue;
    }

    // ---------- rig: one test's world ----------

    private sealed class Rig
    {
        internal readonly string Name;
        internal readonly Checks C;
        internal readonly Player P;
        internal readonly Inventory Inv;
        internal readonly InventoryGui Gui;
        internal readonly MessageHud Msg;
        internal bool Ready;

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<ItemDrop.ItemData> _given = new List<ItemDrop.ItemData>();
        private readonly List<KeyValuePair<ItemDrop.ItemData, float>> _topped = new List<KeyValuePair<ItemDrop.ItemData, float>>();
        private List<ItemDrop.ItemData> _equippedBefore = new List<ItemDrop.ItemData>();
        private ItemDrop.ItemData _hiddenLeft;
        private ItemDrop.ItemData _hiddenRight;
        private HashSet<string> _recipes;
        private HashSet<string> _materials;
        private Dictionary<string, int> _stations;
        private bool _began;
        private bool _noCost;
        private bool _hadSkill;
        private float _level;
        private float _accumulator;
        private bool _tabCraft;
        private bool _tabUpgrade;
        private Harmony _harmony;

        internal Rig(string name, Checks c)
        {
            Name = name;
            C = c;
            P = Player.m_localPlayer;
            Inv = P != null ? P.GetInventory() : null;
            Gui = InventoryGui.instance;
            Msg = MessageHud.instance;
        }

        // Remember what me will change, empty the hands, hide other worn items from the repair, start my effect count.
        internal IEnumerator Begin()
        {
            Ready = false;
            if (P == null || Inv == null || Gui == null || Msg == null || ObjectDB.instance == null || ZNetScene.instance == null
                || ZoneSystem.instance == null)
            {
                C.Check(false, "no player, inventory window, message HUD or world");
                yield break;
            }
            if (Hud.IsUserHidden())
            {
                C.Check(false, "the HUD is hidden (Ctrl+F3): the game drops every message, so nothing can be checked");
                yield break;
            }
            // m_inCraftingStation: player's fixed tick not yet saw the station go. That tick shut the next window.
            if (InventoryGui.IsVisible() || P.GetCurrentCraftingStation() != null || P.m_inCraftingStation)
            {
                C.Note("the inventory window or a station was open at the start: closed");
                yield return Close();
            }

            _noCost = P.m_noPlacementCost;
            _equippedBefore = Inv.GetEquippedItems();
            _hiddenLeft = P.m_hiddenLeftItem;
            _hiddenRight = P.m_hiddenRightItem;
            _recipes = new HashSet<string>(P.m_knownRecipes);
            _materials = new HashSet<string>(P.m_knownMaterial);
            _stations = new Dictionary<string, int>(P.m_knownStations);
            _tabCraft = Gui.m_tabCraft.interactable;
            _tabUpgrade = Gui.m_tabUpgrade.interactable;
            _hadSkill = P.GetSkills().m_skillData.TryGetValue(Skills.SkillType.Crafting, out var skill);
            if (_hadSkill)
            {
                _level = skill.m_level;
                _accumulator = skill.m_accumulator;
            }
            _began = true;

            // Torch in hand lose durability every tick: it would be a new worn item in the middle of a test.
            P.UnequipItem(P.m_rightItem, false);
            P.UnequipItem(P.m_leftItem, false);

            // Worn items of the character or left by other tests: full for now, so vanilla not see them. Put back in End.
            var worn = new List<ItemDrop.ItemData>();
            Inv.GetWornItems(worn);
            foreach (var item in worn)
            {
                _topped.Add(new KeyValuePair<ItemDrop.ItemData, float>(item, item.m_durability));
                item.m_durability = item.GetMaxDurability();
            }
            if (worn.Count > 0)
            {
                C.Note($"{worn.Count} worn item(s) already in the bag set aside (durability put back at the end): {Durabilities(_topped.Select(p => p.Key))}");
            }

            // Skill at the top = no level up message or effect in the middle of a click. repair.skill set its own.
            SetSkill(100f, 0f);

            _repairEffectNames ??= RepairEffectNames();
            ResetHooks();
            _harmony = new Harmony(ModInfo.Guid + ".selftest");
            Patch(AccessTools.Method(typeof(EffectList), nameof(EffectList.Create)), postfix: nameof(CountEffect));
            Ready = true;
        }

        // One of my static methods as prefix / postfix of a game method, removed in End.
        internal void Patch(MethodBase target, string prefix = null, string postfix = null)
        {
            if (target == null)
            {
                throw new InvalidOperationException("game method to patch not found");
            }
            _harmony.Patch(target,
                prefix: prefix != null ? new HarmonyMethod(typeof(SelfTests), prefix) : null,
                postfix: postfix != null ? new HarmonyMethod(typeof(SelfTests), postfix) : null);
        }

        internal void End()
        {
            Step("test patches", () =>
            {
                _harmony?.UnpatchSelf();
                _harmony = null;
                ResetHooks();
            });
            if (!_began || P == null)
            {
                return;
            }
            Step("window", () =>
            {
                if (Gui != null && InventoryGui.IsVisible())
                {
                    Gui.Hide();
                }
                P.SetCraftingStation(null);
            });
            Step("nocost", () => P.m_noPlacementCost = _noCost);
            Step("items", () =>
            {
                foreach (var item in _given)
                {
                    if (!Inv.ContainsItem(item))
                    {
                        continue;
                    }
                    if (item.m_equipped)
                    {
                        P.UnequipItem(item, false);
                    }
                    Inv.RemoveItem(item);
                }
                _given.Clear();
            });
            Step("durability", () =>
            {
                foreach (var pair in _topped)
                {
                    pair.Key.m_durability = pair.Value;
                }
                _topped.Clear();
            });
            Step("equipment", () =>
            {
                foreach (var item in _equippedBefore)
                {
                    if (Inv.ContainsItem(item) && !item.m_equipped)
                    {
                        P.EquipItem(item, false);
                    }
                }
                if (_hiddenLeft != null || _hiddenRight != null)
                {
                    P.m_hiddenLeftItem = _hiddenLeft != null && Inv.ContainsItem(_hiddenLeft) && !_hiddenLeft.m_equipped ? _hiddenLeft : null;
                    P.m_hiddenRightItem = _hiddenRight != null && Inv.ContainsItem(_hiddenRight) && !_hiddenRight.m_equipped ? _hiddenRight : null;
                    P.SetupVisEquipment(P.m_visEquipment, false);
                }
            });
            Step("skill", () =>
            {
                var skills = P.GetSkills();
                if (_hadSkill)
                {
                    SetSkill(_level, _accumulator);
                }
                else
                {
                    skills.m_skillData.Remove(Skills.SkillType.Crafting);
                }
            });
            Step("spawned", () =>
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
            Step("knowledge", () =>
            {
                P.m_knownRecipes.RemoveWhere(r => !_recipes.Contains(r));
                P.m_knownMaterial.RemoveWhere(m => !_materials.Contains(m));
                foreach (var key in P.m_knownStations.Keys.ToList())
                {
                    if (_stations.TryGetValue(key, out var level))
                    {
                        P.m_knownStations[key] = level;
                    }
                    else
                    {
                        P.m_knownStations.Remove(key);
                    }
                }
            });
            Step("tabs", () =>
            {
                Gui.m_tabCraft.interactable = _tabCraft;
                Gui.m_tabUpgrade.interactable = _tabUpgrade;
            });
            // "New item" banners of what me gave: not shown yet = dropped, so they not pile up over the next tests.
            Step("banners", () => Msg.ClearUnlockQueue());
        }

        private void Step(string what, Action act)
        {
            try
            {
                act();
            }
            catch (Exception e)
            {
                SelfTest.Note(Name, $"clean-up of {what} failed: {e.Message}");
            }
        }

        internal void SetSkill(float level, float accumulator)
        {
            var skill = P.GetSkills().GetSkill(Skills.SkillType.Crafting);
            skill.m_level = level;
            skill.m_accumulator = accumulator;
        }

        internal Skills.Skill Skill => P.GetSkills().GetSkill(Skills.SkillType.Crafting);

        internal void Track(GameObject go)
        {
            if (go != null)
            {
                _spawned.Add(go);
            }
        }

        // Station in front of the player (angle = degrees off the look direction). Spawn stones have no roof: this
        // copy ask no roof and no fire, and can be used from far (else the game close the window).
        internal CraftingStation Station(string prefabName, float angle, float distance)
        {
            var prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null || prefab.GetComponentInChildren<CraftingStation>(true) == null)
            {
                C.Check(false, $"station prefab '{prefabName}' is not in this game");
                return null;
            }
            var forward = P.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            var dir = Quaternion.Euler(0f, angle, 0f) * forward;
            var pos = P.transform.position + dir * distance;
            pos.y = ZoneSystem.instance.GetGroundHeight(pos);
            var go = Object.Instantiate(prefab, pos, Quaternion.LookRotation(-dir));
            _spawned.Add(go);
            Sturdy(go);
            var station = go.GetComponentInChildren<CraftingStation>();
            station.m_craftRequireRoof = false;
            station.m_craftRequireFire = false;
            station.m_useDistance = 50f;
            return station;
        }

        internal Container Chest(float angle, float distance)
        {
            var prefab = ZNetScene.instance.GetPrefab(ChestPrefab);
            if (prefab == null || prefab.GetComponent<Container>() == null)
            {
                C.Check(false, $"chest prefab '{ChestPrefab}' is not in this game");
                return null;
            }
            var forward = P.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            var pos = P.transform.position + Quaternion.Euler(0f, angle, 0f) * forward * distance;
            pos.y = ZoneSystem.instance.GetGroundHeight(pos);
            var go = Object.Instantiate(prefab, pos, Quaternion.identity);
            _spawned.Add(go);
            Sturdy(go);
            return go.GetComponent<Container>();
        }

        // Piece of this test never fall apart in the middle of it. Game update wear from a static list (30 s after
        // the piece is made), component off change nothing there: me turn off the support and rain rules on this copy.
        private static void Sturdy(GameObject go)
        {
            var wear = go.GetComponent<WearNTear>();
            if (wear != null)
            {
                wear.m_noSupportWear = false;
                wear.m_noRoofWear = false;
            }
        }

        internal ItemDrop.ItemData Give(string prefab)
        {
            if (ObjectDB.instance.GetItemPrefab(prefab) == null)
            {
                C.Check(false, $"item prefab '{prefab}' is not in this game");
                return null;
            }
            var item = Inv.AddItem(prefab, 1, 1, 0, 0L, "", false, true);
            if (item == null)
            {
                C.Check(false, $"could not add {prefab} to the inventory ({Inv.GetEmptySlots()} free slots)");
                return null;
            }
            _given.Add(item);
            return item;
        }

        // Item of this station that need station level 2: the wanted one, or another one the game has.
        internal ItemDrop.ItemData GiveLevel2(string wanted, CraftingStation station)
        {
            string pick = null;
            var prefab = ObjectDB.instance.GetItemPrefab(wanted);
            if (prefab != null && NeedsLevel2(prefab.GetComponent<ItemDrop>(), station))
            {
                pick = wanted;
            }
            else
            {
                foreach (var recipe in ObjectDB.instance.m_recipes)
                {
                    if (recipe != null && recipe.m_item != null && recipe.m_enabled && recipe.m_minStationLevel == 2
                        && NeedsLevel2(recipe.m_item, station) && ObjectDB.instance.GetItemPrefab(recipe.m_item.gameObject.name) != null)
                    {
                        pick = recipe.m_item.gameObject.name;
                        break;
                    }
                }
                if (pick != null)
                {
                    C.Note($"{wanted} is not a level 2 item of {station.m_name} in this game: {pick} used instead");
                }
            }
            if (pick == null)
            {
                C.Check(false, $"no item of {station.m_name} needs station level 2 in this game ({wanted} does not)");
                return null;
            }
            return Give(pick);
        }

        // First <count> of these prefabs that pass the test, added to the bag in that order. Null (check failed) when
        // the game has too few.
        internal List<ItemDrop.ItemData> GivePicked(int count, string what, Func<ItemDrop.ItemData, bool> fits, IEnumerable<string> candidates)
        {
            var list = new List<ItemDrop.ItemData>();
            var tried = new List<string>();
            foreach (var name in candidates)
            {
                if (list.Count == count)
                {
                    break;
                }
                tried.Add(name);
                var prefab = ObjectDB.instance.GetItemPrefab(name);
                var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null || !fits(drop.m_itemData))
                {
                    continue;
                }
                var item = Give(name);
                if (item == null)
                {
                    return null;
                }
                list.Add(item);
            }
            if (list.Count < count)
            {
                C.Check(false, $"setup: only {list.Count} of the {count} needed {what} found in this game (tried {string.Join(", ", tried.ToArray())})");
                return null;
            }
            if (!list.Select(PrefabName).SequenceEqual(candidates.Take(count)))
            {
                C.Note($"{what}: {string.Join(", ", list.Select(PrefabName).ToArray())} used (not the first choices {string.Join(", ", candidates.Take(count).ToArray())})");
            }
            return list;
        }

        // Gear a plain (level 1) station of this kind repairs, by its recipe.
        internal List<ItemDrop.ItemData> GiveOf(CraftingStation station, int count, IEnumerable<string> candidates) =>
            GivePicked(count, $"level 1 items of {station.m_name}", item => RecipeProblem(item, station, 1) == null, candidates);

        // Repairable gear of some other station.
        internal List<ItemDrop.ItemData> GiveForeign(CraftingStation station, int count, IEnumerable<string> candidates) =>
            GivePicked(count, $"repairable items of another station than {station.m_name}", item => OtherStationGear(item, station), candidates);

        internal void Damage(ItemDrop.ItemData item, float fraction) => item.m_durability = item.GetMaxDurability() * fraction;

        internal void DamageAll(IList<ItemDrop.ItemData> items)
        {
            for (var i = 0; i < items.Count; i++)
            {
                Damage(items[i], 0.3f + 0.1f * (i % 5));
            }
        }

        internal List<ItemDrop.ItemData> Worn()
        {
            var worn = new List<ItemDrop.ItemData>();
            Inv.GetWornItems(worn);
            return worn;
        }

        // These items in the order vanilla walk them (bag order), worn ones only.
        internal List<ItemDrop.ItemData> WornOf(ICollection<ItemDrop.ItemData> items) => Worn().Where(items.Contains).ToList();

        internal IEnumerator Open(CraftingStation station)
        {
            P.SetCraftingStation(station);
            Gui.Show(null, 3);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return null;
        }

        // Plain inventory, no station (what Tab open).
        internal IEnumerator OpenPlain()
        {
            Gui.Show(null);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return null;
        }

        // Window shut and station left, and the player's fixed tick saw it (else that tick shut the next window).
        internal IEnumerator Close()
        {
            if (InventoryGui.IsVisible())
            {
                Gui.Hide();
            }
            P.SetCraftingStation(null);
            var until = Time.realtimeSinceStartup + 2f;
            do
            {
                yield return new WaitForFixedUpdate();
                yield return null;
            }
            while ((InventoryGui.IsVisible() || P.m_inCraftingStation) && Time.realtimeSinceStartup < until);
        }

        internal bool At(CraftingStation station) =>
            C.Check(station != null && P.GetCurrentCraftingStation() == station && InventoryGui.IsVisible(),
                $"setup: the crafting window is open at {(station != null ? station.m_name : "the station")}");

        internal bool ButtonShown => Gui.m_repairButton.gameObject.activeInHierarchy;

        internal bool ButtonUsable => ButtonShown && Gui.m_repairButton.interactable;

        internal bool Glows => Gui.m_repairButtonGlow.gameObject.activeSelf;

        internal string ButtonState =>
            $"shown {ButtonShown}, usable {Gui.m_repairButton.interactable}, glow {Glows}";

        // Greyed out: there, not clickable, no glow.
        internal bool ButtonGrey => ButtonShown && !Gui.m_repairButton.interactable && !Glows;

        internal string CenterText => Msg.m_messageCenterText.text;

        // Start of a click: counters to zero, effect list to watch (null = none), HUD counts.
        internal Tape Mark(EffectList watch)
        {
            _watch = watch;
            _watchCalls = 0;
            _repairSounds = 0;
            _rpcCalls = 0;
            return new Tape { Buffer = Msg._crossFadeTextBuffer.Count, Queue = Msg.m_msgQeue.Count };
        }

        // Centre messages that reached the screen since the mark (same frame only).
        internal int CenterShown(Tape tape) => (Msg._crossFadeTextBuffer.Count - tape.Buffer) / 2;

        internal List<string> TopLeftSince(Tape tape) => Msg.m_msgQeue.Skip(tape.Queue).Select(m => m.m_text).ToList();

        // Mouse click on the repair button the way the game get it: Button.OnPointerClick -> onClick, refused by the
        // button itself when greyed out. False = button not clickable now.
        internal bool Click()
        {
            var button = Gui.m_repairButton;
            if (button == null || !button.gameObject.activeInHierarchy || !button.interactable)
            {
                return false;
            }
            if (button.IsInteractable())
            {
                button.OnPointerClick(new PointerEventData(EventSystem.current));
            }
            else
            {
                C.Note("a parent canvas group blocks the button for the mouse right now: its click listeners were called directly");
                button.onClick.Invoke();
            }
            return true;
        }

        // After any click: mod left nothing switched (messages not muted, station has its own effect list again).
        internal void CheckIdle(CraftingStation station, EffectList own, string when)
        {
            C.Check(!BulkRepair.Muting && BulkRepair.LastMuted == null, $"{when}: the mod no longer mutes messages (muting {BulkRepair.Muting}, kept '{BulkRepair.LastMuted}')");
            if (station != null)
            {
                C.Check(ReferenceEquals(station.m_repairItemDoneEffects, own), $"{when}: the station has its own repair effects back");
            }
        }
    }

    // ---------- repair.bench (T01) ----------

    private static IEnumerator RunBench()
    {
        var c = new Checks(BenchName);
        var rig = new Rig(BenchName, c);
        try
        {
            yield return rig.Begin();
            if (!rig.Ready)
            {
                c.Report();
                yield break;
            }
            var bench = rig.Station(BenchPrefab, 0f, 3f);
            var items = bench != null ? rig.GiveOf(bench, 4, BenchGear) : null;
            var armor = items != null ? rig.GiveOf(bench, 1, BenchArmor) : null;
            if (armor == null)
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
            // Station hide what is in the hands, so the worn piece of this test is armour.
            var tunic = armor[0];
            items.Add(tunic);
            rig.P.EquipItem(tunic, false);
            c.Check(tunic.m_equipped, $"setup: {PrefabName(tunic)} is put on (the equipped item of this test)");
            rig.DamageAll(items);
            yield return rig.Open(bench);
            if (!rig.At(bench))
            {
                c.Report();
                yield break;
            }
            rig.Gui.UpdateRepair();
            c.Check(rig.ButtonUsable && rig.Glows, $"before the click the repair button is usable and glows ({rig.ButtonState})");
            var order = rig.WornOf(items);
            c.Check(rig.Worn().Count == 5 && order.Count == 5, $"setup: exactly the 5 test items are worn ({Durabilities(rig.Worn())})");
            var effects = bench.m_repairItemDoneEffects;
            var perRepair = EnabledEffects(effects);
            var soundPrefabs = SoundEffects(effects);
            c.Note($"workbench level {bench.GetLevel()}, {perRepair} repair effect prefab(s), {soundPrefabs} with a sound, items in bag order: {Names(order)}");
            // Else "one repair sound" below would be a count of silent objects.
            c.Check(perRepair >= 1 && soundPrefabs >= 1, $"setup: the workbench's repair effect is a sound ({soundPrefabs} of its {perRepair} effect prefab(s) carry an audio source)");

            var tape = rig.Mark(effects);
            c.Check(rig.Click(), "the repair button takes the click");
            c.Check(items.All(Full), $"one click repairs all 5 items, in the bag and worn: {Durabilities(items)}");
            c.Check(tunic.m_equipped, "the armour piece is still worn after the repair");
            c.Check(_watchCalls == 1, $"the station plays its repair effect once for the click, not once per item (played {_watchCalls} time(s))");
            c.Check(_repairSounds == perRepair, $"repair sound objects started: {_repairSounds}, one vanilla repair starts {perRepair}");
            var want = RepairedText(order);
            c.Check(rig.CenterText == want, $"the message is '{rig.CenterText}', expected '{want}'");
            // The three names then "+2", as one piece (not EndsWith: some languages put the names in the middle).
            var words = string.Join(", ", order.Take(3).Select(i => Localization.instance.Localize(i.m_shared.m_name)).ToArray()) + " +2";
            c.Check(rig.CenterText.IndexOf(words, StringComparison.Ordinal) >= 0 && rig.CenterText.IndexOf('$') < 0,
                $"3 names then '+2' for the other two, every name translated: '{rig.CenterText}' holds '{words}'");
            c.Check(rig.CenterShown(tape) == 2, $"{rig.CenterShown(tape)} messages reached the middle of the screen in the click (2 = the game's own for the first item, then the summary over it)");
            c.Check(rig.TopLeftSince(tape).Count == 0, $"no top-left message: {string.Join(" / ", rig.TopLeftSince(tape).ToArray())}");
            rig.CheckIdle(bench, effects, "after the click");
            rig.Gui.UpdateRepair();
            c.Check(rig.ButtonGrey && !rig.Gui.HaveRepairableItems(), $"after the click the button is greyed out and no longer glows ({rig.ButtonState})");
            c.Check(items.All(i => !HasOwnData(i)), "the mod stores nothing on the repaired items");
            SelfTest.Screenshot(BenchName, "after-click");
            yield return null;
            yield return null;
            c.Check(InventoryGui.IsVisible() && rig.ButtonGrey, $"two frames later (the game's own per-frame update) the button is still greyed out ({rig.ButtonState})");
            c.Report($"; '{want}'");
        }
        finally
        {
            rig.End();
        }
    }

    // ---------- repair.stations (T02) ----------

    private static IEnumerator RunStations()
    {
        var c = new Checks(StationsName);
        var rig = new Rig(StationsName, c);
        try
        {
            yield return rig.Begin();
            if (!rig.Ready)
            {
                c.Report();
                yield break;
            }
            var bench = rig.Station(BenchPrefab, -35f, 3f);
            var forge = rig.Station(ForgePrefab, 35f, 4f);
            var wood = bench != null && forge != null ? rig.GiveOf(bench, 3, BenchGear) : null;
            var bronze = wood != null ? rig.GiveOf(forge, 4, ForgeGear) : null;
            if (bronze == null)
            {
                c.Report();
                yield break;
            }
            c.Check(bronze.All(i => OtherStationGear(i, bench)) && wood.All(i => OtherStationGear(i, forge)),
                "setup: by their recipes no forge item is workbench gear and no workbench item is forge gear");
            yield return new WaitForSeconds(0.5f);
            rig.DamageAll(wood);
            rig.DamageAll(bronze);
            var bronzeBefore = bronze.Select(i => i.m_durability).ToArray();

            // Workbench: only its own gear.
            yield return rig.Open(bench);
            if (!rig.At(bench))
            {
                c.Report();
                yield break;
            }
            var order = rig.WornOf(wood);
            var effects = bench.m_repairItemDoneEffects;
            var tape = rig.Mark(effects);
            c.Check(rig.Click(), "workbench: the repair button takes the click");
            c.Check(wood.All(Full), $"workbench: all workbench gear is repaired: {Durabilities(wood)}");
            c.Check(bronze.Select(i => i.m_durability).SequenceEqual(bronzeBefore), $"workbench: the bronze gear keeps its exact durability: {Durabilities(bronze)}");
            c.Check(_watchCalls == 1, $"workbench: repair effect played once ({_watchCalls})");
            c.Check(rig.CenterText == RepairedText(order), $"workbench: message '{rig.CenterText}', expected '{RepairedText(order)}'");
            rig.CheckIdle(bench, effects, "workbench");
            rig.Gui.UpdateRepair();
            c.Check(rig.ButtonGrey, $"workbench: with only bronze gear left damaged the button is greyed out and does not glow ({rig.ButtonState})");
            yield return null;
            yield return null;
            c.Check(InventoryGui.IsVisible() && rig.ButtonGrey, $"workbench: still greyed out two frames later ({rig.ButtonState})");
            yield return rig.Close();

            // Forge: one click, all bronze.
            yield return rig.Open(forge);
            if (!rig.At(forge))
            {
                c.Report();
                yield break;
            }
            rig.Gui.UpdateRepair();
            c.Check(rig.ButtonUsable && rig.Glows, $"forge: the button is usable and glows for the bronze gear ({rig.ButtonState})");
            order = rig.WornOf(bronze);
            c.Check(order.Count == 4 && rig.Worn().Count == 4, $"setup: exactly the 4 bronze items are worn at the forge ({Durabilities(rig.Worn())})");
            effects = forge.m_repairItemDoneEffects;
            tape = rig.Mark(effects);
            c.Check(rig.Click(), "forge: the repair button takes the click");
            c.Check(bronze.All(Full), $"forge: one click repairs all the bronze gear: {Durabilities(bronze)}");
            c.Check(_watchCalls == 1, $"forge: repair effect played once ({_watchCalls})");
            c.Check(rig.CenterText == RepairedText(order) && rig.CenterShown(tape) == 2, $"forge: message '{rig.CenterText}', expected '{RepairedText(order)}' ({rig.CenterShown(tape)} messages)");
            rig.CheckIdle(forge, effects, "forge");
            rig.Gui.UpdateRepair();
            c.Check(rig.ButtonGrey, $"forge: the button is greyed out after the click ({rig.ButtonState})");
            c.Report($"; workbench level {bench.GetLevel()}, forge level {forge.GetLevel()}");
        }
        finally
        {
            rig.End();
        }
    }

    // ---------- repair.level (T03) ----------

    private static IEnumerator RunLevel()
    {
        var c = new Checks(LevelName);
        var rig = new Rig(LevelName, c);
        try
        {
            yield return rig.Begin();
            if (!rig.Ready)
            {
                c.Report();
                yield break;
            }
            var bench = rig.Station(BenchPrefab, -35f, 3f);
            var forge = rig.Station(ForgePrefab, 35f, 4f);
            if (bench == null || forge == null)
            {
                c.Report();
                yield break;
            }
            // Bag order on purpose: the level 2 item first, so every press walk past it.
            var leather = rig.GiveLevel2("ArmorLeatherChest", bench);
            var wood = leather != null ? rig.GiveOf(bench, 2, BenchGear) : null;
            var iron = wood != null ? rig.GiveLevel2("SwordIron", forge) : null;
            var bronze = iron != null ? rig.GiveOf(forge, 2, ForgeGear) : null;
            if (bronze == null)
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
            c.Check(bench.GetLevel() == 1 && forge.GetLevel() == 1, $"setup: both stations have no upgrade (workbench level {bench.GetLevel()}, forge level {forge.GetLevel()})");
            c.Note($"level 2 items: {PrefabName(leather)} needs {bench.m_name} level {ObjectDB.instance.GetRecipe(leather).m_minStationLevel}, "
                   + $"{PrefabName(iron)} needs {forge.m_name} level {ObjectDB.instance.GetRecipe(iron).m_minStationLevel}");

            var all = new List<ItemDrop.ItemData> { leather, iron };
            all.AddRange(wood);
            all.AddRange(bronze);
            rig.DamageAll(all);
            var leatherBefore = leather.m_durability;
            var ironBefore = iron.m_durability;

            yield return rig.Open(bench);
            if (!rig.At(bench))
            {
                c.Report();
                yield break;
            }
            var order = rig.WornOf(wood);
            var effects = bench.m_repairItemDoneEffects;
            var tape = rig.Mark(effects);
            c.Check(rig.Click(), "workbench: the repair button takes the click");
            c.Check(wood.All(Full), $"workbench: the two level 1 items are repaired: {Durabilities(wood)}");
            c.Check(leather.m_durability == leatherBefore, $"workbench level 1: {Names(new[] { leather })} stays damaged ({F(leather.m_durability)}, was {F(leatherBefore)})");
            c.Check(iron.m_durability == ironBefore && !bronze.Any(Full), $"workbench: the forge gear stays damaged ({Durabilities(bronze)})");
            c.Check(rig.CenterText == RepairedText(order) && rig.CenterShown(tape) == 2, $"workbench: message '{rig.CenterText}', expected '{RepairedText(order)}'");
            c.Check(_watchCalls == 1, $"workbench: repair effect played once ({_watchCalls})");
            rig.CheckIdle(bench, effects, "workbench");
            rig.Gui.UpdateRepair();
            c.Check(rig.ButtonGrey, $"workbench: nothing left that it can repair, the button is greyed out ({rig.ButtonState})");
            yield return rig.Close();

            yield return rig.Open(forge);
            if (!rig.At(forge))
            {
                c.Report();
                yield break;
            }
            order = rig.WornOf(bronze);
            effects = forge.m_repairItemDoneEffects;
            tape = rig.Mark(effects);
            c.Check(rig.Click(), "forge: the repair button takes the click");
            c.Check(bronze.All(Full), $"forge: the two level 1 items are repaired: {Durabilities(bronze)}");
            c.Check(iron.m_durability == ironBefore, $"forge level 1: {Names(new[] { iron })} stays damaged ({F(iron.m_durability)}, was {F(ironBefore)})");
            c.Check(leather.m_durability == leatherBefore, "forge: the workbench item stays damaged too");
            c.Check(rig.CenterText == RepairedText(order) && rig.CenterShown(tape) == 2, $"forge: message '{rig.CenterText}', expected '{RepairedText(order)}'");
            c.Check(_watchCalls == 1, $"forge: repair effect played once ({_watchCalls})");
            rig.CheckIdle(forge, effects, "forge");
            rig.Gui.UpdateRepair();
            c.Check(rig.ButtonGrey, $"forge: the button is greyed out ({rig.ButtonState})");
            c.Report();
        }
        finally
        {
            rig.End();
        }
    }

    // ---------- repair.single (T04) ----------

    private static IEnumerator RunSingle()
    {
        var c = new Checks(SingleName);
        var rig = new Rig(SingleName, c);
        try
        {
            yield return rig.Begin();
            if (!rig.Ready)
            {
                c.Report();
                yield break;
            }
            var bench = rig.Station(BenchPrefab, 0f, 3f);
            var own = bench != null ? rig.GiveOf(bench, 1, new[] { "Club" }.Concat(BenchGear).Distinct()) : null;
            var foreign = own != null ? rig.GiveForeign(bench, 1, ForgeGear) : null;
            if (foreign == null)
            {
                c.Report();
                yield break;
            }
            // "club" = the one workbench item, "sword" = the item of another station (their first choices).
            var club = own[0];
            var sword = foreign[0];
            yield return new WaitForSeconds(0.5f);
            yield return rig.Open(bench);
            if (!rig.At(bench))
            {
                c.Report();
                yield break;
            }
            var effects = bench.m_repairItemDoneEffects;

            // Reference: the vanilla repair itself, called straight (no click, so no mod code around it).
            rig.Damage(club, 0.5f);
            var tape = rig.Mark(effects);
            rig.Gui.RepairOneItem();
            var refText = rig.CenterText;
            var refShown = rig.CenterShown(tape);
            var refEffects = _watchCalls;
            var refSounds = _repairSounds;
            // refSounds 0 = nothing to hear in vanilla either: "same sound as vanilla" below would prove nothing.
            c.Check(Full(club) && refShown == 1 && refEffects == 1 && refSounds >= 1 && SoundEffects(effects) >= 1,
                $"reference: the vanilla repair fixes the item with one message and one effect that starts a sound ('{refText}', {refShown} message(s), {refEffects} effect call(s), {refSounds} object(s), {SoundEffects(effects)} sound prefab(s))");
            c.Check(refText == RepairedText(new[] { club }), $"reference: the vanilla message is '{refText}', expected '{RepairedText(new[] { club })}'");

            // (a) it is the only worn item of the whole bag.
            rig.Damage(club, 0.5f);
            c.Check(rig.Worn().Count == 1, $"setup (a): one worn item in the whole bag ({Durabilities(rig.Worn())})");
            rig.Gui.UpdateRepair();
            tape = rig.Mark(effects);
            c.Check(rig.Click(), "(a) the repair button takes the click");
            c.Check(Full(club), $"(a) the item is repaired ({Durabilities(new[] { club })})");
            c.Check(rig.CenterText == refText && rig.CenterShown(tape) == refShown, $"(a) same message as vanilla, shown once: '{rig.CenterText}' x{rig.CenterShown(tape)} (vanilla '{refText}' x{refShown})");
            c.Check(_watchCalls == refEffects && _repairSounds == refSounds, $"(a) same repair effect as vanilla: {_watchCalls} call(s), {_repairSounds} object(s) (vanilla {refEffects}, {refSounds})");
            c.Check(rig.TopLeftSince(tape).Count == 0, "(a) no top-left message");
            rig.CheckIdle(bench, effects, "(a)");
            rig.Gui.UpdateRepair();
            c.Check(rig.ButtonGrey, $"(a) the button is greyed out after ({rig.ButtonState})");

            // (b) one repairable item plus a damaged item of another station: the mod looks, finds nothing more.
            rig.Damage(club, 0.4f);
            rig.Damage(sword, 0.6f);
            var swordBefore = sword.m_durability;
            c.Check(rig.Worn().Count == 2, $"setup (b): the workbench item and the other station's item are worn ({Durabilities(rig.Worn())})");
            rig.Gui.UpdateRepair();
            tape = rig.Mark(effects);
            c.Check(rig.Click(), "(b) the repair button takes the click");
            c.Check(Full(club) && sword.m_durability == swordBefore, $"(b) the workbench item is repaired, the other station's item untouched ({Durabilities(new[] { club, sword })})");
            c.Check(rig.CenterText == refText && rig.CenterShown(tape) == refShown, $"(b) same message as vanilla, shown once: '{rig.CenterText}' x{rig.CenterShown(tape)}");
            c.Check(_watchCalls == refEffects && _repairSounds == refSounds, $"(b) same repair effect as vanilla: {_watchCalls} call(s), {_repairSounds} object(s)");
            c.Check(rig.TopLeftSince(tape).Count == 0, "(b) no top-left message");
            rig.CheckIdle(bench, effects, "(b)");
            c.Report($"; '{refText}'");
        }
        finally
        {
            rig.End();
        }
    }

    // ---------- repair.none (T05) ----------

    private static IEnumerator RunNone()
    {
        var c = new Checks(NoneName);
        var rig = new Rig(NoneName, c);
        try
        {
            yield return rig.Begin();
            if (!rig.Ready)
            {
                c.Report();
                yield break;
            }
            var bench = rig.Station(BenchPrefab, 0f, 3f);
            var bronze = bench != null ? rig.GiveForeign(bench, 2, ForgeGear) : null;
            if (bronze == null)
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
            yield return rig.Open(bench);
            if (!rig.At(bench))
            {
                c.Report();
                yield break;
            }

            // Nothing worn at all.
            c.Check(rig.Worn().Count == 0, $"setup: nothing is damaged ({Durabilities(rig.Worn())})");
            yield return null;
            c.Check(rig.ButtonGrey, $"nothing damaged: the button shows greyed out, no glow ({rig.ButtonState})");

            // Worn items, but none this station can repair.
            rig.DamageAll(bronze);
            var before = bronze.Select(i => i.m_durability).ToArray();
            yield return null;
            yield return null;
            c.Check(rig.ButtonGrey, $"only another station's gear damaged: the button shows greyed out, no glow ({rig.ButtonState})");
            var effects = bench.m_repairItemDoneEffects;
            var tape = rig.Mark(effects);
            var text = rig.CenterText;
            c.Check(!rig.Click(), "the greyed out button refuses the click");
            // The real handler of a mouse click: the button itself drop it while greyed out.
            rig.Gui.m_repairButton.OnPointerClick(new PointerEventData(EventSystem.current));
            c.Check(bronze.Select(i => i.m_durability).SequenceEqual(before) && rig.CenterShown(tape) == 0 && rig.CenterText == text && _watchCalls == 0,
                $"a mouse click on the greyed out button does nothing ({Durabilities(bronze)}, {rig.CenterShown(tape)} message(s), {_watchCalls} effect(s))");

            // A UI mod that calls the press without looking at the button: vanilla says "nothing left", the mod stays out.
            tape = rig.Mark(effects);
            rig.Gui.OnRepairPressed();
            c.Check(bronze.Select(i => i.m_durability).SequenceEqual(before), $"press called directly: nothing is repaired ({Durabilities(bronze)})");
            c.Check(rig.CenterText == BulkRepair.VanillaNothingLeft && rig.CenterShown(tape) == 1, $"press called directly: only the game's own '{BulkRepair.VanillaNothingLeft}' shows ('{rig.CenterText}' x{rig.CenterShown(tape)})");
            c.Check(rig.TopLeftSince(tape).Count == 0 && _watchCalls == 0, $"press called directly: no top-left message, no repair effect ({rig.TopLeftSince(tape).Count}, {_watchCalls})");
            rig.CheckIdle(bench, effects, "press called directly");
            c.Check(rig.ButtonGrey, $"the button is still greyed out ({rig.ButtonState})");
            c.Report();
        }
        finally
        {
            rig.End();
        }
    }

    // ---------- repair.nocost (T06) ----------

    private static IEnumerator RunNoCost()
    {
        var c = new Checks(NoCostName);
        var rig = new Rig(NoCostName, c);
        try
        {
            yield return rig.Begin();
            if (!rig.Ready)
            {
                c.Report();
                yield break;
            }
            // No station in this test: gear of two stations told apart by their recipes below.
            var items = rig.GivePicked(3, "repairable items", Repairable, BenchGear);
            var more = items != null ? rig.GivePicked(2, "repairable items", Repairable, ForgeGear) : null;
            if (more == null)
            {
                c.Report();
                yield break;
            }
            items.AddRange(more);
            var homes = items.Select(i => ObjectDB.instance.GetRecipe(i)).Where(r => r != null)
                .Select(r => r.m_craftingStation != null ? r.m_craftingStation.m_name : r.m_repairStation != null ? r.m_repairStation.m_name : null)
                .Where(n => n != null).Distinct().ToList();
            c.Check(homes.Count >= 2, $"setup: the items come from at least two stations ({string.Join(", ", homes.ToArray())})");
            c.Check(_repairEffectNames.Count > 0, "setup: the game's stations have repair effects to listen for");
            rig.DamageAll(items);
            if (rig.P.NoCostCheat())
            {
                c.Note("nocost was on at the start: turned off for the test, put back at the end");
                rig.P.SetNoPlacementCost(false);
            }
            c.Check(rig.P.GetCurrentCraftingStation() == null && !rig.P.NoCostCheat(), "setup: no station, nocost off");

            yield return rig.OpenPlain();
            c.Check(InventoryGui.IsVisible() && !rig.Gui.m_repairButton.gameObject.activeSelf, "nocost off: the plain inventory has no repair button");

            // The call the console command "nocost" makes (flag not saved, not sent to anyone; rig put it back).
            rig.P.SetNoPlacementCost(true);
            yield return null;
            yield return null;
            c.Check(rig.P.NoCostCheat() && InventoryGui.IsVisible(), "nocost is on and the plain inventory is still open");
            c.Check(rig.ButtonUsable && rig.Glows && rig.Gui.m_repairPanel.gameObject.activeSelf,
                $"nocost on: the repair button shows in the plain inventory, usable and glowing ({rig.ButtonState})");
            // For the eye: where the button sits (the game's own layout, me not measure it).
            SelfTest.Screenshot(NoCostName, "button");
            yield return null;
            yield return null;
            var order = rig.WornOf(items);
            c.Check(order.Count == 5 && rig.Worn().Count == 5, $"setup: exactly the 5 test items are worn ({Durabilities(rig.Worn())})");

            var tape = rig.Mark(null);
            c.Check(rig.Click(), "the repair button takes the click");
            c.Check(items.All(Full), $"one click repairs everything, workbench and forge gear alike: {Durabilities(items)}");
            c.Check(_repairSounds == 0, $"no repair sound without a station ({_repairSounds} repair effect object(s) started)");
            var want = RepairedText(order);
            c.Check(rig.CenterText == want && rig.CenterShown(tape) == 2, $"one message: '{rig.CenterText}', expected '{want}' ({rig.CenterShown(tape)} reached the screen, 2 = the game's own then the summary over it)");
            c.Check(rig.TopLeftSince(tape).Count == 0, "no top-left message");
            rig.CheckIdle(null, null, "after the click");
            rig.Gui.UpdateRepair();
            c.Check(rig.ButtonGrey, $"after the click the button is greyed out ({rig.ButtonState})");
            c.Report($"; '{want}'");
        }
        finally
        {
            rig.End();
        }
    }

    // ---------- repair.nocost-station (T07) ----------

    private static IEnumerator RunNoCostStation()
    {
        var c = new Checks(NoCostStationName);
        var rig = new Rig(NoCostStationName, c);
        try
        {
            yield return rig.Begin();
            if (!rig.Ready)
            {
                c.Report();
                yield break;
            }
            var bench = rig.Station(BenchPrefab, 0f, 3f);
            var wood = bench != null ? rig.GiveOf(bench, 2, BenchGear) : null;
            var bronze = wood != null ? rig.GiveForeign(bench, 2, ForgeGear) : null;
            if (bronze == null)
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
            var all = wood.Concat(bronze).ToList();
            rig.DamageAll(all);
            // The call the console command "nocost" makes.
            rig.P.SetNoPlacementCost(true);
            yield return rig.Open(bench);
            if (!rig.At(bench))
            {
                c.Report();
                yield break;
            }
            var order = rig.WornOf(all);
            c.Check(rig.P.NoCostCheat() && order.Count == 4 && rig.Worn().Count == 4, $"setup: nocost is on and exactly the 4 test items are worn ({Durabilities(rig.Worn())})");
            var effects = bench.m_repairItemDoneEffects;
            var tape = rig.Mark(effects);
            c.Check(rig.Click(), "the repair button takes the click");
            c.Check(all.All(Full), $"nocost at the workbench: one click repairs everything, bronze included: {Durabilities(all)}");
            c.Check(_watchCalls == 1, $"the station's repair effect plays once ({_watchCalls})");
            c.Check(rig.CenterText == RepairedText(order) && rig.CenterShown(tape) == 2, $"message '{rig.CenterText}', expected '{RepairedText(order)}'");
            rig.CheckIdle(bench, effects, "after the click");

            // nocost off again: the button in the plain inventory is gone.
            yield return rig.Close();
            yield return rig.OpenPlain();
            c.Check(InventoryGui.IsVisible() && rig.ButtonShown, $"nocost still on: the plain inventory has the repair button ({rig.ButtonState})");
            rig.P.SetNoPlacementCost(false);
            yield return null;
            yield return null;
            c.Check(!rig.P.NoCostCheat() && InventoryGui.IsVisible() && !rig.Gui.m_repairButton.gameObject.activeSelf, "nocost off: the repair button in the plain inventory is gone");
            c.Report();
        }
        finally
        {
            rig.End();
        }
    }

    // ---------- repair.skill (T08) ----------

    private static IEnumerator RunSkill()
    {
        var c = new Checks(SkillName);
        var rig = new Rig(SkillName, c);
        try
        {
            yield return rig.Begin();
            if (!rig.Ready)
            {
                c.Report();
                yield break;
            }
            var bench = rig.Station(BenchPrefab, 0f, 3f);
            var items = bench != null ? rig.GiveOf(bench, 4, BenchGear) : null;
            if (items == null)
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
            yield return rig.Open(bench);
            if (!rig.At(bench))
            {
                c.Report();
                yield break;
            }
            var wear = new[] { 0.1f, 0.4f, 0.6f, 0.85f };
            var notes = new List<string>();
            // From level 10 (no level up on the way) and from a new character's level 0 (level ups on the way).
            foreach (var start in new[] { 10f, 0f })
            {
                // A: one click with the mod.
                rig.SetSkill(start, 0f);
                for (var i = 0; i < items.Count; i++)
                {
                    rig.Damage(items[i], wear[i]);
                }
                rig.Gui.UpdateRepair();
                rig.Mark(null);
                c.Check(rig.Click() && items.All(Full), $"from level {F(start)}: one click repairs the 4 items ({Durabilities(items)})");
                var clickLevel = rig.Skill.m_level;
                var clickBar = rig.Skill.m_accumulator;
                // What the Skills window draws for this skill: the level number and this fill of the bar under it.
                var clickFill = rig.Skill.GetLevelPercentage();

                // B: same wear, the vanilla repair called once per item (what 4 vanilla clicks do).
                rig.SetSkill(start, 0f);
                for (var i = 0; i < items.Count; i++)
                {
                    rig.Damage(items[i], wear[i]);
                }
                for (var i = 0; i < items.Count; i++)
                {
                    rig.Gui.RepairOneItem();
                }
                c.Check(items.All(Full), $"from level {F(start)}: 4 vanilla repairs fix the 4 items ({Durabilities(items)})");
                var vanillaLevel = rig.Skill.m_level;
                var vanillaBar = rig.Skill.m_accumulator;
                var vanillaFill = rig.Skill.GetLevelPercentage();

                c.Check(clickLevel == vanillaLevel && Mathf.Abs(clickBar - vanillaBar) < 1e-4f,
                    $"from level {F(start)}: Crafting after one click is level {F(clickLevel)} + {F(clickBar)}, after 4 vanilla repairs level {F(vanillaLevel)} + {F(vanillaBar)}");
                c.Check(Mathf.Abs(clickFill - vanillaFill) < 1e-4f,
                    $"from level {F(start)}: the Crafting bar of the Skills window is filled {F(clickFill * 100f)}% after one click, {F(vanillaFill * 100f)}% after 4 vanilla repairs");
                // Each run starts with an empty bar (progress 0): grew = a higher level or some fill.
                c.Check(clickLevel > start || clickFill > 0f, $"from level {F(start)}: the Crafting bar grew (level {F(clickLevel)}, bar {F(clickFill * 100f)}% full, was level {F(start)} and empty)");
                notes.Add($"from {F(start)}: level {F(clickLevel)}, bar {F(clickFill * 100f)}%");
            }
            rig.CheckIdle(bench, bench.m_repairItemDoneEffects, "after the clicks");
            c.Report($"; {string.Join(", ", notes.ToArray())}");
        }
        finally
        {
            rig.End();
        }
    }

    // ---------- repair.upgrade-tab (T09) ----------

    private static GameObject RowOf(InventoryGui gui, ItemDrop.ItemData item)
    {
        foreach (var row in gui.m_availableRecipes)
        {
            if (ReferenceEquals(row.ItemData, item))
            {
                return row.InterfaceElement;
            }
        }
        return null;
    }

    // "bar" = durability bar shown on the item's row, "no bar" = row there without it, "no row" = not listed.
    private static string BarOf(InventoryGui gui, ItemDrop.ItemData item)
    {
        var row = RowOf(gui, item);
        if (row == null)
        {
            return "no row";
        }
        var bar = row.transform.Find("Durability");
        return bar != null && bar.gameObject.activeSelf ? "bar" : "no bar";
    }

    private static IEnumerator RunUpgradeTab()
    {
        var c = new Checks(UpgradeTabName);
        var rig = new Rig(UpgradeTabName, c);
        try
        {
            yield return rig.Begin();
            if (!rig.Ready)
            {
                c.Report();
                yield break;
            }
            var bench = rig.Station(BenchPrefab, 0f, 3f);
            // Axe last (TESTING T09): vanilla repairs the first one, so the axe is fixed by one of the mod's extra presses.
            // Upgrade tab lists only items that can still be upgraded.
            var items = bench != null
                ? rig.GivePicked(3, $"upgradable level 1 items of {bench.m_name}", item => RecipeProblem(item, bench, 1) == null && item.m_shared.m_maxQuality > 1,
                    new[] { "Club", "ShieldWood", "AxeFlint" }.Concat(BenchGear).Distinct())
                : null;
            if (items == null)
            {
                c.Report();
                yield break;
            }
            var axe = items[2];
            c.Check(items.All(i => i.m_shared.m_maxQuality > 1 && i.m_quality == 1), "setup: every item can still be upgraded (the Upgrade tab lists only those)");
            // The Upgrade tab lists items whose recipe the character knows (put back by the rig).
            foreach (var item in items)
            {
                rig.P.m_knownRecipes.Add(item.m_shared.m_name);
            }
            yield return new WaitForSeconds(0.5f);
            rig.DamageAll(items);
            yield return rig.Open(bench);
            if (!rig.At(bench))
            {
                c.Report();
                yield break;
            }
            rig.Gui.OnTabUpgradePressed();
            yield return null;
            yield return null;
            c.Check(rig.Gui.InUpradeTab(), "the Upgrade tab is open");
            var order = rig.WornOf(items);
            c.Check(order.Count == 3 && ReferenceEquals(order[2], axe), $"setup: {PrefabName(axe)} is the last of the 3 worn items ({Names(order)})");
            c.Check(items.All(i => BarOf(rig.Gui, i) == "bar"),
                $"before: each damaged item has a durability bar in the Upgrade list ({string.Join(", ", items.Select(i => BarOf(rig.Gui, i)).ToArray())})");
            SelfTest.Screenshot(UpgradeTabName, "before");
            yield return null;
            yield return null;

            var effects = bench.m_repairItemDoneEffects;
            rig.Mark(effects);
            c.Check(rig.Click(), "the repair button takes the click");
            c.Check(items.All(Full), $"all 3 items are repaired: {Durabilities(items)}");
            c.Check(rig.Gui.InUpradeTab(), "still on the Upgrade tab");
            c.Check(BarOf(rig.Gui, axe) == "no bar", $"right after the click (same frame) the row of the last item ({PrefabName(axe)}) has no durability bar ({BarOf(rig.Gui, axe)})");
            c.Check(items.All(i => BarOf(rig.Gui, i) == "no bar"),
                $"right after the click no repaired item shows a bar ({string.Join(", ", items.Select(i => BarOf(rig.Gui, i)).ToArray())})");
            rig.CheckIdle(bench, effects, "after the click");
            SelfTest.Screenshot(UpgradeTabName, "after-click");
            yield return null;
            yield return null;
            c.Check(rig.Gui.InUpradeTab() && items.All(i => BarOf(rig.Gui, i) == "no bar"), "two frames later, tab untouched: still no bar");
            c.Report();
        }
        finally
        {
            rig.End();
        }
    }

    // ---------- repair.pad (T10) ----------

    private static IEnumerator RunPad()
    {
        var c = new Checks(PadName);
        var rig = new Rig(PadName, c);
        try
        {
            yield return rig.Begin();
            if (!rig.Ready)
            {
                c.Report();
                yield break;
            }
            var bench = rig.Station(BenchPrefab, 0f, 3f);
            var items = bench != null ? rig.GiveOf(bench, 3, BenchGear) : null;
            if (items == null)
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
            yield return rig.Open(bench);
            if (!rig.At(bench))
            {
                c.Report();
                yield break;
            }
            var button = rig.Gui.m_repairButton;
            var pad = button.GetComponent<UIGamePad>();
            c.Note(pad != null
                ? $"the repair button has a pad component: button '{pad.m_zinputKey}', key {pad.m_keyCode}, hint object {(pad.m_hint != null ? pad.m_hint.name : "none")}"
                : "the repair button has no pad component (UIGamePad): a pad reaches it by selecting it");

            // 1. Button.OnSubmit: what the pad component calls, and what "A" on the selected button calls.
            rig.DamageAll(items);
            rig.Gui.UpdateRepair();
            var order = rig.WornOf(items);
            var effects = bench.m_repairItemDoneEffects;
            var tape = rig.Mark(effects);
            c.Check(button.IsActive() && button.IsInteractable(), $"the button accepts a submit (active {button.IsActive()}, interactable {button.IsInteractable()})");
            button.OnSubmit(null);
            c.Check(items.All(Full), $"submit on the button: all 3 items repaired in one press ({Durabilities(items)})");
            c.Check(rig.CenterText == RepairedText(order) && rig.CenterShown(tape) == 2 && _watchCalls == 1,
                $"submit on the button: one summary, one effect ('{rig.CenterText}', {rig.CenterShown(tape)} messages, {_watchCalls} effect call(s))");
            rig.CheckIdle(bench, effects, "after the submit");

            // 2. The pad component's own Update, with its "button down" answer forced once (no controller here).
            if (pad != null)
            {
                rig.Patch(AccessTools.Method(typeof(UIGamePad), nameof(UIGamePad.ButtonPressed)), postfix: nameof(PadPress));
                yield return null;
                yield return null;
                rig.DamageAll(items);
                rig.Gui.UpdateRepair();
                order = rig.WornOf(items);
                _padTarget = pad;
                _padFired = 0;
                _padArmed = true;
                for (var i = 0; i < 30 && _padFired == 0; i++)
                {
                    yield return null;
                }
                _padArmed = false;
                c.Check(_padFired == 1, $"the pad component asked for its button while the window was open ({_padFired} time(s); it only asks when its button group is active)");
                c.Check(items.All(Full), $"pad press through the pad component: all 3 items repaired in one press ({Durabilities(items)})");
                c.Check(rig.CenterText == RepairedText(order), $"pad press: message '{rig.CenterText}', expected '{RepairedText(order)}'");
                rig.CheckIdle(bench, effects, "after the pad press");
            }
            c.Report(pad != null ? $"; pad button '{pad.m_zinputKey}'" : "; no pad component on the button");
        }
        finally
        {
            rig.End();
        }
    }

    // ---------- repair.toggle (T11) ----------

    private static IEnumerator RunToggle()
    {
        var c = new Checks(ToggleName);
        var rig = new Rig(ToggleName, c);
        try
        {
            yield return rig.Begin();
            if (!rig.Ready)
            {
                c.Report();
                yield break;
            }
            var bench = rig.Station(BenchPrefab, 0f, 3f);
            var items = bench != null ? rig.GiveOf(bench, 3, BenchGear) : null;
            if (items == null)
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
            yield return rig.Open(bench);
            if (!rig.At(bench))
            {
                c.Report();
                yield break;
            }
            var pressed = AccessTools.Method(typeof(InventoryGui), nameof(InventoryGui.OnRepairPressed));
            var shown = AccessTools.Method(typeof(MessageHud), nameof(MessageHud.ShowMessage));
            var effects = bench.m_repairItemDoneEffects;
            c.Check(Owned(pressed) && Owned(shown) && FeatureRegistry.Find(ModInfo.Guid) is FeatureView live && live.IsActive,
                "on at the start: the feature is active and its patches are on the repair button and the message HUD");
            try
            {
                // Off, the way the framework turns any feature off (patches removed). Not the Enabled setting: a test
                // never writes the player's config file.
                Plugin.TestBlocker = "Inactive: turned off by the repair.toggle self-test.";
                FeatureRegistry.RefreshAll();
                var view = FeatureRegistry.Find(ModInfo.Guid);
                c.Check(view.HasValue && !view.Value.IsActive, $"off: the framework shows the feature as not active ({view?.State}: {view?.Status})");
                c.Check(!Owned(pressed) && !Owned(shown), "off: the mod's patches are gone from the repair button and the message HUD");
                yield return null;
                yield return null;
                c.Check(FeatureRegistry.Find(ModInfo.Guid) is FeatureView still && !still.IsActive && !Owned(pressed), "off: still off two frames later");

                rig.DamageAll(items);
                rig.Gui.UpdateRepair();
                var order = rig.WornOf(items);
                var durability = order.Select(i => i.m_durability).ToArray();
                var tape = rig.Mark(effects);
                c.Check(rig.Click(), "off: the repair button takes the click");
                c.Check(Full(order[0]) && order[1].m_durability == durability[1] && order[2].m_durability == durability[2],
                    $"off: the first click repairs one item only, like vanilla ({Durabilities(order)})");
                c.Check(rig.CenterText == RepairedText(new[] { order[0] }) && rig.CenterShown(tape) == 1 && _watchCalls == 1,
                    $"off: the vanilla message and one effect ('{rig.CenterText}', {rig.CenterShown(tape)} message(s), {_watchCalls} effect call(s))");
                rig.Gui.UpdateRepair();
                c.Check(rig.ButtonUsable, $"off: the button stays usable for the next item ({rig.ButtonState})");
                tape = rig.Mark(effects);
                c.Check(rig.Click() && Full(order[1]) && order[2].m_durability == durability[2],
                    $"off: the second click repairs the second item only ({Durabilities(order)})");
                c.Check(rig.CenterText == RepairedText(new[] { order[1] }), $"off: second message '{rig.CenterText}'");

                // On again, no restart.
                Plugin.TestBlocker = null;
                FeatureRegistry.RefreshAll();
                view = FeatureRegistry.Find(ModInfo.Guid);
                c.Check(view.HasValue && view.Value.IsActive && Owned(pressed) && Owned(shown), $"on again: active, patches back ({view?.State}: {view?.Status})");
                yield return null;
                yield return null;
                rig.DamageAll(items);
                rig.Gui.UpdateRepair();
                order = rig.WornOf(items);
                tape = rig.Mark(effects);
                c.Check(rig.Click(), "on again: the repair button takes the click");
                c.Check(items.All(Full), $"on again: one click repairs all 3 items, no restart ({Durabilities(items)})");
                c.Check(rig.CenterText == RepairedText(order) && rig.CenterShown(tape) == 2 && _watchCalls == 1,
                    $"on again: one summary and one effect ('{rig.CenterText}', {rig.CenterShown(tape)} messages, {_watchCalls} effect call(s))");
                rig.CheckIdle(bench, effects, "on again");
            }
            finally
            {
                Plugin.TestBlocker = null;
                FeatureRegistry.RefreshAll();
            }
            c.Report();
        }
        finally
        {
            rig.End();
        }
    }

    // ---------- repair.crossbow (T13) ----------

    private static IEnumerator RunCrossbow()
    {
        var c = new Checks(CrossbowName);
        var rig = new Rig(CrossbowName, c);
        try
        {
            yield return rig.Begin();
            if (!rig.Ready)
            {
                c.Report();
                yield break;
            }
            if (!c.Check(FeatureRegistry.Find(CrossbowGuid) is FeatureView other && other.IsActive,
                    "Crossbow Stays Loaded is not installed or not active: nothing checked"))
            {
                c.Report();
                yield break;
            }
            // Two items first, crossbow last: an extra press of the mod is the one that repairs the crossbow.
            var first = rig.GivePicked(2, "repairable items", Repairable, new[] { "Club" }.Concat(BenchGear).Distinct());
            var crossbow = rig.Give("CrossbowArbalest");
            if (first == null || crossbow == null)
            {
                c.Report();
                yield break;
            }
            c.Check(crossbow.m_shared.m_attack != null && crossbow.m_shared.m_attack.m_requiresReload && crossbow.m_shared.m_canBeReparied,
                "setup: the arbalest needs reloading and can be repaired");
            var loadTime = crossbow.GetWeaponLoadingTime();

            // Loaded and damaged: wear it, hold it, finish the reload the vanilla way (the call the reload ends with).
            // Crossbow Stays Loaded stamps the item there, and again when it is put away.
            rig.Damage(crossbow, 0.5f);
            rig.P.EquipItem(crossbow, false);
            c.Check(crossbow.m_equipped, "setup: the crossbow is held");
            rig.P.SetWeaponLoaded(crossbow);
            if (!c.Check(crossbow.m_customData.ContainsKey(CrossbowStamp), "setup: Crossbow Stays Loaded marked the loaded crossbow (is it set to keep crossbows?)"))
            {
                c.Report();
                yield break;
            }
            rig.P.UnequipItem(crossbow, false);
            c.Check(!crossbow.m_equipped && rig.P.m_weaponLoaded == null && crossbow.m_customData.ContainsKey(CrossbowStamp),
                "setup: the crossbow is back in the bag, still marked as loaded");
            var stampBefore = crossbow.m_customData[CrossbowStamp];
            rig.DamageAll(first);

            // nocost, no station: repairs anywhere, so the crossbow's own station does not matter (TESTING T13 allows it).
            rig.P.SetNoPlacementCost(true);
            yield return rig.OpenPlain();
            var all = first.Concat(new[] { crossbow }).ToList();
            var order = rig.WornOf(all);
            c.Check(order.Count == 3 && ReferenceEquals(order[2], crossbow), $"setup: the crossbow is the last of the 3 worn items ({Names(order)})");
            var tape = rig.Mark(null);
            c.Check(rig.Click(), "the repair button takes the click");
            c.Check(all.All(Full), $"one click repairs the two items and the crossbow ({Durabilities(all)})");
            c.Check(rig.CenterText == RepairedText(order), $"message '{rig.CenterText}', expected '{RepairedText(order)}'");
            c.Note($"loaded mark before the repair '{stampBefore}', after '{(crossbow.m_customData.TryGetValue(CrossbowStamp, out var after) ? after : "gone")}'");
            rig.CheckIdle(null, null, "after the click");
            rig.P.SetNoPlacementCost(false);
            yield return rig.Close();

            // Hold it again: loaded at once, the game never starts a reload.
            rig.P.EquipItem(crossbow, false);
            c.Check(crossbow.m_equipped, "the repaired crossbow is held again");
            var reload = false;
            var ticks = 0;
            for (; ticks < 50; ticks++)
            {
                yield return new WaitForFixedUpdate();
                reload |= rig.P.IsReloadActionQueued();
            }
            c.Check(ReferenceEquals(rig.P.m_weaponLoaded, crossbow) && rig.P.IsWeaponLoaded(),
                $"after {ticks} game ticks the crossbow is loaded (a real reload takes {F(loadTime)} s)");
            c.Check(!reload, "no reload was started in those ticks");
            c.Report($"; reload time {F(loadTime)} s");
        }
        finally
        {
            rig.End();
        }
    }

    // ---------- repair.cost (C02) and repair.blocked (C03): another mod says no in the middle of a click ----------

    private static void PatchCostMod(Rig rig)
    {
        var press = AccessTools.Method(typeof(InventoryGui), nameof(InventoryGui.RepairOneItem));
        rig.Patch(press, prefix: nameof(PressBegin), postfix: nameof(PressEnd));
        rig.Patch(press, prefix: nameof(CostGate));
        rig.Patch(AccessTools.Method(typeof(InventoryGui), nameof(InventoryGui.CanRepair)), postfix: nameof(CostCheck));
    }

    private static void CostOff()
    {
        _gateAllow = -1;
        _gateCalls = 0;
        _checkAllow = -1;
        _checkPaid = 0;
        _checkRefused = 0;
        _inPress = false;
        _presses = 0;
        _reason = null;
    }

    private static IEnumerator RunCost()
    {
        const string reason = "TEST not enough coins to repair";
        var c = new Checks(CostName);
        var rig = new Rig(CostName, c);
        try
        {
            yield return rig.Begin();
            if (!rig.Ready)
            {
                c.Report();
                yield break;
            }
            var bench = rig.Station(BenchPrefab, 0f, 3f);
            var items = bench != null ? rig.GiveOf(bench, 5, BenchGear) : null;
            if (items == null)
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
            yield return rig.Open(bench);
            if (!rig.At(bench))
            {
                c.Report();
                yield break;
            }
            PatchCostMod(rig);
            var effects = bench.m_repairItemDoneEffects;

            // Both styles pay for 3 repairs, then refuse with a message in the middle of the screen.
            foreach (var style in new[] { "repair refused", "item not repairable while repairing" })
            {
                var gate = style == "repair refused";
                CostOff();
                rig.DamageAll(items);
                rig.Gui.UpdateRepair();
                var order = rig.WornOf(items);
                var before = order.Select(i => i.m_durability).ToArray();
                if (!c.Check(order.Count == 5, $"setup ({style}): the 5 items are worn ({Durabilities(rig.Worn())})"))
                {
                    continue;
                }
                _reason = reason;
                if (gate)
                {
                    _gateAllow = 3;
                }
                else
                {
                    _checkAllow = 3;
                }
                var tape = rig.Mark(effects);
                var clicked = rig.Click();
                // Style 1 count at its gate (a refused repair never start), style 2 count started repairs.
                var asked = gate ? _gateCalls : _presses;
                var refusedItems = _checkRefused;
                var sounds = _repairSounds;
                CostOff();

                c.Check(clicked, $"{style}: the repair button takes the click");
                c.Check(Full(order[0]) && Full(order[1]) && Full(order[2]), $"{style}: the 3 paid items are repaired ({Durabilities(order)})");
                c.Check(order[3].m_durability == before[3] && order[4].m_durability == before[4], $"{style}: the other 2 keep their durability ({Durabilities(order)})");
                c.Check(asked == 4, $"{style}: the click ended at the first refusal: 3 repairs and 1 refused attempt, no more ({asked} attempts)");
                var want = RepairedText(order.Take(3).ToList());
                c.Check(rig.CenterText == want && want.IndexOf('+') < 0, $"{style}: the message lists the 3 repaired items, no '+N': '{rig.CenterText}', expected '{want}'");
                c.Check(rig.CenterShown(tape) == 2, $"{style}: {rig.CenterShown(tape)} messages reached the middle of the screen (2 = the game's own, then the summary)");
                var topLeft = rig.TopLeftSince(tape);
                c.Check(topLeft.Count == 1 && topLeft[0] == reason, $"{style}: the refusal text shows top-left, once: '{string.Join("' / '", topLeft.ToArray())}'");
                c.Check(_watchCalls == 1 && sounds >= 1 && sounds == EnabledEffects(effects),
                    $"{style}: one repair sound: the station's repair effect played once ({_watchCalls}) and started {sounds} object(s), one vanilla repair starts {EnabledEffects(effects)}");
                rig.CheckIdle(bench, effects, style);
                rig.Gui.UpdateRepair();
                c.Check(rig.ButtonUsable && rig.Gui.HaveRepairableItems(), $"{style}: 2 items are still repairable, the button stays usable ({rig.ButtonState})");
                if (!gate)
                {
                    c.Note($"{style}: the fake mod refused {refusedItems} item(s) in the last attempt, then vanilla said '{BulkRepair.VanillaNothingLeft}' (muted, never shown top-left)");
                }
            }
            c.Report();
        }
        finally
        {
            CostOff();
            rig.End();
        }
    }

    private static IEnumerator RunBlocked()
    {
        var c = new Checks(BlockedName);
        var rig = new Rig(BlockedName, c);
        try
        {
            yield return rig.Begin();
            if (!rig.Ready)
            {
                c.Report();
                yield break;
            }
            var bench = rig.Station(BenchPrefab, 0f, 3f);
            var items = bench != null ? rig.GiveOf(bench, 4, BenchGear) : null;
            if (items == null)
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
            yield return rig.Open(bench);
            if (!rig.At(bench))
            {
                c.Report();
                yield break;
            }
            PatchCostMod(rig);
            var effects = bench.m_repairItemDoneEffects;

            // (a) A mod takes over every repair after the first and says nothing (RhythmicRepairs paces them itself).
            CostOff();
            rig.DamageAll(items);
            rig.Gui.UpdateRepair();
            var order = rig.WornOf(items);
            var before = order.Select(i => i.m_durability).ToArray();
            if (c.Check(order.Count == 4, $"setup (a): the 4 items are worn ({Durabilities(rig.Worn())})"))
            {
                _gateAllow = 1;
                var tape = rig.Mark(effects);
                var clicked = rig.Click();
                var asked = _gateCalls;
                CostOff();
                c.Check(clicked, "(a) the repair button takes the click");
                c.Check(Full(order[0]) && order.Skip(1).Select(i => i.m_durability).SequenceEqual(before.Skip(1)),
                    $"(a) only the first item is repaired, the rest keep their durability ({Durabilities(order)})");
                c.Check(asked == 2, $"(a) the mod tried one more repair, saw nothing change and stopped ({asked} attempts)");
                c.Check(rig.CenterText == RepairedText(new[] { order[0] }) && rig.CenterShown(tape) == 1,
                    $"(a) the game's own message stays, no summary: '{rig.CenterText}' x{rig.CenterShown(tape)}");
                c.Check(rig.TopLeftSince(tape).Count == 0, $"(a) no top-left message: '{string.Join("' / '", rig.TopLeftSince(tape).ToArray())}'");
                c.Check(_watchCalls == 1, $"(a) the station's repair effect played once ({_watchCalls})");
                rig.CheckIdle(bench, effects, "(a)");
            }

            // (b) A mod reports the items as not repairable after 2 repairs, without a word: vanilla then says
            // "No more item to repair" in the muted press. That line is never passed on.
            CostOff();
            rig.DamageAll(items);
            rig.Gui.UpdateRepair();
            order = rig.WornOf(items);
            before = order.Select(i => i.m_durability).ToArray();
            if (c.Check(order.Count == 4, $"setup (b): the 4 items are worn ({Durabilities(rig.Worn())})"))
            {
                _checkAllow = 2;
                var tape = rig.Mark(effects);
                var clicked = rig.Click();
                var refused = _checkRefused;
                var asked = _presses;
                CostOff();
                c.Check(clicked, "(b) the repair button takes the click");
                c.Check(Full(order[0]) && Full(order[1]) && order[2].m_durability == before[2] && order[3].m_durability == before[3],
                    $"(b) the first 2 items are repaired, the other 2 keep their durability ({Durabilities(order)})");
                c.Check(asked == 3 && refused == 2, $"(b) the click ended after one refused attempt: {asked} repairs started (2 done, 1 refused), the fake mod refused {refused} item(s) (the 2 left, once each)");
                var want = RepairedText(order.Take(2).ToList());
                c.Check(rig.CenterText == want && rig.CenterShown(tape) == 2, $"(b) the summary lists the 2 repaired items: '{rig.CenterText}', expected '{want}'");
                var topLeft = rig.TopLeftSince(tape);
                c.Check(topLeft.Count == 0, $"(b) nothing top-left, the game's '{BulkRepair.VanillaNothingLeft}' is not repeated there: '{string.Join("' / '", topLeft.ToArray())}'");
                rig.CheckIdle(bench, effects, "(b)");
            }
            c.Report();
        }
        finally
        {
            CostOff();
            rig.End();
        }
    }

    // ---------- repair.handoff / repair.mp.handoff (M02) ----------

    // The newest data of this network object went to the server (client only; the list holds what was sent to or
    // came from each peer, and this object was made here).
    private static bool SentToServer(ZDO zdo, uint revision)
    {
        foreach (var peer in ZDOMan.instance.m_peers)
        {
            if (peer.m_zdos.TryGetValue(zdo.m_uid, out var info) && info.m_dataRevision >= revision)
            {
                return true;
            }
        }
        return false;
    }

    private static IEnumerator RunHandoff(string name, bool multiplayer)
    {
        var c = new Checks(name);
        var rig = new Rig(name, c);
        Container chest = null;
        ItemDrop.ItemData inChest = null;
        try
        {
            if (multiplayer && !c.Check(ZNet.instance != null && !ZNet.instance.IsServer() && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected,
                    "this test needs a client joined to a dedicated server"))
            {
                c.Report();
                yield break;
            }
            yield return rig.Begin();
            if (!rig.Ready)
            {
                c.Report();
                yield break;
            }
            var bench = rig.Station(BenchPrefab, 0f, 3f);
            chest = rig.Chest(90f, 3f);
            var items = bench != null && chest != null ? rig.GiveOf(bench, 3, BenchGear) : null;
            if (items == null)
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
            var dataBefore = items.Select(DataOf).ToArray();
            rig.DamageAll(items);
            yield return rig.Open(bench);
            if (!rig.At(bench))
            {
                c.Report();
                yield break;
            }
            rig.Mark(bench.m_repairItemDoneEffects);
            c.Check(rig.Click() && items.All(Full), $"one click repairs the 3 items ({Durabilities(items)})");
            c.Check(items.Select(DataOf).SequenceEqual(dataBefore) && items.All(i => !HasOwnData(i)),
                $"the repair adds no data to the items ({string.Join(", ", items.Select(DataOf).ToArray())})");
            yield return rig.Close();

            // On the ground: the drop's network data is all another player gets. Out of auto pickup reach.
            var club = items[0];
            var back = -rig.P.transform.forward;
            back.y = 0f;
            var drop = ItemDrop.DropItem(club, 1, rig.P.transform.position + back.normalized * 8f + Vector3.up * 1.5f, Quaternion.identity);
            rig.Track(drop.gameObject);
            var dropZdo = drop.m_nview != null ? drop.m_nview.GetZDO() : null;
            var dropRevision = 0u;
            if (c.Check(dropZdo != null && dropZdo.IsOwner(), $"the dropped item ({PrefabName(club)}) is a network object of this game"))
            {
                dropRevision = dropZdo.DataRevision;
                var seen = club.m_dropPrefab.GetComponent<ItemDrop>().m_itemData.Clone();
                ItemDrop.LoadFromZDO(seen, dropZdo);
                c.Check(NearFull(seen), $"dropped item as another player loads it: durability {F(seen.m_durability)} of {F(seen.GetMaxDurability())} (the save keeps 2 decimals)");
                // Same data as before the repair (another mod may mark every item: not my business), nothing of mine.
                c.Check(DataOf(seen) == dataBefore[0] && !HasOwnData(seen) && seen.m_quality == 1 && seen.m_stack == 1,
                    $"dropped item: plain item, the data it had before the repair and nothing of this mod ({DataOf(seen)}, before {dataBefore[0]}, quality {seen.m_quality}, stack {seen.m_stack})");
            }

            // In a chest: same, through the chest's saved content.
            var axe = items[1];
            chest.GetInventory().MoveItemToThis(rig.Inv, axe);
            ZDO chestZdo = null;
            var chestRevision = 0u;
            if (c.Check(chest.GetInventory().ContainsItem(axe) && !rig.Inv.ContainsItem(axe), $"the second repaired item ({PrefabName(axe)}) moved into the chest"))
            {
                inChest = axe;
                chestZdo = chest.m_nview != null ? chest.m_nview.GetZDO() : null;
                var saved = chestZdo != null ? chestZdo.GetByteArray(ZDOVars.s_items) : null;
                if (c.Check(saved != null && saved.Length > 0, "the chest saved its content to its network object"))
                {
                    chestRevision = chestZdo.DataRevision;
                    var other = new Inventory("selftest", null, chest.m_width, chest.m_height);
                    other.Load(new ZPackage(saved));
                    var seen = other.GetAllItems().FirstOrDefault(i => i.m_shared.m_name == axe.m_shared.m_name);
                    c.Check(seen != null && other.GetAllItems().Count == 1, $"chest content as another player loads it: {other.GetAllItems().Count} item(s)");
                    if (seen != null)
                    {
                        c.Check(NearFull(seen), $"item from the chest: durability {F(seen.m_durability)} of {F(seen.GetMaxDurability())}");
                        c.Check(DataOf(seen) == dataBefore[1] && !HasOwnData(seen) && seen.m_quality == 1,
                            $"item from the chest: plain item, the data it had before the repair and nothing of this mod ({DataOf(seen)}, before {dataBefore[1]})");
                    }
                }
            }

            if (multiplayer && dropZdo != null && chestZdo != null)
            {
                // The server is who hands these objects to any other player: wait until both went out.
                var until = Time.realtimeSinceStartup + 20f;
                while (Time.realtimeSinceStartup < until && !(SentToServer(dropZdo, dropRevision) && SentToServer(chestZdo, chestRevision)))
                {
                    yield return null;
                }
                c.Check(SentToServer(dropZdo, dropRevision), "the dropped item's data (full durability) was sent to the server");
                c.Check(SentToServer(chestZdo, chestRevision), "the chest's content (the repaired item) was sent to the server");
                var again = club.m_dropPrefab.GetComponent<ItemDrop>().m_itemData.Clone();
                ItemDrop.LoadFromZDO(again, dropZdo);
                c.Check(NearFull(again), $"the drop still holds full durability after the send ({F(again.m_durability)})");
                c.Check(ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected, "still connected to the server");
            }
            c.Report(multiplayer ? "; a real second player picking the items up is not part of this test" : "");
        }
        finally
        {
            // Empty chest before it goes (a chest never spills, but its content would be lost data on the server).
            try
            {
                if (chest != null && inChest != null && chest.GetInventory().ContainsItem(inChest))
                {
                    chest.GetInventory().RemoveItem(inChest);
                }
            }
            catch (Exception e)
            {
                SelfTest.Note(name, $"clean-up of the chest failed: {e.Message}");
            }
            rig.End();
        }
    }

    // ---------- repair.mp.bulk / repair.mp.vanilla-server (M01) ----------

    // Only ever runs on a server that loads this mod. A dedicated server does not (client mod): the probe answers
    // "no step" instead, which is what the client test wants to see.
    private static IEnumerator ServerHasMod(string arg, object[] reply)
    {
        yield return null;
        SelfTest.Answer(reply, true, $"{ModInfo.Guid} {ModInfo.Version} runs on the server");
    }

    private static IEnumerator RunMpBulk(string name)
    {
        var c = new Checks(name);
        var rig = new Rig(name, c);
        try
        {
            if (!c.Check(ZNet.instance != null && !ZNet.instance.IsServer() && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected,
                    "this test needs a client joined to a dedicated server"))
            {
                c.Report();
                yield break;
            }
            yield return rig.Begin();
            if (!rig.Ready)
            {
                c.Report();
                yield break;
            }
            var bench = rig.Station(BenchPrefab, 0f, 3f);
            var items = bench != null ? rig.GiveOf(bench, 4, BenchGear) : null;
            if (items == null)
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
            yield return rig.Open(bench);
            if (!rig.At(bench))
            {
                c.Report();
                yield break;
            }
            rig.Patch(AccessTools.Method(typeof(ZRoutedRpc), nameof(ZRoutedRpc.InvokeRoutedRPC), new[] { typeof(long), typeof(ZDOID), typeof(string), typeof(object[]) }),
                prefix: nameof(CountRpc));
            var effects = bench.m_repairItemDoneEffects;
            var objects = ZDOMan.instance.m_objectsByID;

            // Baseline: one damaged item. The mod stays out, so this click is the vanilla click.
            rig.Damage(items[0], 0.5f);
            rig.Gui.UpdateRepair();
            c.Check(rig.Worn().Count == 1, $"setup: one worn item for the vanilla click ({Durabilities(rig.Worn())})");
            var count = objects.Count;
            var tape = rig.Mark(effects);
            c.Check(rig.Click() && Full(items[0]), "vanilla click: the one damaged item is repaired");
            var oneObjects = objects.Count - count;
            var oneRpcs = _rpcCalls;
            var oneEffects = _watchCalls;

            // One click, 4 items.
            rig.DamageAll(items);
            rig.Gui.UpdateRepair();
            var order = rig.WornOf(items);
            count = objects.Count;
            tape = rig.Mark(effects);
            c.Check(rig.Click(), "the repair button takes the click");
            var allObjects = objects.Count - count;
            var allRpcs = _rpcCalls;
            c.Check(items.All(Full), $"on the server, one click repairs all 4 items ({Durabilities(items)})");
            c.Check(rig.CenterText == RepairedText(order) && rig.CenterShown(tape) == 2, $"one summary: '{rig.CenterText}', expected '{RepairedText(order)}'");
            c.Check(_watchCalls == 1 && oneEffects == 1, $"the station's repair effect plays once, as for one vanilla repair ({_watchCalls}, vanilla click {oneEffects})");
            c.Check(allObjects == oneObjects, $"network objects made by the 4-item click: {allObjects}, by the vanilla 1-item click: {oneObjects} (what other players can see or hear)");
            c.Check(allRpcs == oneRpcs, $"network calls made by the 4-item click: {allRpcs}, by the vanilla 1-item click: {oneRpcs}");
            rig.CheckIdle(bench, effects, "after the click");
            c.Check(items.All(i => !HasOwnData(i)), "the mod stores nothing on the repaired items");
            yield return null;
            yield return null;
            c.Check(ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected && Player.m_localPlayer != null, "still connected to the server after the repair");
            yield return rig.Close();

            // Last (a dead server probe would eat the test's time): the server runs no copy of this mod.
            var reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(ServerStep, "", reply);
            c.Check(reply.Answered && !reply.Ok && reply.Detail.IndexOf("no step", StringComparison.OrdinalIgnoreCase) >= 0,
                $"the dedicated server does not run this mod (server probe: {reply})");
            c.Report($"; scenario '{SelfTest.Scenario}', {oneObjects} network object(s) and {oneRpcs} network call(s) per click");
        }
        finally
        {
            rig.End();
        }
    }

    // ---------- repair.clean (T14) ----------

    private static IEnumerator RunClean()
    {
        var c = new Checks(CleanName);
        yield return null;
        var errors = ErrorWatch.Count(out var first);
        c.Check(ErrorWatch.Installed, "the error watch was started with the mod");
        c.Check(errors == 0, $"{errors} error line(s) from {ModInfo.Name} or naming it or MC.Crafting since the mod went on, first: {first}");
        c.Check(!BulkRepair.Muting && BulkRepair.LastMuted == null, "the mod left its message muting on");
        c.Report($"; log watched for {F(Time.realtimeSinceStartup - ErrorWatch.Since)} s since the mod went on");
    }
}
#endif
