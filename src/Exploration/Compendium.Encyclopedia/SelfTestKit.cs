#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using MC.Shared;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MC.Exploration.CompendiumEncyclopediaMod;

// Debug build only: tools the self tests share (SelfTests.More.cs, Ui/UiSelfTests.More.cs).
//   LogWatch   = me listen to our own log lines from plugin start: count Warning / Error lines (T24), count some marker
//                lines ("catalog built", "Met ...", "button placed"). Test that make a warning on purpose say so with
//                Expect, so the clean-log test never count it. Me also count Error lines of other loggers (Unity's
//                exception log) that name our namespace: an exception of ours nobody caught.
//   PlayerState / ProfileState = copy of everything a test may touch on the character (known sets, custom data,
//                skills, foods) and in the profile (every stat table), put back in finally.
//   TestKit    = catalog wait, report, fake pointer clicks (down, up, click like the input module), spawn helpers.
// Nothing here change what the mod do for players.

/// <summary>Me count our own Warning / Error lines and some marker lines since the plugin started.</summary>
internal sealed class LogWatch : ILogListener
{
    internal const string CatalogBuilt = "Encyclopedia catalog built in";
    internal const string ButtonPlaced = "Encyclopedia button placed";
    internal const string SidePanelDump = "Encyclopedia side panel layout";
    internal const string EscChanged = "InventoryGui.Update changed";
    internal const string CannotBuild = "Encyclopedia window cannot be built";
    internal const string GaveUp = "Another mod keeps moving";
    internal const string OutsidePanel = "Encyclopedia button is outside the side panel background";

    private static readonly string[] Markers = { CatalogBuilt, ButtonPlaced, SidePanelDump, EscChanged, CannotBuild, GaveUp };

    // Exception that escape our code never reach our own logger: Unity log it, with our namespace in the stack trace.
    private static readonly string OwnNamespace = typeof(LogWatch).Namespace + ".";
    private static readonly object Gate = new object();
    private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> Met = new Dictionary<string, int>(StringComparer.Ordinal);
    private static readonly List<string> Expected = new List<string>();
    private static readonly List<string> UnexpectedLines = new List<string>();
    private static LogWatch _instance;
    private static ManualLogSource _source;
    private static int _unexpected;
    private static int _bad;
    private static string _lastButtonPlaced = "";
    private static string _lastCatalogBuilt = "";

    internal static bool Installed => _instance != null;

    /// <summary>Last Info "Encyclopedia catalog built in ..." line (T05 read its counts). "" = none yet.</summary>
    internal static string LastCatalogBuilt
    {
        get
        {
            lock (Gate)
            {
                return _lastCatalogBuilt;
            }
        }
    }

    /// <summary>Plugin call me first thing (BindConfig). Twice = nothing.</summary>
    internal static void Install(ManualLogSource source)
    {
        if (_instance != null || source == null)
        {
            return;
        }
        _source = source;
        _instance = new LogWatch();
        BepInEx.Logging.Logger.Listeners.Add(_instance);
    }

