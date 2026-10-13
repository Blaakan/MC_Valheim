#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using MC.Shared;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MC.Exploration.ViewSpyglassMod;

// Debug build only. Me = self tests with another MC mod really doing its part in the same game (every MC mod is
// loaded in a test run). Other mod found by GUID in the shared registry; me never use its types: its effect is read
// from the game (scene objects, chest slots, craft list), its settings through BepInEx or, where a single-player
// test must not write a config file, through its own in-memory test window (reflection, fails loud when gone).
// Not loaded or not active = FAIL, never a quiet pass. TESTING.md items in brackets:
//   spyglass.horizons.detail    Distant Horizons: with a real spyglass at x8 the far land where it looks is drawn
//                               finer within seconds, also after turning; lowered: its extra detail and the haze are
//                               back within a second or two [T12]
//   spyglass.horizons.settings  Distant Horizons' SpyglassDetail = false, then SpyglassMaxBoost = 1: a raised x8
//                               spyglass adds no detail, the haze is still cleared; defaults: detail comes [X01]
//   spyglass.sort.chest         Sort Chest: a real chest with spyglass, hammer, hoe, sword, club and wood, its Sort
//                               button: the spyglass lands in the block of the tools [X02]
//   spyglass.sort.crafting      Crafting Search and Sort: workbench list sorted by its "Tools and light" option puts
//                               the spyglass with hammer and hoe; by "Weapons" it is not among them [X02]
internal static partial class SelfTests
{
    private const string HorizonsDetailTest = "spyglass.horizons.detail";
    private const string HorizonsSettingsTest = "spyglass.horizons.settings";
    private const string SortChestTest = "spyglass.sort.chest";
    private const string SortCraftingTest = "spyglass.sort.crafting";

    private const string SortChestGuid = "MC.UX.Container.Sort";
    private const string CraftSortGuid = "MC.UX.Crafting.SearchSort";
    private const BindingFlags AnyMember = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    // ---------- Distant Horizons ----------

    // Far land tiles Distant Horizons draws right now, read from the scene: its root object "DistantHorizons_LOD"
    // holds one object per tile, each with a game Heightmap; switched on = drawn. x, z = centre, y = edge length (m).
    private static List<Vector3> FarTiles()
    {
        var list = new List<Vector3>();
        var root = GameObject.Find("DistantHorizons_LOD");
        if (root == null)
        {
            return list;
        }
        foreach (Transform child in root.transform)
        {
            if (!child.gameObject.activeSelf)
            {
                continue;
            }
            var map = child.GetComponent<Heightmap>();
            if (map != null)
            {
                list.Add(new Vector3(child.position.x, map.m_width * map.m_scale, child.position.z));
            }
        }
        return list;
    }

    // Finest drawn far tile (edge length, m) under points 2 to 5 km out along a compass direction and 4 degrees to
    // each side. 0 = no far land drawn there.
    private static float FinestAhead(Vector3 from, float yaw)
    {
        var tiles = FarTiles();
        var finest = float.MaxValue;
        for (var side = -4f; side <= 4f; side += 4f)
        {
            var dir = Quaternion.Euler(0f, yaw + side, 0f) * Vector3.forward;
            for (var d = 2000f; d <= 5000f; d += 250f)
            {
                var x = from.x + dir.x * d;
                var z = from.z + dir.z * d;
                foreach (var t in tiles)
                {
                    var half = t.y * 0.5f;
                    if (t.y < finest && Mathf.Abs(x - t.x) <= half && Mathf.Abs(z - t.z) <= half)
                    {
                        finest = t.y;
                    }
                }
            }
        }
        return finest < float.MaxValue ? finest : 0f;
    }

    // Distant Horizons' own in-memory test window (Debug build of it): two spyglass settings forced without its
    // config file, and its count of far tiles it wants split farther out than without a spyglass.
    private sealed class HorizonsWindow
    {
        internal string Problem = "";
        private object _cfg;
        private object _detail;
        private object _maxBoost;
        private MethodInfo _set;
        private MethodInfo _clear;
        private PropertyInfo _manager;
        private MethodInfo _boostLeft;

