#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MC.Crafting.ForgeIdolUpgradesMod;

// Debug build only. Me = tools every self test share: log watch (what this mod say in log), default rules (test items
// say 35 / 55 / 75 / 95 % and 5 silver: test must not care what tester's config say), test rig (items, Forge, chest,
// clean up), mod off and on through real framework path, hand into other MC mods (compatibility tests).
internal static partial class SelfTests
{
    // ---------- log watch ----------

    private sealed class LogLine
    {
        internal LogLevel Level;
        internal string Text;
        internal bool Provoked; // logged while a test asked for trouble on purpose (typo rules...)
    }

    // Me hear every line of this mod's own log source, whole session (also Debug level: listener get all levels),
    // and error lines of other sources (Unity) that name this mod's code in their stack.
    private sealed class LogWatch : ILogListener
    {
        private const string ModCodeFrames = "MC.Crafting.ForgeIdolUpgradesMod.";
        private const string TestCodeFrames = "MC.Crafting.ForgeIdolUpgradesMod.SelfTests";

        private static LogWatch _instance;
        private static readonly List<LogLine> Lines = new List<LogLine>();

        // > 0 while a test make warnings on purpose: those lines no count as "trouble with default settings".
        internal static int Provoking;

        internal static bool Installed => _instance != null;

        internal static void Install()
        {
            if (_instance == null)
            {
                _instance = new LogWatch();
                BepInEx.Logging.Logger.Listeners.Add(_instance);
            }
        }

        internal static int Mark()
        {
            lock (Lines)
            {
                return Lines.Count;
            }
        }

        internal static List<LogLine> Since(int mark)
        {
            lock (Lines)
            {
                return Lines.Skip(Mathf.Max(0, mark)).ToList();
            }
        }

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            try
            {
                if (eventArgs == null || eventArgs.Source == null)
                {
                    return;
                }
                var text = eventArgs.Data as string ?? eventArgs.Data?.ToString() ?? "";
                if (eventArgs.Source.SourceName != ModInfo.Name)
                {
                    // Other log source (Unity): me keep only error lines whose stack go through this mod's code
                    // (exception nobody caught). Stack with test code only = test's own trouble, probe report it.
                    if ((eventArgs.Level & (LogLevel.Error | LogLevel.Fatal)) == 0
                        || text.Replace(TestCodeFrames, "").IndexOf(ModCodeFrames, StringComparison.Ordinal) < 0)
                    {
                        return;
                    }
                }
                lock (Lines)
                {
                    if (Lines.Count < 50000)
                    {
                        Lines.Add(new LogLine { Level = eventArgs.Level, Text = text, Provoked = Provoking > 0 });
                    }
                }
            }
            catch (Exception)
            {
                // Me never throw into the logger.
            }
        }