    public void LogEvent(object sender, LogEventArgs eventArgs)
    {
        try
        {
            if (eventArgs == null)
            {
                return;
            }
            var text = eventArgs.Data != null ? eventArgs.Data.ToString() : "";
            if (text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal))
            {
                return; // test result lines (FAIL is an Error line) are not the mod talking
            }
            if (!ReferenceEquals(eventArgs.Source, _source))
            {
                // Not our logger. Only one kind count: an Error / Fatal line (Unity's exception log) whose text name
                // our namespace = an exception of ours nobody caught. Never asked for: always unexpected.
                if ((eventArgs.Level & (LogLevel.Error | LogLevel.Fatal)) == 0 || text.IndexOf(OwnNamespace, StringComparison.Ordinal) < 0)
                {
                    return;
                }
                lock (Gate)
                {
                    _bad++;
                    _unexpected++;
                    if (UnexpectedLines.Count < 8)
                    {
                        var name = eventArgs.Source != null ? eventArgs.Source.SourceName : "?";
                        UnexpectedLines.Add($"[{eventArgs.Level}: {name}] {(text.Length > 160 ? text.Substring(0, 160) + "..." : text)}");
                    }
                }
                return;
            }
            lock (Gate)
            {
                foreach (var m in Markers)
                {
                    if (text.IndexOf(m, StringComparison.Ordinal) >= 0)
                    {
                        Counts.TryGetValue(m, out var n);
                        Counts[m] = n + 1;
                        if (m == ButtonPlaced)
                        {
                            _lastButtonPlaced = text;
                        }
                        else if (m == CatalogBuilt)
                        {
                            _lastCatalogBuilt = text;
                        }
                    }
                }
                if (text.StartsWith("Met ", StringComparison.Ordinal))
                {
                    var end = text.IndexOf(" (its name plate", StringComparison.Ordinal);
                    if (end > 4)
                    {
                        var name = text.Substring(4, end - 4);
                        Met.TryGetValue(name, out var n);
                        Met[name] = n + 1;
                    }
                }
                if ((eventArgs.Level & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) == 0)
                {
                    return;
                }
                _bad++;
                foreach (var e in Expected)
                {
                    if (text.IndexOf(e, StringComparison.Ordinal) >= 0)
                    {
                        return;
                    }
                }
                _unexpected++;
                if (UnexpectedLines.Count < 8)
                {
                    UnexpectedLines.Add($"[{eventArgs.Level}] {(text.Length > 160 ? text.Substring(0, 160) + "..." : text)}");
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

    /// <summary>Lines with this marker text since plugin start.</summary>
    internal static int Count(string marker)
    {
        lock (Gate)
        {
            return Counts.TryGetValue(marker, out var n) ? n : 0;
        }
    }

    /// <summary>"Met name (its name plate was shown)." Debug lines for this creature name since plugin start.</summary>
    internal static int MetLines(string creatureName)
    {
        lock (Gate)
        {
            return creatureName != null && Met.TryGetValue(creatureName, out var n) ? n : 0;
        }
    }

    /// <summary>Warning / Error / Fatal lines of the mod no test asked for.</summary>
    internal static int Unexpected
    {
        get
        {
            lock (Gate)
            {
                return _unexpected;
            }
        }
    }

    /// <summary>Every Warning / Error / Fatal line of the mod, asked for or not.</summary>
    internal static int Bad
    {
        get
        {
            lock (Gate)
            {
                return _bad;
            }
        }
    }

    internal static string LastButtonPlaced
    {
        get
        {
            lock (Gate)
            {
                return _lastButtonPlaced;
            }
        }
    }

    internal static string UnexpectedText()
    {
        lock (Gate)
        {
            return string.Join(" | ", UnexpectedLines);
        }
    }

    /// <summary>Test make a warning with this text on purpose: not counted until the scope is disposed.</summary>
    internal static IDisposable Expect(string text) => new ExpectScope(text);

    private sealed class ExpectScope : IDisposable
    {
        private readonly string _text;
        private bool _done;

        internal ExpectScope(string text)
        {
            _text = text;
            lock (Gate)
            {
                Expected.Add(text);
            }
        }

        public void Dispose()
        {
            if (_done)
            {
                return;
            }
            _done = true;
            lock (Gate)
            {
                Expected.Remove(_text);
            }
        }
    }
}

/// <summary>Copy of what the character knows and carries in memory. Restore put every table back in place (same objects).</summary>
internal sealed class PlayerState
{
    private readonly Player _p;
    private readonly HashSet<string> _materials;
    private readonly HashSet<string> _recipes;
    private readonly HashSet<string> _trophies;
    private readonly HashSet<string> _uniques;
    private readonly HashSet<string> _biomes;
    private readonly HashSet<string> _tutorials;
    private readonly Dictionary<string, int> _stations;
    private readonly Dictionary<string, string> _texts;
    private readonly Dictionary<string, string> _custom;
    private readonly Dictionary<Skills.SkillType, Skills.Skill> _skills;
    private readonly Dictionary<Skills.Skill, Vector2> _skillValues;
    private readonly List<Player.Food> _foods;
    private readonly float _guardianCooldown;
    private readonly bool _noCost;

    private PlayerState(Player p)
    {
        _p = p;
        _materials = new HashSet<string>(p.m_knownMaterial);
        _recipes = new HashSet<string>(p.m_knownRecipes);
        _trophies = new HashSet<string>(p.m_trophies);
        _uniques = new HashSet<string>(p.m_uniques);
        _biomes = new HashSet<string>(p.m_knownBiome);
        _tutorials = new HashSet<string>(p.m_shownTutorials);
        _stations = new Dictionary<string, int>(p.m_knownStations);
        _texts = new Dictionary<string, string>(p.m_knownTexts);
        _custom = new Dictionary<string, string>(p.m_customData);
        var skills = p.GetSkills();
        _skills = skills != null && skills.m_skillData != null
            ? new Dictionary<Skills.SkillType, Skills.Skill>(skills.m_skillData)
            : new Dictionary<Skills.SkillType, Skills.Skill>();
        _skillValues = new Dictionary<Skills.Skill, Vector2>();
        foreach (var s in _skills.Values)
        {
            if (s != null)
            {
                _skillValues[s] = new Vector2(s.m_level, s.m_accumulator);
            }
        }
        _foods = new List<Player.Food>(p.m_foods);
        _guardianCooldown = p.m_guardianPowerCooldown;
        _noCost = p.m_noPlacementCost;
    }

    internal static PlayerState Capture(Player p) => new PlayerState(p);

    internal void Restore()
    {
        var p = _p;
        if (p == null)
        {
            return;
        }
        Put(p.m_knownMaterial, _materials);
        Put(p.m_knownRecipes, _recipes);
        Put(p.m_trophies, _trophies);
        Put(p.m_uniques, _uniques);
        Put(p.m_knownBiome, _biomes);
        Put(p.m_shownTutorials, _tutorials);
        p.m_knownStations.Clear();
        foreach (var kv in _stations)
        {
            p.m_knownStations[kv.Key] = kv.Value;
        }
        p.m_knownTexts.Clear();
        foreach (var kv in _texts)
        {
            p.m_knownTexts[kv.Key] = kv.Value;
        }
        p.m_customData.Clear();
        foreach (var kv in _custom)
        {
            p.m_customData[kv.Key] = kv.Value;
        }
        var skills = p.GetSkills();
        if (skills != null && skills.m_skillData != null)
        {
            skills.m_skillData.Clear();
            foreach (var kv in _skills)
            {
                skills.m_skillData[kv.Key] = kv.Value;
                if (kv.Value != null && _skillValues.TryGetValue(kv.Value, out var v))
                {
                    kv.Value.m_level = v.x;
                    kv.Value.m_accumulator = v.y;
                }
            }
        }
        p.m_foods.Clear();
        p.m_foods.AddRange(_foods);
        p.m_guardianPowerCooldown = _guardianCooldown;
        p.m_noPlacementCost = _noCost;
        OwnRecords.ClearCache();
        TamesReader.ClearCache();
    }

    private static void Put(HashSet<string> set, HashSet<string> saved)
    {
        set.Clear();
        set.UnionWith(saved);
    }
}

/// <summary>Copy of every stat table of the profile (all difficulty slots). Restore put the numbers back in place.</summary>
internal sealed class ProfileState
{
    private readonly List<KeyValuePair<Dictionary<string, float>, Dictionary<string, float>>> _named =
        new List<KeyValuePair<Dictionary<string, float>, Dictionary<string, float>>>();

    private readonly List<KeyValuePair<Dictionary<PlayerStatType, float>, Dictionary<PlayerStatType, float>>> _stats =
        new List<KeyValuePair<Dictionary<PlayerStatType, float>, Dictionary<PlayerStatType, float>>>();

    private ProfileState(PlayerProfile profile)
    {
        if (profile == null || profile.m_playerStats == null)
        {
            return;
        }
        foreach (var s in profile.m_playerStats)
        {
            if (s == null)
            {
                continue;
            }
            if (s.m_stats != null)
            {
                _stats.Add(new KeyValuePair<Dictionary<PlayerStatType, float>, Dictionary<PlayerStatType, float>>(s.m_stats,
                    new Dictionary<PlayerStatType, float>(s.m_stats)));
            }
            Keep(s.m_knownWorlds);
            Keep(s.m_knownWorldKeys);
            Keep(s.m_knownCommands);
            Keep(s.m_itemPickupStats);
            Keep(s.m_itemCraftStats);
            Keep(s.m_pickableStats);
            Keep(s.m_foodEatenStats);
            Keep(s.m_piecesPlacedStats);
            if (s.m_enemyStats != null)
            {
                foreach (var d in s.m_enemyStats)
                {
                    Keep(d);
                }
            }
        }
    }

    internal static ProfileState Capture() => new ProfileState(Game.instance != null ? Game.instance.GetPlayerProfile() : null);

    private void Keep(Dictionary<string, float> d)
    {
        if (d != null)
        {
            _named.Add(new KeyValuePair<Dictionary<string, float>, Dictionary<string, float>>(d, new Dictionary<string, float>(d)));
        }
    }

    internal void Restore()
    {
        foreach (var kv in _named)
        {
            kv.Key.Clear();
            foreach (var e in kv.Value)
            {
                kv.Key[e.Key] = e.Value;
            }
        }
        foreach (var kv in _stats)
        {
            kv.Key.Clear();
            foreach (var e in kv.Value)
            {
                kv.Key[e.Key] = e.Value;
            }
        }
    }
}

internal static class TestKit
{
    internal const float CatalogTimeout = 60f;
    internal const float PrepareTimeout = 30f;

    /// <summary>Box a wait loop fill: catalog, or why not.</summary>
    internal sealed class CatalogBox
    {
        internal Catalog Value;
        internal string Error;
    }

    /// <summary>Wait the catalog (build it if needed). Box.Value null at the end = Box.Error say why.</summary>
    internal static IEnumerator WaitCatalog(CatalogBox box)
    {
        var start = Time.realtimeSinceStartup;
        while (true)
        {
            try
            {
                box.Value = CatalogService.EnsureReady();
            }
            catch (Exception e)
            {
                box.Error = $"EnsureReady threw {e.GetType().Name}: {e.Message}";
                yield break;
            }
            if (box.Value != null)
            {
                yield break;
            }
            if (Time.realtimeSinceStartup - start > CatalogTimeout)
            {
                box.Error = $"catalog not ready after {CatalogTimeout} s (building: {CatalogService.IsBuilding})";
                yield break;
            }
            yield return null;
        }
    }

    /// <summary>Data test with no frame to wait: wait the catalog, run the check, one PASS or FAIL.</summary>
    internal static IEnumerator RunData(string name, Func<Catalog, List<string>, string> check)
    {
        var box = new CatalogBox();
        yield return WaitCatalog(box);
        if (box.Value == null)
        {
            SelfTest.Fail(name, box.Error);
            yield break;
        }
        var problems = new List<string>();
        var detail = "";
        if (Player.m_localPlayer == null || Game.instance == null || Game.instance.GetPlayerProfile() == null)
        {
            SelfTest.Fail(name, "no local player or profile");
            yield break;
        }
        try
        {
            detail = check(box.Value, problems) ?? "";
        }
        catch (Exception e)
        {
            problems.Add($"check threw {e}");
        }
        Report(name, problems, detail);
    }

    internal static void Report(string name, List<string> problems, string passDetail)
    {
        if (problems.Count == 0 && string.IsNullOrEmpty(passDetail))
        {
            // No problem but no result either: a check was skipped on the way. Never a pass.
            SelfTest.Fail(name, "the test ended without checking anything (a needed object was missing)");
            return;
        }
        if (problems.Count == 0)
        {
            SelfTest.Pass(name, passDetail);
            return;
        }
        var shown = problems.Take(12).ToList();
        SelfTest.Fail(name, $"{problems.Count} problem(s): {string.Join(" | ", shown)}{(problems.Count > shown.Count ? " | ..." : "")}");
    }

    internal static IEnumerator Frames(int n)
    {
        for (var i = 0; i < n; i++)
        {
            yield return null;
        }
    }

    // ---------------------------------------------------------------- details as text

    /// <summary>Printed text of every line of a detail view.</summary>
    internal static List<string> Texts(DetailView view) => view.Lines.Select(l => Names.StripTags(l.Text())).ToList();

    /// <summary>Printed text of every line that is not the entry's own game text (description, stats, lore).</summary>
    internal static List<string> LabelTexts(DetailView view) =>
        view.Lines.Where(l => !l.Parts.Any(p => p.OwnText)).Select(l => Names.StripTags(l.Text())).ToList();

    /// <summary>Lines between the header with this text and the next header (header not included). Null = no such header.</summary>
    internal static List<DetailLine> Block(DetailView view, string header)
    {
        List<DetailLine> block = null;
        foreach (var l in view.Lines)
        {
            if (l.Kind == DetailLineKind.Header)
            {
                if (block != null)
                {
                    return block;
                }
                if (Names.StripTags(l.Text()) == header)
                {
                    block = new List<DetailLine>();
                }
                continue;
            }
            block?.Add(l);
        }
        return block;
    }

    /// <summary>Whole-word, case-insensitive.</summary>
    internal static bool ContainsWord(string text, string word)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(word))
        {
            return false;
        }
        var at = 0;
        while (true)
        {
            var i = text.IndexOf(word, at, StringComparison.OrdinalIgnoreCase);
            if (i < 0)
            {
                return false;
            }
            var before = i == 0 || !char.IsLetterOrDigit(text[i - 1]);
            var end = i + word.Length;
            var after = end >= text.Length || !char.IsLetterOrDigit(text[end]);
            if (before && after)
            {
                return true;
            }
            at = i + 1;
        }
    }

    // ---------------------------------------------------------------- inventory

    /// <summary>
    /// Item put in the player's inventory the way a pickup end (Inventory.AddItem, so Player.OnInventoryChanged run).
    /// Null = no such prefab or no room.
    /// </summary>
    internal static string GiveItem(Player player, string prefabName, int amount)
    {
        var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
        var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        if (drop == null)
        {
            return null;
        }
        var data = drop.m_itemData.Clone();
        data.m_dropPrefab = prefab;
        data.m_stack = Mathf.Min(amount, data.m_shared.m_maxStackSize);
        data.m_worldLevel = (byte)Game.m_worldLevel;
        return player.GetInventory().AddItem(data) ? data.m_shared.m_name : null;
    }

    /// <summary>Like GiveItem, into one empty slot of the player's grid. Null = no such prefab or the slot is taken.</summary>
    internal static string GiveItemAt(Player player, string prefabName, int amount, Vector2i slot)
    {
        var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
        var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        var inventory = player.GetInventory();
        if (drop == null || inventory.GetItemAt(slot.x, slot.y) != null)
        {
            return null;
        }
        var data = drop.m_itemData.Clone();
        data.m_dropPrefab = prefab;
        data.m_stack = Mathf.Min(amount, data.m_shared.m_maxStackSize);
        data.m_worldLevel = (byte)Game.m_worldLevel;
        return inventory.AddItem(data, slot) ? data.m_shared.m_name : null;
    }

    /// <summary>Take back what GiveItem gave (by item token and amount).</summary>
    internal static void TakeItem(Player player, string token, int amount)
    {
        if (player != null && token != null)
        {
            player.GetInventory().RemoveItem(token, amount);
        }
    }

    /// <summary>"New item" / "New recipe" popups still waiting: gone (the test's fake pickups made them).</summary>
    internal static void ClearUnlockPopups()
    {
        if (MessageHud.instance != null && MessageHud.instance.m_unlockMsgQueue != null)
        {
            MessageHud.instance.m_unlockMsgQueue.Clear();
        }
    }

    // ---------------------------------------------------------------- world

    /// <summary>Point near the player on the ground: metres ahead and to the right of the camera's flat forward.</summary>
    internal static Vector3 PointNear(Player player, float ahead, float right)
    {
        var cam = Utils.GetMainCamera();
        var up = Vector3.up;
        var fwd = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, up).normalized : player.transform.forward;
        var side = cam != null ? Vector3.ProjectOnPlane(cam.transform.right, up).normalized : player.transform.right;
        var pos = player.transform.position + fwd * ahead + side * right;
        pos.y = ZoneSystem.instance.GetGroundHeight(pos) + 0.2f;
        return pos;
    }