        internal static HorizonsWindow Open()
        {
            var w = new HorizonsWindow();
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(ScopeCamera.DistantHorizonsGuid, out var info) || info.Instance == null)
                {
                    w.Problem = "Distant Horizons is not loaded";
                    return w;
                }
                var plugin = info.Instance.GetType();
                var cfg = plugin.GetProperty("Cfg", AnyMember);
                w._cfg = cfg != null ? cfg.GetValue(null, null) : null;
                var cfgType = w._cfg != null ? w._cfg.GetType() : null;
                var detail = cfgType != null ? cfgType.GetField("SpyglassDetail", AnyMember) : null;
                var maxBoost = cfgType != null ? cfgType.GetField("SpyglassMaxBoost", AnyMember) : null;
                w._detail = detail != null ? detail.GetValue(w._cfg) : null;
                w._maxBoost = maxBoost != null ? maxBoost.GetValue(w._cfg) : null;
                w._set = cfgType != null ? cfgType.GetMethod("SetForTest", AnyMember) : null;
                w._clear = cfgType != null ? cfgType.GetMethod("ClearForTest", AnyMember) : null;
                var manager = plugin.Assembly.GetType(plugin.Namespace + ".LodTerrainManager");
                w._manager = manager != null ? manager.GetProperty("Instance", AnyMember) : null;
                w._boostLeft = manager != null ? manager.GetMethod("TestBoostLeft", AnyMember) : null;
                if (!(w._detail is ConfigEntry<bool>) || !(w._maxBoost is ConfigEntry<float>) || w._set == null || w._clear == null
                    || w._manager == null || w._boostLeft == null)
                {
                    w.Problem = "Distant Horizons has no test window any more (a Release build of it is loaded, or its SpyglassDetail / SpyglassMaxBoost "
                                + "settings, SetForTest, ClearForTest or LodTerrainManager.TestBoostLeft changed)";
                }
            }
            catch (Exception e)
            {
                w.Problem = "Distant Horizons' test window could not be opened: " + e.Message;
            }
            return w;
        }

        // Both settings read as these values from now on (nothing saved). False = could not.
        internal bool Force(bool detail, float maxBoost)
        {
            try
            {
                _set.MakeGenericMethod(typeof(bool)).Invoke(_cfg, new[] { _detail, (object)detail });
                _set.MakeGenericMethod(typeof(float)).Invoke(_cfg, new[] { _maxBoost, (object)maxBoost });
                return true;
            }
            catch (Exception e)
            {
                Problem = "Distant Horizons' settings could not be forced: " + (e.InnerException ?? e).Message;
                return false;
            }
        }

        // Settings read from its config again.
        internal void Release()
        {
            if (_clear == null || _cfg == null)
            {
                return;
            }
            _clear.Invoke(_cfg, new[] { _detail });
            _clear.Invoke(_cfg, new[] { _maxBoost });
        }

        // Far tiles it wants split farther out than its no-spyglass rule allows (0 whenever no spyglass boost is in
        // play). -1 = not readable.
        internal int PastReach()
        {
            try
            {
                var manager = _manager.GetValue(null, null);
                if (manager == null || (manager is Object o && o == null))
                {
                    return -1;
                }
                var args = new object[] { 0, 0, null };
                _boostLeft.Invoke(manager, args);
                return args[0] is int n ? n : -1;
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }

    // Start of both tests: Distant Horizons active and its window open, spyglass in hand at noon in clear weather,
    // looking over open land, fog and far land settled. start: [0] yaw, [1] fog density before, [2] finest far tile
    // ahead, [3] finest far tile 90 degrees to the right. Null window = nothing to test on (checks say why).
    private static IEnumerator HorizonsStart(Checks c, SpyRig rig, bool detail, float maxBoost, HorizonsWindow[] window, float[] start)
    {
        window[0] = null;
        var player = rig.Player;
        var w = HorizonsWindow.Open();
        if (!c.Check(w.Problem.Length == 0, w.Problem.Length == 0 ? "Distant Horizons' in-memory test window is open" : w.Problem))
        {
            yield break;
        }
        rig.Noon();
        rig.ClearWeather();
        if (Hold(rig, c) == null)
        {
            yield break;
        }
        ServerRules.TestRules = new SpyglassRules();
        Scope.TestHoldToLook = false;
        rig.OnRestore("Distant Horizons settings", w.Release);
        if (!c.Check(w.Force(detail, maxBoost), w.Problem.Length == 0 ? $"Distant Horizons reads SpyglassDetail = {detail}, SpyglassMaxBoost = {F(maxBoost)} (in memory)" : w.Problem))
        {
            yield break;
        }
        start[0] = OpenYaw(player, out _);
        rig.Look(start[0], -1f);
        var fog = new float[1];
        yield return FogSettled(fog, 15f);
        start[1] = fog[0];
        // Far land settled: same finest tile ahead for 2 s and nothing of an earlier spyglass left.
        var eye = player.m_eye.position;
        var last = -1f;
        var since = Time.realtimeSinceStartup;
        var end = Time.realtimeSinceStartup + 25f;
        while (Time.realtimeSinceStartup < end)
        {
            var now = FinestAhead(eye, start[0]);
            if (!Mathf.Approximately(now, last) || w.PastReach() != 0)
            {
                last = now;
                since = Time.realtimeSinceStartup;
            }
            else if (Time.realtimeSinceStartup - since >= 2f)
            {
                break;
            }
            yield return new WaitForSecondsRealtime(0.25f);
        }
        start[2] = FinestAhead(eye, start[0]);
        start[3] = FinestAhead(eye, start[0] + 90f);
        var past = w.PastReach();
        if (!c.Check(start[2] > 0f && start[3] > 0f && past == 0,
                $"spyglass down: Distant Horizons draws far land 2 to 5 km out (finest tile {F(start[2])} m ahead, {F(start[3])} m to the right) and wants no extra detail ({past} tiles split past its normal reach)"))
        {
            yield break;
        }
        window[0] = w;
    }

    // Spyglass at the eye for some seconds: finest[0] = finest far tile drawn where it looks meanwhile, most[0] =
    // most tiles Distant Horizons wanted split past its normal reach. stopWhenFiner = leave as soon as both show.
    // wantedAfter[0] (when given) = seconds until Distant Horizons first wanted such a split, -1 = never.
    private static IEnumerator WatchDetail(Player player, HorizonsWindow w, float yaw, float before, float seconds, bool stopWhenFiner, float[] finest, int[] most, float[] took,
        float[] wantedAfter = null)
    {
        var eye = player.m_eye.position;
        finest[0] = before;
        most[0] = 0;
        took[0] = -1f;
        if (wantedAfter != null)
        {
            wantedAfter[0] = -1f;
        }
        var t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < seconds)
        {
            var now = FinestAhead(eye, yaw);
            if (now > 0f && now < finest[0])
            {
                finest[0] = now;
            }
            most[0] = Mathf.Max(most[0], w.PastReach());
            if (wantedAfter != null && wantedAfter[0] < 0f && most[0] > 0)
            {
                wantedAfter[0] = Time.realtimeSinceStartup - t0;
            }
            if (finest[0] < before - 0.5f && most[0] > 0)
            {
                if (took[0] < 0f)
                {
                    took[0] = Time.realtimeSinceStartup - t0;
                }
                if (stopWhenFiner)
                {
                    break;
                }
            }
            yield return null;
        }
    }

    private static IEnumerator RunHorizonsDetail()
    {
        var c = new Checks(HorizonsDetailTest);
        if (!Ready(HorizonsDetailTest, out var player, out var cam))
        {
            yield break;
        }
        if (!OtherModActive(ScopeCamera.DistantHorizonsGuid))
        {
            SelfTest.Fail(HorizonsDetailTest, "Distant Horizons is not loaded and active: nothing of TESTING.md T12 was checked");
            yield break;
        }
        var rig = new SpyRig(player, HorizonsDetailTest);
        try
        {
            var window = new HorizonsWindow[1];
            var start = new float[4];
            // Its default spyglass settings, whatever the player's config says.
            yield return HorizonsStart(c, rig, true, 4f, window, start);
            var w = window[0];
            if (w == null)
            {
                c.Report();
                yield break;
            }
            var yaw = start[0];
            var before = start[1];
            var problems = _tap != null ? _tap.Problems().Count : 0;
            var finest = new float[1];
            var most = new int[1];
            var took = new float[1];

            Scope.TestSetMagnification(8f);
            yield return RaiseUp(c, player, "far view");
            yield return WatchDetail(player, w, yaw, start[2], 10f, true, finest, most, took);
            c.Check(took[0] >= 0f && took[0] <= 8f,
                $"x8: the far land where the spyglass looks is drawn finer within a few seconds (finest tile 2 to 5 km out {F(start[2])} m -> {F(finest[0])} m, "
                + $"{most[0]} tiles split past the normal reach, after {(took[0] >= 0f ? F(took[0]) + " s" : "more than 10 s")})");
            yield return new WaitForEndOfFrame();
            var hazy = RenderSettings.fogDensity;
            c.Check(ScopeCamera.DistantHorizonsSeen && ScopeCamera.FogApplied && hazy < before * 0.95f,
                $"with less haze than without the spyglass (fog density {D(hazy)}, {D(before)} before)");
            SelfTest.Screenshot(HorizonsDetailTest, "ahead");
            yield return null;
            yield return null;

            // Turned to a new direction while looking.
            rig.Look(yaw + 90f, -1f);
            yield return Frames(3);
            c.Check(Vector3.Angle(cam.transform.forward, Quaternion.Euler(0f, yaw + 90f, 0f) * Vector3.forward) < 5f && Scope.State == ScopeState.Raised,
                "turned 90 degrees to the right, still looking");
            yield return WatchDetail(player, w, yaw + 90f, start[3], 10f, true, finest, most, took);
            c.Check(took[0] >= 0f && took[0] <= 8f,
                $"turning refines the new direction too (finest tile {F(start[3])} m -> {F(finest[0])} m after {(took[0] >= 0f ? F(took[0]) + " s" : "more than 10 s")})");

            // Lowered: detail and haze back.
            yield return LowerDown();
            var t0 = Time.realtimeSinceStartup;
            yield return WaitReal(() => w.PastReach() == 0, 5f);
            var back = Time.realtimeSinceStartup - t0;
            c.Check(w.PastReach() == 0 && back <= 2.5f, $"lowered: Distant Horizons wants no extra detail any more after {F(back)} s ({w.PastReach()} tiles still split past its normal reach)");
            var fog = new float[1];
            t0 = Time.realtimeSinceStartup;
            yield return FogSettled(fog, 4f);
            c.Check(!ScopeCamera.FogApplied && Mathf.Abs(fog[0] - before) <= before * 0.03f + 0.0000001f,
                $"lowered: the haze is back within {F(Time.realtimeSinceStartup - t0)} s (fog density {D(fog[0])}, {D(before)} before)");
            c.Check(_tap != null && ProblemsSince(problems).Count == 0, "no warning or error from the spyglass meanwhile");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    private static IEnumerator RunHorizonsSettings()
    {
        var c = new Checks(HorizonsSettingsTest);
        if (!Ready(HorizonsSettingsTest, out var player, out _))
        {
            yield break;
        }
        if (!OtherModActive(ScopeCamera.DistantHorizonsGuid))
        {
            SelfTest.Fail(HorizonsSettingsTest, "Distant Horizons is not loaded and active: nothing of TESTING.md X01 was checked");
            yield break;
        }
        var rig = new SpyRig(player, HorizonsSettingsTest);
        try
        {
            var window = new HorizonsWindow[1];
            var start = new float[4];
            yield return HorizonsStart(c, rig, false, 4f, window, start);
            var w = window[0];
            if (w == null)
            {
                c.Report();
                yield break;
            }
            var yaw = start[0];
            var before = start[1];
            var finest = new float[1];
            var most = new int[1];
            var took = new float[1];
            Scope.TestSetMagnification(8f);
            // Seconds at the eye each "no detail" round is watched. The control at the end must see Distant Horizons
            // ask for detail inside the same time, else a quiet window says nothing.
            const float Window = 3.5f;

            for (var round = 0; round < 2; round++)
            {
                var what = round == 0 ? "SpyglassDetail = false" : "SpyglassMaxBoost = 1";
                if (round == 1 && !c.Check(w.Force(true, 1f), w.Problem.Length == 0 ? "Distant Horizons reads SpyglassDetail = true, SpyglassMaxBoost = 1 (in memory)" : w.Problem))
                {
                    break;
                }
                yield return RaiseUp(c, player, what);
                yield return WatchDetail(player, w, yaw, start[2], Window, false, finest, most, took);
                yield return new WaitForEndOfFrame();
                var hazy = RenderSettings.fogDensity;
                var slot = ViewBoost.Peek();
                c.Check(Scope.State == ScopeState.Raised && slot != null && Math.Abs(slot[2] - 8) < 0.01 && slot[3] > 0.99,
                    $"{what}: the spyglass is at the eye at x8 and says so to Distant Horizons");
                c.Check(most[0] == 0 && finest[0] >= start[2] - 0.5f,
                    $"{what}: no extra detail where the spyglass looks for 3.5 s (finest far tile {F(start[2])} m -> {F(finest[0])} m, {most[0]} tiles split past the normal reach)");
                c.Check(ScopeCamera.FogApplied && hazy < before * 0.95f, $"{what}: the haze is still cleared (fog density {D(hazy)}, {D(before)} before)");
                yield return LowerDown();
                var fog = new float[1];
                yield return FogSettled(fog, 4f);
            }

            // Control: with its default settings the detail does come, so the two results above are the settings'.
            if (c.Check(w.Force(true, 4f), w.Problem.Length == 0 ? "Distant Horizons reads its default spyglass settings again (in memory)" : w.Problem))
            {
                var wantedAfter = new float[1];
                yield return RaiseUp(c, player, "defaults");
                yield return WatchDetail(player, w, yaw, start[2], 10f, true, finest, most, took, wantedAfter);
                c.Check(took[0] >= 0f, $"SpyglassDetail = true, SpyglassMaxBoost = 4: extra detail comes (finest far tile {F(start[2])} m -> {F(finest[0])} m, {most[0]} tiles split past the normal reach)");
                c.Check(wantedAfter[0] >= 0f && wantedAfter[0] <= Window,
                    $"and Distant Horizons asks for it {(wantedAfter[0] >= 0f ? F(wantedAfter[0]) + " s" : "never")} after the spyglass is at the eye: inside the {F(Window)} s "
                    + "the two settings above were watched for, so their quiet windows were long enough to show it");
                yield return LowerDown();
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- Sort Chest ----------

    // Setting of another plugin as it is now, as text (BepInEx config, no type of theirs). Null = not found.
    private static string OtherSetting(string guid, string section, string key)
    {
        if (!Chainloader.PluginInfos.TryGetValue(guid, out var info) || info.Instance == null)
        {
            return null;
        }
        var definition = new ConfigDefinition(section, key);
        var config = info.Instance.Config;
        return config.ContainsKey(definition) && config[definition].BoxedValue != null ? config[definition].BoxedValue.ToString() : null;
    }

    // Sort Chest sorts by its SortBy setting. Not "Type" in this game: forced in memory through its own test value
    // (never its config file). undo = put back; null answer = sorting by type now.
    private static string SortByType(out Action undo)
    {
        undo = null;
        var now = OtherSetting(SortChestGuid, "General", "SortBy");
        if (now == null)
        {
            return "Sort Chest has no General / SortBy setting any more";
        }
        if (now == "Type")
        {
            return null;
        }
        try
        {
            var plugin = Chainloader.PluginInfos[SortChestGuid].Instance.GetType();
            var field = plugin.GetField("TestSortBy", AnyMember);
            var kind = field != null ? Nullable.GetUnderlyingType(field.FieldType) : null;
            var read = plugin.GetMethod("ReadSortBy", AnyMember);
            if (field == null || kind == null || !kind.IsEnum || read == null)
            {
                return $"Sort Chest sorts by {now} in this game and has no in-memory test value to sort by type";
            }
            field.SetValue(null, Enum.Parse(kind, "Type"));
            undo = () => field.SetValue(null, null);
            var got = read.Invoke(null, null);
            return got != null && got.ToString() == "Type" ? null : $"Sort Chest still sorts by {got} after its test value was set";
        }
        catch (Exception e)
        {
            return $"Sort Chest sorts by {now} in this game and could not be set to sort by type: " + e.Message;
        }
    }

    private static IEnumerator RunSortChest()
    {
        var c = new Checks(SortChestTest);
        if (!Ready(SortChestTest, out var player, out _))
        {
            yield break;
        }
        if (!OtherModActive(SortChestGuid))
        {
            SelfTest.Fail(SortChestTest, "Sort Chest is not loaded and active: the chest part of TESTING.md X02 was not checked");
            yield break;
        }
        var rig = new SpyRig(player, SortChestTest);
        GameObject chest = null;
        try
        {
            var gui = InventoryGui.instance;
            var problem = SortByType(out var undo);
            if (undo != null)
            {
                rig.OnRestore("Sort Chest order", undo);
            }
            var prefab = ZNetScene.instance.GetPrefab("piece_chest_wood");
            var spot = Vector3.zero;
            if (!c.Check(problem == null, problem ?? "Sort Chest sorts by type")
                || !c.Check(gui != null && prefab != null && FreeSpot(player, new[] { 2f, 2.3f, 1.8f }, 0.6f, out spot), "a chest and free ground in front"))
            {
                c.Report();
                yield break;
            }
            chest = Object.Instantiate(prefab, spot, Quaternion.identity);
            var container = chest.GetComponent<Container>();
            rig.OnRestore("chest window", () =>
            {
                if (InventoryGui.instance != null && InventoryGui.IsVisible())
                {
                    InventoryGui.instance.Hide();
                }
            });
            yield return new WaitForSeconds(0.5f);
            if (!c.Check(container != null && container.GetInventory() != null, "the chest has its inventory"))
            {
                c.Report();
                yield break;
            }
            // Put in out of order: wood, spyglass, sword, hammer, club, hoe.
            var inv = container.GetInventory();
            var names = new[] { "Wood", SpyglassContent.ItemName, "SwordIron", "Hammer", "Club", "Hoe" };
            var items = new Dictionary<string, ItemDrop.ItemData>();
            foreach (var name in names)
            {
                var item = inv.AddItem(name, name == "Wood" ? 5 : 1, 1, 0, 0L, "", false);
                if (item != null)
                {
                    items[name] = item;
                }
            }
            if (!c.Check(items.Count == names.Length, $"spyglass, Hammer, Hoe, SwordIron, Club and Wood put in the chest ({items.Count} of {names.Length})"))
            {
                c.Report();
                yield break;
            }
            int Slot(string name) => items[name].m_gridPos.y * inv.GetWidth() + items[name].m_gridPos.x;
            c.Check(Slot(SpyglassContent.ItemName) < Slot("SwordIron") && Slot("SwordIron") < Slot("Hammer"), "before the sort the spyglass lies before the sword, the hammer after it");

            // Open like the player (Use on the chest), then the Sort button Sort Chest added.
            container.Interact(player, false, false);
            yield return WaitReal(() => ReferenceEquals(gui.m_currentContainer, container) && gui.m_container.gameObject.activeInHierarchy, 5f);
            c.Check(ReferenceEquals(gui.m_currentContainer, container), "the chest opens on Use");
            yield return Frames(10);
            var found = FindDeep(gui.transform, "MC_ContainerSort_Sort");
            var button = found != null ? found.GetComponent<Button>() : null;
            if (!c.Check(ReferenceEquals(gui.m_currentContainer, container) && button != null && button.gameObject.activeInHierarchy && button.interactable,
                    "the chest is open and shows Sort Chest's Sort button"))
            {
                c.Report();
                yield break;
            }
            var errors = _tap != null ? _tap.ErrorMark : 0;
            button.onClick.Invoke();
            yield return Frames(5);

            // Slots after the sort, in reading order.
            var order = new List<string>(names);
            order.Sort((a, b) => Slot(a).CompareTo(Slot(b)));
            var tools = new[] { order.IndexOf("Hammer"), order.IndexOf("Hoe"), order.IndexOf(SpyglassContent.ItemName) };
            var first = Mathf.Min(tools[0], tools[1], tools[2]);
            var last = Mathf.Max(tools[0], tools[1], tools[2]);
            c.Note("order after the sort: " + string.Join(", ", order.ToArray()));
            c.Check(inv.NrOfItems() == names.Length && CountSpyglasses(inv) == 1, $"nothing lost or doubled by the sort ({inv.NrOfItems()} stacks)");
            c.Check(last - first == 2, $"the spyglass, the hammer and the hoe lie next to each other: one block of tools (places {first + 1} to {last + 1})");
            var sword = order.IndexOf("SwordIron");
            var club = order.IndexOf("Club");
            c.Check(Mathf.Abs(sword - club) == 1 && (sword < first || sword > last) && (club < first || club > last),
                $"the sword and the club lie next to each other in their own block, outside the tools (places {sword + 1} and {club + 1})");
            var wood = order.IndexOf("Wood");
            c.Check(wood < first || wood > last, $"the wood is not among the tools either (place {wood + 1})");
            var logged = _tap != null ? _tap.ErrorsSince(errors) : new List<string>();
            c.Check(_tap != null && logged.Count == 0, "no error logged by the sort" + (logged.Count == 0 ? "" : ": " + First(logged)));
            gui.Hide();
            yield return WaitReal(() => !InventoryGui.IsVisible(), 3f);
            c.Report();
        }
        finally
        {
            rig.Restore();
            if (chest != null && ZNetScene.instance != null)
            {
                // Empty first: a destroyed chest never drops its test items.
                var box = chest.GetComponent<Container>();
                if (box != null && box.GetInventory() != null)
                {
                    box.GetInventory().RemoveAll();
                }
                ZNetScene.instance.Destroy(chest);
            }
        }
    }

    // ---------- Crafting Search and Sort ----------

    // Rows at the top of the open craft list that belong to these kinds (the list as the game shows it now).
    private static List<string> LeadingRows(Func<ItemKind, bool> belongs, out int total)
    {
        var rows = InventoryGui.instance.m_availableRecipes;
        var list = new List<string>();
        total = rows.Count;
        foreach (var row in rows)
        {
            var recipe = row.Recipe;
            var shared = recipe != null && recipe.m_item != null ? recipe.m_item.m_itemData.m_shared : null;
            if (shared == null || !belongs(ItemKinds.Classify(shared)))
            {
                break;
            }
            list.Add(recipe.m_item.gameObject.name);
        }
        return list;
    }

    private static IEnumerator RunSortCrafting()
    {
        var c = new Checks(SortCraftingTest);
        if (!Ready(SortCraftingTest, out var player, out _))
        {
            yield break;
        }
        if (!OtherModActive(CraftSortGuid))
        {
            SelfTest.Fail(SortCraftingTest, "Crafting Search and Sort is not loaded and active: the crafting part of TESTING.md X02 was not checked");
            yield break;
        }
        var rig = new SpyRig(player, SortCraftingTest);
        try
        {
            var remember = OtherSetting(CraftSortGuid, "General", "RememberSort");
            if (!c.Check(remember == "True", $"Crafting Search and Sort remembers the sort of a station in this game (RememberSort = {remember ?? "not found"}): the test picks the sort that way"))
            {
                c.Report();
                yield break;
            }
            // Recipe at the workbench for the test (RecipeStation setting, in memory): there the list has the hammer
            // and the hoe to stand next to.
            KeepKnown(rig);
            rig.OnRestore("recipe", () =>
            {
                ServerRules.TestRules = null;
                SpyglassContent.Rebuild();
            });
            ServerRules.TestRules = new SpyglassRules { RecipeStation = "piece_workbench" };
            SpyglassContent.Rebuild();
            var recipe = SpyglassContent.CraftRecipe;
            var station = SpawnStation(c, rig);
            if (station == null || !c.Check(recipe != null && recipe.m_enabled && recipe.m_craftingStation != null && recipe.m_craftingStation.m_name == station.m_name,
                    "the spyglass recipe is at the workbench for the test"))
            {
                c.Report();
                yield break;
            }
            foreach (var name in new[] { "Hammer", "Hoe", "Club" })
            {
                var shared = SharedName(name);
                if (shared != null)
                {
                    player.m_knownRecipes.Add(shared);
                }
            }
            player.m_knownRecipes.Add(recipe.m_item.m_itemData.m_shared.m_name);

            // The sort a player picked at this station is kept with the character (custom data); me write it there.
            const string Key = CraftSortGuid + ".Sort";
            var had = player.m_customData.TryGetValue(Key, out var saved);
            rig.OnRestore("saved sort", () =>
            {
                if (had)
                {
                    player.m_customData[Key] = saved;
                }
                else
                {
                    player.m_customData.Remove(Key);
                }
            });
            var row = new int[1];
            var errors = _tap != null ? _tap.ErrorMark : 0;
            var leading = new List<string>[2];
            var totals = new int[2];
            var options = new[] { "tools", "weapons" };
            for (var pass = 0; pass < 2; pass++)
            {
                player.m_customData[Key] = station.m_name + "=" + options[pass];
                // Hand crafting list first: the station's saved sort is read when the station changes.
                player.SetCraftingStation(null);
                InventoryGui.instance.Show(null, 3);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return CloseStation(player);
                yield return OpenStation(player, station, row);
                leading[pass] = pass == 0
                    ? LeadingRows(k => k == ItemKind.Tool || k == ItemKind.SkillTool || k == ItemKind.Torch, out totals[pass])
                    : LeadingRows(k => k == ItemKind.Weapon, out totals[pass]);
                if (pass == 0)
                {
                    c.Check(row[0] >= 0, $"the spyglass is in the workbench's craft list ({totals[pass]} rows)");
                    SelfTest.Screenshot(SortCraftingTest, "tools");
                    yield return null;
                    yield return null;
                }
                yield return CloseStation(player);
            }
            c.Note($"sorted by \"Tools and light\" the list starts with: {string.Join(", ", leading[0].ToArray())}; by \"Weapons\": {string.Join(", ", leading[1].ToArray())}");
            c.Check(leading[0].Contains(SpyglassContent.ItemName) && leading[0].Contains("Hammer") && leading[0].Count < totals[0],
                $"sorted by \"Tools and light\": the spyglass is in the block of tools at the top of the list, with the hammer ({leading[0].Count} of {totals[0]} rows)");
            if (SharedName("Hoe") != null)
            {
                c.Check(leading[0].Contains("Hoe"), "and with the hoe");
            }
            c.Check(!leading[0].Contains("Club"), "the club is not in that block");
            c.Check(leading[1].Contains("Club") && !leading[1].Contains(SpyglassContent.ItemName) && !leading[1].Contains("Hammer"),
                $"sorted by \"Weapons\": the club leads the list, the spyglass and the hammer are not among the weapons ({leading[1].Count} of {totals[1]} rows)");
            var logged = _tap != null ? _tap.ErrorsSince(errors) : new List<string>();
            c.Check(_tap != null && logged.Count == 0, "no error logged meanwhile" + (logged.Count == 0 ? "" : ": " + First(logged)));
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }
}
#endif