        public void Dispose()
        {
        }
    }

    // Line this mod logged since mark, at this level, with every part in it. Null = none.
    private static string Logged(int mark, LogLevel level, params string[] parts)
    {
        foreach (var line in LogWatch.Since(mark))
        {
            if ((line.Level & level) != 0 && parts.All(p => line.Text.IndexOf(p, StringComparison.Ordinal) >= 0))
            {
                return line.Text;
            }
        }
        return null;
    }

    private static string Tail(int mark) =>
        string.Join(" | ", LogWatch.Since(mark).Select(l => $"[{l.Level}] {l.Text}").ToArray());

    // ---------- small helpers ----------

    private static string L(string text) => Localization.instance.Localize(text);

    private static bool English => Localization.instance.GetSelectedLanguage() == "English";

    private static string NameOf(string prefab) => Prefab(prefab)?.m_itemData.m_shared.m_name;

    private static Sprite PlainIconOf(string prefab)
    {
        var icons = Prefab(prefab)?.m_itemData.m_shared.m_icons;
        return icons != null && icons.Length > 0 ? icons[0] : null;
    }

    private static bool Near(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f && Mathf.Abs(a.a - b.a) < 0.02f;

    // Rules = config defaults, whatever tester's config file say (ConfigEntry.DefaultValue: me only read).
    private static ForgeRules DefaultRules()
    {
        var r = new ForgeRules();
        for (var level = 0; level <= IdolLevels.Max; level++)
        {
            r.Chance[level] = (int)Plugin.Chance[level].DefaultValue;
        }
        r.Failure = (FailureMode)Plugin.Failure.DefaultValue;
        r.LevelsLost = (int)Plugin.LevelsLost.DefaultValue;
        r.TierByLevel = (bool)Plugin.TierByLevel.DefaultValue;
        r.BaseLevels = (int)Plugin.BaseLevels.DefaultValue;
        r.LevelsPerTier = (int)Plugin.LevelsPerTier.DefaultValue;
        for (var level = 1; level <= IdolLevels.Max; level++)
        {
            r.Material[level] = (int)Plugin.MaterialCost[level].DefaultValue;
            r.Trophies[level] = (int)Plugin.TrophyCost[level].DefaultValue;
        }
        for (var t = 0; t < IdolTierDefaults.Count; t++)
        {
            r.TierMaterial[t] = (string)Plugin.Tiers[t].Material.DefaultValue;
            r.TierCommon[t] = (string)Plugin.Tiers[t].Base.DefaultValue;
            r.TierElite[t] = (string)Plugin.Tiers[t].Elite.DefaultValue;
            r.TierBoss[t] = (string)Plugin.Tiers[t].Boss.DefaultValue;
        }
        return r;
    }

    private static ForgeRules DefaultRules(Action<ForgeRules> change)
    {
        var r = DefaultRules();
        change(r);
        return r;
    }

    // Mod off / on like player's toggle (framework path). Tests stay registered through it.
    private static bool SetModOff(bool off)
    {
        _toggling = true;
        try
        {
            return Plugin.TestSetOff(off);
        }
        finally
        {
            _toggling = false;
        }
    }

    private static bool OursPatch(Type type, string method)
    {
        var info = HarmonyLib.Harmony.GetPatchInfo(HarmonyLib.AccessTools.Method(type, method));
        return info != null && info.Owners.Contains(ModInfo.Guid);
    }

    private static IEnumerator Frames(int n)
    {
        for (var i = 0; i < n; i++)
        {
            yield return null;
        }
    }

    // Recipe of item prefab, and prefab name of idol it ask for (vanilla data).
    private static Recipe RecipeOf(string prefab)
    {
        var drop = Prefab(prefab);
        return drop != null ? ObjectDB.instance.GetRecipe(drop.m_itemData) : null;
    }

    private static string OwnIdolOf(string prefab)
    {
        var recipe = RecipeOf(prefab);
        var req = recipe != null && recipe.m_resources != null
            ? recipe.m_resources.FirstOrDefault(q => q != null && q.m_upgraderResource && q.m_resItem != null)
            : null;
        return req != null ? req.m_resItem.gameObject.name : null;
    }

    // ---------- other MC mods (compatibility tests) ----------

    private sealed class OtherMod
    {
        internal readonly string Guid;
        internal readonly Assembly Assembly;
        internal readonly bool Active;
        internal readonly string State;

        internal OtherMod(string guid)
        {
            Guid = guid;
            if (Chainloader.PluginInfos.TryGetValue(guid, out var info) && info.Instance != null)
            {
                Assembly = info.Instance.GetType().Assembly;
            }
            var view = FeatureRegistry.Find(guid);
            Active = view != null && view.Value.IsActive;
            State = Assembly == null ? "not installed" : view == null ? "not registered" : view.Value.State;
        }

        internal Type Type(string name) => Assembly?.GetType(name, false);
    }

    private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static object GetStatic(Type type, string member)
    {
        if (type == null)
        {
            return null;
        }
        var field = type.GetField(member, AnyStatic);
        if (field != null)
        {
            return field.GetValue(null);
        }
        var property = type.GetProperty(member, AnyStatic);
        return property != null ? property.GetValue(null, null) : null;
    }

    private static MethodInfo Method(Type type, string name, params Type[] args) =>
        type?.GetMethod(name, AnyStatic, null, args, null);

    // ---------- test rig ----------

    // Me = one test's world: player, items me give (inventory put back: me remove every extra), recipes and materials
    // player learn (forgotten again), skills, Forge, chests, drops, test overrides. Call Dispose in finally.
    private sealed class Rig
    {
        internal readonly Checks C;
        internal readonly Forge Forge = new Forge();
        internal readonly Player P;
        internal readonly Inventory Inv;
        internal readonly InventoryGui Gui;
        internal int Row = -1;

        private readonly Dictionary<string, int> _had;
        private readonly HashSet<string> _recipes;
        private readonly HashSet<string> _materials;
        private readonly HashSet<string> _trophies;
        private readonly Dictionary<Skills.SkillType, Vector2> _skills = new Dictionary<Skills.SkillType, Vector2>();
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly bool _noCost;
        private int _provoked;

        internal Rig(string name)
        {
            C = new Checks(name);
            P = Player.m_localPlayer;
            Inv = P.GetInventory();
            Gui = InventoryGui.instance;
            _had = Count(Inv);
            _recipes = new HashSet<string>(P.m_knownRecipes);
            _materials = new HashSet<string>(P.m_knownMaterial);
            _trophies = new HashSet<string>(P.m_trophies);
            _noCost = P.m_noPlacementCost;
            // Refinement and repair raise crafting skill: me put level and progress back after.
            if (P.m_skills != null)
            {
                foreach (var pair in P.m_skills.m_skillData)
                {
                    _skills[pair.Key] = new Vector2(pair.Value.m_level, pair.Value.m_accumulator);
                }
            }
        }

        private static string Key(ItemDrop.ItemData item) => item.m_shared.m_name + "\n" + item.m_quality;

        private static Dictionary<string, int> Count(Inventory inventory)
        {
            var counts = new Dictionary<string, int>();
            foreach (var item in inventory.GetAllItems())
            {
                counts.TryGetValue(Key(item), out var n);
                counts[Key(item)] = n + item.m_stack;
            }
            return counts;
        }

        // Stack in inventory that hold what me just gave (null = prefab unknown or no room).
        internal ItemDrop.ItemData Give(string prefab, int amount = 1, int quality = 1)
        {
            var drop = Prefab(prefab);
            if (drop == null)
            {
                C.Check(false, $"item '{prefab}' is not in this game");
                return null;
            }
            var added = Inv.AddItem(prefab, amount, quality, 0, 0L, "", false);
            if (added != null && Inv.ContainsItem(added))
            {
                return added;
            }
            var name = drop.m_itemData.m_shared.m_name;
            var stack = Inv.GetAllItems().LastOrDefault(i => i.m_shared.m_name == name && i.m_quality == quality);
            C.Check(stack != null, $"could not give {amount} x {prefab} (quality {quality}): inventory full?");
            return stack;
        }

        internal ItemDrop.ItemData Find(string prefab, int quality)
        {
            var name = NameOf(prefab);
            return Inv.GetAllItems().FirstOrDefault(i => i.m_shared.m_name == name && i.m_quality == quality);
        }

        internal int Has(string prefab, int quality = -1)
        {
            var name = NameOf(prefab);
            return name != null ? Inv.CountItems(name, quality, false) : 0;
        }

        internal void Teach(params string[] prefabs)
        {
            foreach (var prefab in prefabs)
            {
                var name = NameOf(prefab);
                if (name != null)
                {
                    P.m_knownRecipes.Add(name);
                }
            }
        }

        // Me put a Club (stack size 1) in every empty slot. Give back how many went in.
        internal int Fill()
        {
            var n = 0;
            while (Inv.GetEmptySlots() > 0 && n < 400)
            {
                if (Inv.AddItem("Club", 1, 1, 0, 0L, "", false) == null)
                {
                    break;
                }
                n++;
            }
            return n;
        }

        internal void Unfill(int clubs)
        {
            var name = NameOf("Club");
            if (name != null && clubs > 0)
            {
                Inv.RemoveItem(name, clubs, -1, false);
            }
        }

        internal IEnumerator OpenForge()
        {
            yield return Forge.Open(Forge);
        }

        // Me close window like player do (Forge stay in world). ReopenForge open it at the Forge again.
        internal IEnumerator CloseWindow()
        {
            if (InventoryGui.IsVisible())
            {
                Gui.Hide();
            }
            yield return new WaitForSecondsRealtime(0.4f);
        }

        internal IEnumerator ReopenForge()
        {
            P.SetCraftingStation(Forge.Station);
            Gui.Show(null, 3);
            yield return new WaitForSecondsRealtime(0.6f);
        }

        internal IEnumerator OpenInventory(Container container = null)
        {
            Gui.Show(container);
            yield return new WaitForSecondsRealtime(0.6f);
        }

        // Me rebuild list, select row of this item (Row = -1: not listed), wait panel drawn.
        internal IEnumerator Select(ItemDrop.ItemData item)
        {
            Gui.UpdateCraftingPanel();
            yield return null;
            Row = item != null ? RowOf(Gui, item) : -1;
            if (Row >= 0)
            {
                Gui.SetRecipe(Row, false);
            }
            yield return null;
            yield return null;
        }

        internal IEnumerator ClickIdolsTab()
        {
            var tab = IdolsTabButton();
            if (tab != null)
            {
                tab.onClick.Invoke();
            }
            yield return null;
            yield return null;
        }

        internal Button IdolsTabButton()
        {
            var tab = Gui.m_tabCraft.transform.parent.Find("MC_IdolsTab");
            return tab != null ? tab.GetComponent<Button>() : null;
        }

        internal bool RowCanCraft => Row >= 0 && Row < Gui.m_availableRecipes.Count && Gui.m_availableRecipes[Row].CanCraft;

        internal GameObject RowElement => Row >= 0 && Row < Gui.m_availableRecipes.Count ? Gui.m_availableRecipes[Row].InterfaceElement : null;

        internal string ButtonLabel
        {
            get
            {
                var label = Gui.m_craftButton.GetComponentInChildren<TMP_Text>();
                return label != null ? label.text ?? "" : "";
            }
        }

        internal string ButtonTip => TipOf(Gui.m_craftButton.gameObject);

        internal bool ButtonOn => Gui.m_craftButton.interactable;

        internal string CraftText => Gui.m_itemCraftType.gameObject.activeSelf ? Gui.m_itemCraftType.text : "";

        internal string Description => Gui.m_recipeDecription.text ?? "";

        internal string SlotName(int i) => Slot(Gui, i, "res_name");

        internal string SlotAmount(int i) => Slot(Gui, i, "res_amount");

        internal string SlotTip(int i) => TipOf(Gui.m_recipeRequirementList[i]);

        internal Sprite SlotIcon(int i)
        {
            var child = Gui.m_recipeRequirementList[i].transform.Find("res_icon");
            var image = child != null ? child.GetComponent<Image>() : null;
            return image != null ? image.sprite : null;
        }

        internal Color SlotAmountColor(int i)
        {
            var child = Gui.m_recipeRequirementList[i].transform.Find("res_amount");
            var text = child != null ? child.GetComponent<TMP_Text>() : null;
            return text != null ? text.color : Color.clear;
        }

        private static string TipOf(GameObject go)
        {
            var tip = go != null ? go.GetComponent<UITooltip>() : null;
            return tip != null ? tip.m_text ?? "" : "";
        }

        internal string Center => MessageHud.instance != null ? MessageHud.instance.m_messageCenterText.text ?? "" : "";

        internal void ClearCenter()
        {
            if (MessageHud.instance != null)
            {
                MessageHud.instance.m_messageCenterText.text = "";
            }
        }

        // Me press craft button, then skip the 8-12 s timer (next frame run DoCrafting).
        internal IEnumerator Press()
        {
            yield return PressAndFinish(Gui);
        }

        internal InventoryElement Element(InventoryGrid grid, ItemDrop.ItemData item) =>
            item != null && grid != null && grid.m_inventory != null
                ? grid.GetElement(item.m_gridPos.x, item.m_gridPos.y, grid.m_inventory.GetWidth())
                : null;

        // Wooden chest 2.5 m to the right (support check off: never break and spill). Null = prefab missing.
        internal Container SpawnChest()
        {
            var prefab = ZNetScene.instance.GetPrefab("piece_chest_wood");
            if (prefab == null || prefab.GetComponent<Container>() == null)
            {
                C.Check(false, "piece_chest_wood is not in this game");
                return null;
            }
            var right = P.transform.right;
            right.y = 0f;
            right.Normalize();
            var pos = P.transform.position + right * 2.5f;
            pos.y = ZoneSystem.instance.GetGroundHeight(pos);
            var go = Object.Instantiate(prefab, pos, Quaternion.identity);
            _spawned.Add(go);
            var wnt = go.GetComponent<WearNTear>();
            if (wnt != null)
            {
                wnt.enabled = false;
            }
            return go.GetComponent<Container>();
        }

        // One item on ground near player. Auto pickup off: it stay there until test take it.
        internal ItemDrop Drop(string prefab, int quality, int amount = 1, float ahead = 3f)
        {
            var drop = Prefab(prefab);
            if (drop == null)
            {
                C.Check(false, $"item '{prefab}' is not in this game");
                return null;
            }
            var item = drop.m_itemData.Clone();
            item.m_quality = quality;
            item.m_dropPrefab = drop.gameObject;
            item.m_worldLevel = Game.m_worldLevel;
            var forward = P.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            var dropped = ItemDrop.DropItem(item, amount, P.transform.position + forward * ahead + Vector3.up, Quaternion.identity);
            if (dropped != null)
            {
                dropped.m_autoPickup = false;
                _spawned.Add(dropped.gameObject);
            }
            return dropped;
        }

        // From here test make warnings on purpose (typo rules): clean-log test must not count them.
        internal void Provoke()
        {
            _provoked++;
            LogWatch.Provoking++;
        }

        internal void Dispose()
        {
            ServerRules.TestRules = null;
            ForgeRefine.TestRoll = null;
            IdolChoice.TestPick = null;
            StarIcons.TestForceEmpty = false;
            IdolChoice.Clear();
            if (Plugin.TestIsOff)
            {
                SetModOff(false);
            }
            LogWatch.Provoking -= _provoked;
            _provoked = 0;
            // Idol table back on real rules now (not later, inside other test's log window).
            IdolCatalog.IdolAt(0, true);
            if (P == null)
            {
                return;
            }
            P.m_noPlacementCost = _noCost;
            Forge.Close();
            foreach (var go in _spawned)
            {
                if (go == null)
                {
                    continue;
                }
                var container = go.GetComponent<Container>();
                if (container != null && container.GetInventory() != null)
                {
                    container.GetInventory().RemoveAll();
                }
                ZNetScene.instance.Destroy(go);
            }
            _spawned.Clear();

            // Inventory: every extra go away (given items, clubs, refunds, items a test raised a level).
            var now = Count(Inv);
            foreach (var item in Inv.GetAllItems().ToList())
            {
                _had.TryGetValue(Key(item), out var had);
                if (now[Key(item)] > had && P.IsItemEquiped(item))
                {
                    P.UnequipItem(item, false);
                }
            }
            foreach (var pair in now)
            {
                _had.TryGetValue(pair.Key, out var had);
                if (pair.Value > had)
                {
                    var split = pair.Key.Split('\n');
                    Inv.RemoveItem(split[0], pair.Value - had, int.Parse(split[1]), false);
                }
            }
            Reset(P.m_knownRecipes, _recipes);
            Reset(P.m_knownMaterial, _materials);
            Reset(P.m_trophies, _trophies);
            if (P.m_skills != null)
            {
                foreach (var type in P.m_skills.m_skillData.Keys.ToList())
                {
                    if (_skills.TryGetValue(type, out var was))
                    {
                        P.m_skills.m_skillData[type].m_level = was.x;
                        P.m_skills.m_skillData[type].m_accumulator = was.y;
                    }
                    else
                    {
                        P.m_skills.m_skillData.Remove(type);
                    }
                }
            }
            // "New item / New recipe" popups of things me gave: nobody need them after test.
            if (MessageHud.instance != null)
            {
                MessageHud.instance.ClearUnlockQueue();
            }
        }

        private static void Reset(HashSet<string> live, HashSet<string> saved)
        {
            if (!live.SetEquals(saved))
            {
                live.Clear();
                live.UnionWith(saved);
            }
        }
    }
}
#endif