    internal static GameObject Spawn(string prefabName, Vector3 pos, Quaternion rot)
    {
        var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefabName) : null;
        return prefab != null ? UnityEngine.Object.Instantiate(prefab, pos, rot) : null;
    }

    internal static void Despawn(GameObject go)
    {
        if (go == null)
        {
            return;
        }
        var nview = go.GetComponent<ZNetView>();
        if (nview != null && nview.IsValid() && !nview.IsOwner())
        {
            nview.ClaimOwnership(); // not ours = its ZDO would stay in the world
        }
        if (ZNetScene.instance != null)
        {
            ZNetScene.instance.Destroy(go);
        }
        else
        {
            UnityEngine.Object.Destroy(go);
        }
    }

    /// <summary>Ids of every ragdoll and dropped item alive now (kill tests remove what came after).</summary>
    internal static HashSet<int> WorldLitter()
    {
        var ids = new HashSet<int>();
        foreach (var r in UnityEngine.Object.FindObjectsByType<Ragdoll>(FindObjectsSortMode.None))
        {
            ids.Add(r.gameObject.GetInstanceID());
        }
        foreach (var d in ItemDrop.s_instances)
        {
            if (d != null)
            {
                ids.Add(d.gameObject.GetInstanceID());
            }
        }
        return ids;
    }

    /// <summary>Ragdolls (their loot never spawn) and dropped items that came after the snapshot, near the point: gone.</summary>
    internal static int ClearLitter(HashSet<int> before, Vector3 near, float radius)
    {
        var removed = 0;
        foreach (var r in UnityEngine.Object.FindObjectsByType<Ragdoll>(FindObjectsSortMode.None))
        {
            if (r != null && !before.Contains(r.gameObject.GetInstanceID()) && Vector3.Distance(r.transform.position, near) < radius)
            {
                Despawn(r.gameObject);
                removed++;
            }
        }
        foreach (var d in new List<ItemDrop>(ItemDrop.s_instances))
        {
            if (d != null && !before.Contains(d.gameObject.GetInstanceID()) && Vector3.Distance(d.transform.position, near) < radius)
            {
                Despawn(d.gameObject);
                removed++;
            }
        }
        return removed;
    }

    /// <summary>Hit that kill: the local player is the attacker (kill go to its profile the vanilla way), no backstab show.</summary>
    internal static HitData KillingHit(Player player, Character target, Skills.SkillType skill)
    {
        var hit = new HitData();
        hit.m_damage.m_damage = 1000000f;
        hit.m_point = target.GetCenterPoint();
        hit.m_dir = (target.transform.position - player.transform.position).normalized;
        hit.m_skill = skill;
        hit.m_backstabBonus = 1f;
        hit.m_blockable = false;
        hit.m_dodgeable = false;
        hit.SetAttacker(player);
        return hit;
    }

    // ---------------------------------------------------------------- UI

    internal static bool InventoryShown(InventoryGui gui) => SideButton.InventoryShown(gui);

    /// <summary>Inventory open and its show animation over.</summary>
    internal static IEnumerator ShowInventory(InventoryGui gui)
    {
        if (!InventoryShown(gui))
        {
            gui.Show(null);
        }
        yield return new WaitForSecondsRealtime(1.2f);
    }

    /// <summary>Raven click, then the Encyclopedia tab click, then wait "Preparing entries..." out. False = not open.</summary>
    internal static IEnumerator OpenByTab(InventoryGui gui, List<string> problems, string when)
    {
        var raven = TopTabs.RavenButton(gui);
        if (raven == null)
        {
            problems.Add($"{when}: the Valheim Compendium button (raven) was not found");
            yield break;
        }
        if (!CompendiumWindow.IsOpen)
        {
            if (!TopTabs.VanillaShown(gui))
            {
                raven.onClick.Invoke();
                yield return null;
                yield return null;
            }
            if (!CompendiumWindow.IsOpen && TopTabs.VanillaEncyclopedia != null)
            {
                TopTabs.VanillaEncyclopedia.onClick.Invoke();
            }
            yield return null;
            yield return null;
        }
        yield return WaitPrepared();
        if (!CompendiumWindow.IsOpen || CompendiumWindow.Preparing)
        {
            problems.Add($"{when}: the Encyclopedia is not open with its list (open {CompendiumWindow.IsOpen}, preparing {CompendiumWindow.Preparing})");
        }
    }

    internal static IEnumerator WaitPrepared()
    {
        var start = Time.realtimeSinceStartup;
        while (CompendiumWindow.IsOpen && CompendiumWindow.Preparing && Time.realtimeSinceStartup - start < PrepareTimeout)
        {
            yield return null;
        }
    }

    /// <summary>Window, vanilla dialog and inventory closed (every test end with me in finally).</summary>
    internal static void CloseAll(InventoryGui gui)
    {
        CompendiumWindow.Close(selectButton: false);
        if (gui == null)
        {
            return;
        }
        if (TopTabs.VanillaShown(gui))
        {
            gui.m_textsDialog.OnClose();
        }
        if (InventoryShown(gui))
        {
            gui.Hide();
        }
    }

    /// <summary>What sits on top at a screen point (UI raycast). Null = nothing.</summary>
    internal static GameObject TopAt(Vector2 screen)
    {
        var es = EventSystem.current;
        if (es == null)
        {
            return null;
        }
        var hits = new List<RaycastResult>();
        es.RaycastAll(new PointerEventData(es) { position = screen }, hits);
        return hits.Count > 0 ? hits[0].gameObject : null;
    }

    /// <summary>
    /// One mouse click at a screen point, the way the input module send it: pointer down to the first object that take
    /// it (from the top one up), pointer up, then the click to the first object that take clicks. Return the top
    /// object's path ("nothing" when the point hit no UI).
    /// </summary>
    internal static string Click(Vector2 screen, PointerEventData.InputButton button)
    {
        var es = EventSystem.current;
        if (es == null)
        {
            return "no event system";
        }
        var data = new PointerEventData(es) { position = screen, button = button, clickCount = 1, eligibleForClick = true };
        var hits = new List<RaycastResult>();
        es.RaycastAll(data, hits);
        if (hits.Count == 0)
        {
            return "nothing";
        }
        var over = hits[0].gameObject;
        data.pointerCurrentRaycast = hits[0];
        data.pointerPressRaycast = hits[0];
        data.pressPosition = screen;
        var clickTarget = ExecuteEvents.GetEventHandler<IPointerClickHandler>(over);
        var pressed = ExecuteEvents.ExecuteHierarchy(over, data, ExecuteEvents.pointerDownHandler);
        if (pressed == null)
        {
            pressed = clickTarget;
        }
        data.pointerPress = pressed;
        data.rawPointerPress = over;
        if (pressed != null)
        {
            ExecuteEvents.Execute(pressed, data, ExecuteEvents.pointerUpHandler);
        }
        if (clickTarget != null)
        {
            ExecuteEvents.Execute(clickTarget, data, ExecuteEvents.pointerClickHandler);
        }
        return UiUtil.Path(over.transform);
    }

    /// <summary>Click on one UI object (its own handlers, wherever it sits): what a click that reach it does.</summary>
    internal static void ClickObject(GameObject go, PointerEventData.InputButton button)
    {
        var es = EventSystem.current;
        if (es == null || go == null)
        {
            return;
        }
        var data = new PointerEventData(es) { button = button, clickCount = 1, eligibleForClick = true };
        ExecuteEvents.Execute(go, data, ExecuteEvents.pointerClickHandler);
    }

    internal static Vector2 Center(RectTransform rt) => UiUtil.WorldRect(rt).center;

    internal static bool Under(GameObject go, GameObject root) => go != null && root != null && go.transform.IsChildOf(root.transform);

    /// <summary>List row view bound to this entry now (null = not on screen).</summary>
    internal static RowView BoundRow(Entry e)
    {
        var rows = CompendiumWindow.Rows;
        foreach (var r in CompendiumWindow.ListPool)
        {
            if (r.Go.activeSelf && r.Bound >= 0 && r.Bound < rows.Count && ReferenceEquals(rows[r.Bound].Entry, e))
            {
                return r;
            }
        }
        return null;
    }

    /// <summary>Is another MC mod loaded and active now?</summary>
    internal static bool ModActive(string guid, out string why)
    {
        var view = FeatureRegistry.Find(guid);
        if (view == null)
        {
            why = "not installed";
            return false;
        }
        why = view.Value.IsActive ? "" : $"{view.Value.State}: {view.Value.Status}";
        return view.Value.IsActive;
    }

    /// <summary>Type of another loaded mod by assembly and full type name (no reference to it). Null = not loaded.</summary>
    internal static Type FindType(string assemblyName, string typeName)
    {
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (a.GetName().Name == assemblyName)
            {
                return a.GetType(typeName, false);
            }
        }
        return null;
    }

    /// <summary>First object with this name under the inventory screen (other mods' buttons, by their object name).</summary>
    internal static Transform FindUnder(InventoryGui gui, string name)
    {
        foreach (var t in gui.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
            {
                return t;
            }
        }
        return null;
    }
}

/// <summary>One UI self test run: what it needs and what it must put back.</summary>
internal sealed class UiRun
{
    internal string Name = "";
    internal InventoryGui Gui;
    internal Player Player;
    internal Catalog Cat;
    internal readonly List<string> Problems = new List<string>();
    internal string Detail = "";
    internal TopTabs.Choice LastBefore;
    internal PlayerState State;
    internal ProfileState Profile;
    internal bool Forced;

    /// <summary>Wait the catalog. Cat null after = FAIL already said.</summary>
    internal IEnumerator Begin(string name)
    {
        Name = name;
        Gui = InventoryGui.instance;
        Player = Player.m_localPlayer;
        if (Gui == null || Player == null || Game.instance == null || Game.instance.GetPlayerProfile() == null || Plugin.Instance == null)
        {
            SelfTest.Fail(name, "no InventoryGui, local player, profile or plugin");
            yield break;
        }
        var box = new TestKit.CatalogBox();
        yield return TestKit.WaitCatalog(box);
        if (box.Value == null)
        {
            SelfTest.Fail(name, box.Error);
            yield break;
        }
        Cat = box.Value;
    }

    /// <summary>
    /// Settings forced in memory (display defaults, side button as asked), remembered tab = Texts, copies of the
    /// character and the profile. First line of the test's try block.
    /// </summary>
    internal void Force(bool sideButton)
    {
        LastBefore = TopTabs.Last;
        State = PlayerState.Capture(Player);
        Profile = ProfileState.Capture();
        Forced = true;
        Plugin.TestDisplay = new Plugin.DisplayOverride { ShowUndiscovered = true, RevealAll = false };
        Plugin.TestSideButton = sideButton;
        SideButton.Sync(Gui);
        TopTabs.SetLastForTest(TopTabs.Choice.Texts);
    }

    /// <summary>The test's finally: everything closed, forced settings gone, character and profile as before.</summary>
    internal void End()
    {
        if (Plugin.TestOff)
        {
            Plugin.SetOffForTest(false);
        }
        Plugin.TestDisplay = null;
        Plugin.TestSideButton = null;
        if (Forced)
        {
            TopTabs.SetLastForTest(LastBefore);
        }
        TestKit.CloseAll(Gui);
        if (Gui != null)
        {
            SideButton.Sync(Gui);
        }
        State?.Restore();
        Profile?.Restore();
        TestKit.ClearUnlockPopups();
    }

    internal void Report() => TestKit.Report(Name, Problems, Detail);

    internal void Note(string text) => SelfTest.Note(Name, text);
}
#endif
