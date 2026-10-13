#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using MC.Shared;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Exploration.MusicInstrumentsMod;

// Debug build only. Self tests with another MC mod loaded next to this one (same class as SelfTests.cs). Every MC mod
// is loaded in a test run; a test fails with a clear line when its other mod is not loaded and active. The other mod
// is reached by the game-level effect, or by reflection on its plugin (never its types at compile time).
//   music.x.spyglass   X01  Spyglass: a real left click raises it (its own flag, view zoomed), the flute in hand
//                           lowers it and the view is normal, the click then opens the song window, the song plays;
//                           spyglass again mid-song: song over, click raises the spyglass again
//   music.x.search     X03  Crafting Search and Sort, Encyclopedia: their search field used (text set, as typing
//                           leaves it), closed, then the rhythm game with real lane keys, a real right click, real W:
//                           nothing holds the keys
//   music.x.sortchest  X04  Sort Chest: a real chest with the three instruments, a hammer, swords and other things,
//                           sorted by its own Sort: the instruments end up with the hammer
//   music.x.dualwield  X05  Dual Wielding: a real pair in hand, an instrument equipped: pair put away; a weapon
//                           equipped next to the instrument: never paired
internal static partial class SelfTests
{
    private const string XSpyglassName = "music.x.spyglass";
    private const string XSearchName = "music.x.search";
    private const string XSortName = "music.x.sortchest";
    private const string XDualName = "music.x.dualwield";

    private const string SpyglassGuid = "MC.Exploration.View.Spyglass";
    private const string CraftSearchGuid = "MC.UX.Crafting.SearchSort";
    private const string EncyclopediaGuid = "MC.Exploration.Compendium.Encyclopedia";
    private const string SortChestGuid = "MC.UX.Container.Sort";
    private const string DualWieldGuid = "MC.Combat.Weapons.DualWield";

    private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    // ---------- other mod helpers ----------

    private static bool OtherActive(Checks c, string guid, string name)
    {
        var view = FeatureRegistry.Find(guid);
        var ok = view != null && view.Value.IsActive;
        c.Check(ok, $"{name} ({guid}) is loaded and active ({(view == null ? "not installed" : view.Value.State)}): without it the two mods cannot be checked together");
        return ok;
    }

    // Type of another plugin by its short name (their namespace is their business). Null = plugin or type missing.
    private static Type OtherType(string guid, string typeName)
    {
        if (!Chainloader.PluginInfos.TryGetValue(guid, out var info) || info == null || info.Instance == null)
        {
            return null;
        }
        Type[] types;
        try
        {
            types = info.Instance.GetType().Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            types = e.Types;
        }
        foreach (var t in types)
        {
            if (t != null && t.Name == typeName)
            {
                return t;
            }
        }
        return null;
    }

    // Static property or field of their type. Null = not there.
    private static object OtherValue(Type type, string member)
    {
        if (type == null)
        {
            return null;
        }
        try
        {
            var property = type.GetProperty(member, AnyStatic);
            if (property != null)
            {
                return property.GetValue(null, null);
            }
            var field = type.GetField(member, AnyStatic);
            return field != null ? field.GetValue(null) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool? OtherBool(Type type, string member) => OtherValue(type, member) as bool?;

    // Static method of their type with these arguments. Null in found[0] = no such method.
    private static object OtherCall(Type type, string method, bool[] found, params object[] args)
    {
        found[0] = false;
        if (type == null)
        {
            return null;
        }
        var types = new Type[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            types[i] = args[i].GetType();
        }
        var m = type.GetMethod(method, AnyStatic, null, types, null);
        if (m == null)
        {
            return null;
        }
        found[0] = true;
        return m.Invoke(null, args);
    }

    // ---------- music.x.spyglass (X01) ----------

    private static IEnumerator RunXSpyglass()
    {
        var c = new Checks(XSpyglassName);
        var player = Player.m_localPlayer;
        var camera = Utils.GetMainCamera();
        const string spyglassItem = "MC_Spyglass";
        if (player == null || camera == null || ObjectDB.instance == null)
        {
            SelfTest.Fail(XSpyglassName, "no player or camera");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        Type scope = null;
        FieldInfo hold = null;
        var found = new bool[1];
        try
        {
            if (!OtherActive(c, SpyglassGuid, "Spyglass") || !InputReady(c))
            {
                c.Report();
                yield break;
            }
            scope = OtherType(SpyglassGuid, "Scope");
            bool? Raised() => OtherBool(scope, "Engaged");
            var attackPath = ButtonPath("Attack");
            if (!c.Check(Raised() != null && ObjectDB.instance.GetItemPrefab(spyglassItem) != null, "Spyglass's item and its own \"raised\" flag (Scope.Engaged) are found")
                || !c.Check(attackPath == LeftMousePath, $"the attack button is the left mouse button (bound to '{attackPath}'): the test can only press that one"))
            {
                c.Report();
                yield break;
            }
            // One click raises it and it stays up, whatever this player's HoldToLook says: Spyglass's own in-memory
            // test switch, never its config.
            hold = scope.GetField("TestHoldToLook", AnyStatic);
            if (hold != null && hold.FieldType == typeof(bool?))
            {
                hold.SetValue(null, (bool?)false);
            }
            else
            {
                hold = null;
                c.Note("Spyglass's test switch for \"hold to look\" was not found: this player's own setting is used");
            }
            rig.TakeControls();
            ServerRules.TestRules = TestRules();
            x.NoShare();
            PadInput.Test = false;
            var spyglass = rig.Give(spyglassItem);
            var flute = rig.Give(InstrumentContent.ItemName(InstrumentKind.Flute));
            if (!c.Check(spyglass != null && flute != null && player.EquipItem(spyglass, false), "spyglass in hand, a flute in the inventory"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(0.8f);
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            var view = camera.fieldOfView;
            // The game reads the mouse itself from here on.
            x.Controller(true);

            // Raised for real.
            yield return SelfTest.KeyTap(KeyCode.Mouse0);
            yield return WaitReal(() => Raised() == true, 2.5f);
            yield return new WaitForSeconds(1.5f);
            var zoomed = camera.fieldOfView;
            c.Check(Raised() == true, "a real left click with the spyglass in hand raises it");
            c.Check(zoomed < view - 5f, $"the view is zoomed in ({F(zoomed)} degrees, {F(view)} normally)");
            c.Check(!SongWindow.IsOpen && Performance.Mode == PerformanceMode.None && Performance.Held == InstrumentKind.None && !KeyCapture.Active,
                "with the spyglass in hand nothing of the instruments acts");

            // The instrument takes the hands while the spyglass is up.
            player.EquipItem(flute, false);
            yield return WaitReal(() => Raised() == false, 2.5f);
            yield return new WaitForSeconds(1.2f);
            c.Check(player.GetRightItem() == flute && !player.IsItemEquiped(spyglass), "the flute puts the spyglass away");
            c.Check(Raised() == false && Mathf.Abs(camera.fieldOfView - view) < 1f,
                $"the spyglass is down and the view is the normal one again ({F(camera.fieldOfView)} degrees)");
            // Spyglass's own word for "camera, dark ring or arm pose still shown" (Scope.Active).
            c.Check(OtherBool(scope, "Active") == false,
                $"nothing of the spyglass is left with the flute in hand: no view, ring or arm pose of it (its Scope.Active says {OtherBool(scope, "Active")})");
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            yield return SelfTest.KeyTap(KeyCode.Mouse0);
            yield return WaitReal(() => SongWindow.IsOpen, 1.5f);
            c.Check(SongWindow.IsOpen && Raised() == false, "a real left click with the flute opens the song window, the spyglass stays down");
            c.Check(SongWindow.TestSelect("preset:greensleeves") && SongWindow.TestPress("Play"), "Play");
            yield return new WaitForSeconds(1.3f);
            var emitter = Performance.LocalEmitter;
            var rms = new float[1];
            yield return Measure(emitter, 0.5f, rms);
            c.Check(Performance.Mode == PerformanceMode.Auto && InstrumentPose.Weight(player) > 0.9f && rms[0] > 0.002f && Mathf.Abs(camera.fieldOfView - view) < 1f,
                $"the flute plays: posed, heard, normal view (rms {F(rms[0])})");

            // The spyglass again, mid-song.
            player.EquipItem(spyglass, false);
            yield return Frames(3);
            c.Check(player.GetRightItem() == spyglass && Performance.Mode == PerformanceMode.None, "the spyglass in hand stops the song at once");
            yield return new WaitForSeconds(0.8f);
            yield return Measure(emitter, 0.4f, rms);
            c.Check(InstrumentPose.Weight(player) < 0.01f && PlayingFlag(player) == 0 && rms[0] < 0.0002f, "no playing pose and no sound left");
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            yield return SelfTest.KeyTap(KeyCode.Mouse0);
            yield return WaitReal(() => Raised() == true, 2.5f);
            yield return new WaitForSeconds(1.5f);
            c.Check(Raised() == true && !SongWindow.IsOpen && !Performance.WindowOpen, "a real left click raises the spyglass again and opens no song window");
            c.Check(camera.fieldOfView < view - 5f, $"zoomed in again ({F(camera.fieldOfView)} degrees)");
            if (hold != null)
            {
                // Click again: down.
                yield return SelfTest.KeyTap(KeyCode.Mouse0);
                yield return WaitReal(() => Raised() == false, 2.5f);
                yield return new WaitForSeconds(1.2f);
                yield return WaitReal(() => OtherBool(scope, "Active") == false, 2f);
                c.Check(Raised() == false && OtherBool(scope, "Active") == false && Mathf.Abs(camera.fieldOfView - view) < 1f && !SongWindow.IsOpen,
                    $"a click lowers it: normal view, nothing stuck ({F(camera.fieldOfView)} degrees, its Scope.Active says {OtherBool(scope, "Active")})");
            }
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            try
            {
                if (OtherBool(scope, "Engaged") == true)
                {
                    OtherCall(scope, "Abort", found);
                }
                if (hold != null)
                {
                    hold.SetValue(null, null);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up (spyglass) failed: {e.Message}");
            }
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.x.search (X03) ----------

    // The running rhythm game played with real key presses for a while. result[0] = presses, result[1] = hits.
    private static IEnumerator PlayLanesForReal(MiniGame game, float seconds, int[] result)
    {
        var pressed = new HashSet<long>();
        var down = KeyCode.None;
        var releaseAt = 0;
        var began = Time.realtimeSinceStartup;
        while (game != null && Time.realtimeSinceStartup - began < seconds && Performance.Mode == PerformanceMode.MiniGame)
        {
            if (down != KeyCode.None && Time.frameCount >= releaseAt)
            {
                SelfTest.Key(down, false);
                down = KeyCode.None;
            }
            else if (down == KeyCode.None)
            {
                foreach (var v in game.Visible)
                {
                    var key = NoteKey(v);
                    if (!pressed.Contains(key) && Due(game, game.NoteTime(v.Key, v.Value), -0.03f))
                    {
                        pressed.Add(key);
                        down = LaneKeys.Key(game.Lane(v.Value));
                        if (down != KeyCode.None)
                        {
                            SelfTest.Key(down, true);
                        }
                        releaseAt = Time.frameCount + 3;
                        break;
                    }
                }
            }
            yield return null;
        }
        if (down != KeyCode.None)
        {
            SelfTest.Key(down, false);
        }
        result[0] = pressed.Count;
        result[1] = game != null ? game.Judge.Hits : 0;
    }

    // After another mod's screen was used and closed: perform with real keys, stop with a real right click, walk
    // with real W. Nothing may still hold the keys.
    private static IEnumerator KeysNormalAfter(Checks c, Extra x, Player player, string label)
    {
        yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
        c.Check(!GameScreens.AnyOpen() && !GameScreens.TextFieldHasKeyboard() && !Chat.instance.HasFocus(),
            label + ": no screen and no text field counts as open any more");
        if (!c.Check(Performance.StartMiniGame(Preset("kjerringa-med-staven"), -1, out var error), label + ": performs: " + error))
        {
            yield break;
        }
        yield return Frames(2);
        var game = Performance.Game;
        var at = player.transform.position;
        var result = new int[2];
        yield return PlayLanesForReal(game, Judge.LeadIn + 5f, result);
        yield return Frames(3);
        c.Check(result[0] >= 4 && result[1] >= result[0] - 2, $"{label}: the real lane keys hit the notes ({result[1]} hits for {result[0]} presses)");
        c.Check(Flat(player.transform.position, at) < 0.05f && Performance.Mode == PerformanceMode.MiniGame, label + ": and do nothing else (D is a walking key)");
        yield return SelfTest.KeyTap(KeyCode.Mouse1, 0.2f);
        yield return WaitReal(() => Performance.Mode == PerformanceMode.None, 1.5f);
        yield return Frames(5);
        var controller = x.ControllerObject;
        c.Check(Performance.Mode == PerformanceMode.None && !KeyCapture.Active && !Chat.instance.HasFocus(), label + ": a real right click stops the rhythm game and the keys are free");
        c.Check(controller != null && controller.TakeInput(false) && player.TakeInput(), label + ": the game takes every key again");
        var forwardPath = ButtonPath("Forward");
        if (forwardPath == ForwardKeyPath)
        {
            var from = player.transform.position;
            SelfTest.Key(KeyCode.W, true);
            yield return new WaitForSeconds(0.8f);
            SelfTest.Key(KeyCode.W, false);
            yield return Frames(4);
            c.Check(Flat(player.transform.position, from) > 0.3f, $"{label}: the real W key walks the player again ({F(Flat(player.transform.position, from))} m)");
        }
        else
        {
            c.Note($"{label}: this player's Forward key is '{forwardPath}', not W: the walk with a real key was not tried");
        }
    }

    private static IEnumerator RunXSearch()
    {
        var c = new Checks(XSearchName);
        var player = Player.m_localPlayer;
        if (player == null || InventoryGui.instance == null || Chat.instance == null)
        {
            SelfTest.Fail(XSearchName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        var found = new bool[1];
        try
        {
            var craftOn = OtherActive(c, CraftSearchGuid, "Crafting Search and Sort");
            var bookOn = OtherActive(c, EncyclopediaGuid, "Encyclopedia");
            if (!InputReady(c) || (!craftOn && !bookOn))
            {
                c.Report();
                yield break;
            }
            rig.TakeControls();
            ServerRules.TestRules = TestRules(60f);
            x.NoShare();
            PadInput.Test = false;
            LaneKeys.TestSet(0, KeyCode.D);
            LaneKeys.TestSet(1, KeyCode.F);
            LaneKeys.TestSet(2, KeyCode.J);
            LaneKeys.TestSet(3, KeyCode.K);
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            var gui = InventoryGui.instance;

            if (craftOn)
            {
                // The crafting search of the open inventory.
                gui.Show(null);
                yield return Real(0.7f);
                var searchUi = OtherType(CraftSearchGuid, "SearchUi");
                var field = OtherValue(searchUi, "Field") as TMP_InputField;
                if (c.Check(InventoryGui.IsVisible() && field != null && field.gameObject.activeInHierarchy,
                        "the crafting search field of Crafting Search and Sort is on the open inventory"))
                {
                    var before = field.text;
                    OtherCall(searchUi, "FocusField", found); // what a click on their field does
                    yield return Frames(3);
                    field.text = "flu"; // what typing leaves in the field (its change event runs their search)
                    yield return Frames(4);
                    c.Note($"crafting search: text '{field.text}', the field says focused: {field.isFocused} (a text field may not take the keyboard while the game window is behind another one)");
                    c.Check(field.text == "flu" && InventoryGui.IsVisible(), "their search takes the text and stays open");
                    c.Check(!SongWindow.IsOpen && !Performance.WindowOpen && !KeyCapture.Active && Performance.Mode == PerformanceMode.None && GameScreens.AnyOpen(),
                        "with their search in use the mod opens nothing and holds no keys");
                    field.text = before;
                    yield return Frames(2);
                    OtherCall(searchUi, "DropFocus", found);
                }
                gui.Hide();
                x.Controller(true);
                yield return KeysNormalAfter(c, x, player, "after the crafting search");
                x.Controller(false);
                rig.TakeControls();
            }

            if (bookOn)
            {
                // The Encyclopedia window (its own Open: what its tab in the Valheim Compendium calls) and its search.
                yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
                gui.Show(null);
                yield return Real(0.7f);
                var window = OtherType(EncyclopediaGuid, "CompendiumWindow");
                var opened = OtherCall(window, "Open", found) is bool b && b;
                yield return Frames(5);
                var search = OtherValue(window, "SearchField") as TMP_InputField;
                if (c.Check(opened && OtherBool(window, "IsOpen") == true && search != null && search.gameObject.activeInHierarchy,
                        "the Encyclopedia window is open with its search field"))
                {
                    var before = search.text;
                    search.ActivateInputField();
                    yield return Frames(3);
                    search.text = "boar";
                    yield return Frames(4);
                    c.Note($"Encyclopedia search: text '{search.text}', the field says focused: {search.isFocused}");
                    c.Check(search.text == "boar" && OtherBool(window, "IsOpen") == true, "its search takes the text and the window stays open");
                    c.Check(!SongWindow.IsOpen && !Performance.WindowOpen && !KeyCapture.Active && Performance.Mode == PerformanceMode.None && GameScreens.AnyOpen(),
                        "with the Encyclopedia open the mod opens nothing and holds no keys");
                    search.text = before;
                    yield return Frames(2);
                    OtherCall(window, "Close", found, false);
                    yield return Frames(2);
                    c.Check(OtherBool(window, "IsOpen") == false, "the Encyclopedia window closed");
                }
                gui.Hide();
                x.Controller(true);
                yield return KeysNormalAfter(c, x, player, "after the Encyclopedia");
                x.Controller(false);
                rig.TakeControls();
            }
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            try
            {
                var window = OtherType(EncyclopediaGuid, "CompendiumWindow");
                if (OtherBool(window, "IsOpen") == true)
                {
                    OtherCall(window, "Close", found, false);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up (search) failed: {e.Message}");
            }
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.x.sortchest (X04) ----------

    private static string PrefabNameOf(ItemDrop.ItemData item) =>
        item == null ? "nothing" : item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared.m_name;

    private static List<string> ChestOrder(Inventory inventory)
    {
        var items = new List<ItemDrop.ItemData>(inventory.GetAllItems());
        items.Sort((a, b) => a.m_gridPos.y != b.m_gridPos.y ? a.m_gridPos.y.CompareTo(b.m_gridPos.y) : a.m_gridPos.x.CompareTo(b.m_gridPos.x));
        var names = new List<string>();
        foreach (var item in items)
        {
            names.Add(PrefabNameOf(item));
        }
        return names;
    }

    // The tools of the list sit next to each other (no other thing between them).
    private static bool Together(List<string> order, List<string> tools)
    {
        var min = int.MaxValue;
        var max = -1;
        var n = 0;
        for (var i = 0; i < order.Count; i++)
        {
            if (tools.Contains(order[i]))
            {
                n++;
                min = Math.Min(min, i);
                max = Math.Max(max, i);
            }
        }
        return n == tools.Count && max - min == tools.Count - 1;
    }

    private static IEnumerator RunXSort()
    {
        var c = new Checks(XSortName);
        var player = Player.m_localPlayer;
        if (player == null || InventoryGui.instance == null || ZNetScene.instance == null)
        {
            SelfTest.Fail(XSortName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        FieldInfo forced = null;
        Container chest = null;
        var found = new bool[1];
        try
        {
            if (!OtherActive(c, SortChestGuid, "Sort Chest"))
            {
                c.Report();
                yield break;
            }
            rig.TakeControls();
            var sorter = OtherType(SortChestGuid, "ContainerSorter");
            var prefab = ZNetScene.instance.GetPrefab("piece_chest_wood");
            if (!c.Check(sorter != null && prefab != null, "Sort Chest's sorter and the wooden chest are found"))
            {
                c.Report();
                yield break;
            }
            // Sort by type, whatever this player's setting: Sort Chest's own in-memory test switch, never its config.
            var plugin = Chainloader.PluginInfos[SortChestGuid].Instance;
            var field = plugin.GetType().GetField("TestSortBy", AnyStatic);
            var enumType = field != null ? Nullable.GetUnderlyingType(field.FieldType) : null;
            if (enumType != null && enumType.IsEnum && Enum.IsDefined(enumType, "Type"))
            {
                field.SetValue(null, Enum.Parse(enumType, "Type"));
                forced = field;
            }
            else
            {
                object value = null;
                foreach (var key in plugin.Config.Keys)
                {
                    if (key.Key == "SortBy")
                    {
                        value = plugin.Config[key].BoxedValue;
                    }
                }
                if (!c.Check(value != null && value.ToString() == "Type", $"Sort Chest sorts by type (it sorts by '{value}', and its test switch was not found): the grouping cannot be checked"))
                {
                    c.Report();
                    yield break;
                }
            }
            var forward = player.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            var spot = player.transform.position + forward * 1.6f;
            spot.y = ZoneSystem.instance.GetGroundHeight(spot);
            var chestObject = Object.Instantiate(prefab, spot, Quaternion.LookRotation(-forward));
            rig.Spawned(chestObject);
            yield return new WaitForSeconds(0.6f);
            chest = chestObject != null ? chestObject.GetComponentInChildren<Container>() : null;
            if (!c.Check(chest != null, "a wooden chest stands in front of the player"))
            {
                c.Report();
                yield break;
            }
            var inventory = chest.GetInventory();
            var flute = InstrumentContent.ItemName(InstrumentKind.Flute);
            var lyre = InstrumentContent.ItemName(InstrumentKind.Lyre);
            var tambourine = InstrumentContent.ItemName(InstrumentKind.Tambourine);
            var tools = new List<string> { flute, lyre, tambourine, "Hammer" };
            // Mixed on purpose: a sword, an instrument, wood, the hammer, arrows...
            var put = new[] { "SwordIron", flute, "Wood", "Hammer", "ArrowWood", lyre, "SwordBronze", tambourine };
            var all = true;
            foreach (var name in put)
            {
                if (inventory.AddItem(name, 1, 1, 0, 0L, "", false) == null)
                {
                    all = false;
                    c.Check(false, $"could not put {name} in the chest");
                }
            }
            yield return null;
            var first = ChestOrder(inventory);
            c.Check(all && first.Count == put.Length && !Together(first, tools), $"the chest holds the eight things, the tools apart ({string.Join(", ", first.ToArray())})");
            var gui = InventoryGui.instance;
            // The Use key on the chest.
            chest.Interact(player, false, false);
            yield return WaitReal(() => ReferenceEquals(gui.m_currentContainer, chest), 2f);
            if (!ReferenceEquals(gui.m_currentContainer, chest))
            {
                gui.Show(chest);
                yield return WaitReal(() => ReferenceEquals(gui.m_currentContainer, chest), 2f);
            }
            yield return Real(0.5f);
            c.Check(ReferenceEquals(gui.m_currentContainer, chest) && InventoryGui.IsVisible(), "the chest is open");
            OtherCall(sorter, "TrySortOpenContainer", found); // what Sort Chest's button runs
            c.Check(found[0], "Sort Chest's Sort ran on the open chest");
            yield return WaitReal(() => Together(ChestOrder(inventory), tools), 1.5f);
            var order = ChestOrder(inventory);
            c.Note("after the sort: " + string.Join(", ", order.ToArray()));
            c.Check(order.Count == put.Length, $"nothing lost or doubled by the sort ({order.Count} things)");
            c.Check(Together(order, tools), "the three instruments end up with the hammer: the four tools sit together, no sword, wood or arrow between them");
            var swords = new List<string> { "SwordIron", "SwordBronze" };
            c.Check(Together(order, swords), "(the two swords sit together too: the chest is sorted by kind)");
            gui.Hide();
            yield return null;
            c.Report();
        }
        finally
        {
            try
            {
                if (forced != null)
                {
                    forced.SetValue(null, null);
                }
                if (InventoryGui.instance != null && InventoryGui.IsVisible())
                {
                    InventoryGui.instance.Hide();
                }
                if (chest != null)
                {
                    chest.GetInventory().RemoveAll();
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up (sort chest) failed: {e.Message}");
            }
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.x.dualwield (X05) ----------

    private static string HandsOf(Player player) =>
        $"right {PrefabNameOf(player.GetRightItem())}, left {PrefabNameOf(player.GetLeftItem())}";

    private static IEnumerator RunXDual()
    {
        var c = new Checks(XDualName);
        var player = Player.m_localPlayer;
        const string knife = "KnifeCopper";
        if (player == null || ObjectDB.instance == null)
        {
            SelfTest.Fail(XDualName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            if (!OtherActive(c, DualWieldGuid, "Dual Wielding"))
            {
                c.Report();
                yield break;
            }
            rig.TakeControls();
            ServerRules.TestRules = TestRules();
            x.NoShare();
            var first = rig.Give(knife);
            var second = rig.Give(knife);
            var flute = rig.Give(InstrumentContent.ItemName(InstrumentKind.Flute));
            if (!c.Check(first != null && second != null && flute != null, $"two {knife} and a flute in the inventory"))
            {
                c.Report();
                yield break;
            }
            foreach (var hand in new[] { player.GetRightItem(), player.GetLeftItem() })
            {
                if (hand != null)
                {
                    player.UnequipItem(hand, false);
                }
            }
            yield return Frames(2);
            player.EquipItem(first, false);
            player.EquipItem(second, false);
            yield return new WaitForSeconds(0.7f);
            if (!c.Check(ReferenceEquals(player.GetRightItem(), first) && ReferenceEquals(player.GetLeftItem(), second),
                    $"Dual Wielding pairs the two knives, one in each hand ({HandsOf(player)})"))
            {
                c.Report();
                yield break;
            }

            // An instrument with the pair in hand: the pair is put away.
            c.Check(player.EquipItem(flute, false), "the flute is equipped with the pair in hand");
            yield return new WaitForSeconds(0.7f);
            c.Check(ReferenceEquals(player.GetRightItem(), flute) && player.GetLeftItem() == null && !player.IsItemEquiped(first) && !player.IsItemEquiped(second),
                $"the pair is put away, the flute alone in the hands ({HandsOf(player)})");
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            c.Check(Performance.StartAuto(Preset("greensleeves"), -1, out var error), "and it plays: " + error);
            yield return new WaitForSeconds(0.8f);
            c.Check(Performance.Mode == PerformanceMode.Auto && player.GetLeftItem() == null, "playing, the other hand stays empty");

            // A weapon equipped next to the instrument: never a pair with it.
            player.EquipItem(first, false);
            yield return new WaitForSeconds(0.7f);
            c.Check(!(player.IsItemEquiped(flute) && player.IsItemEquiped(first)), $"a knife equipped while the flute is held is never paired with it ({HandsOf(player)})");
            c.Check(Performance.Mode == PerformanceMode.None, "(the song is over: the flute left the hand)");
            c.Check(player.IsItemEquiped(first) && !player.IsItemEquiped(flute), $"the knife took the hand, the flute is put away ({HandsOf(player)})");
            // The flute again, then the other knife.
            player.EquipItem(flute, false);
            yield return new WaitForSeconds(0.7f);
            c.Check(ReferenceEquals(player.GetRightItem(), flute) && player.GetLeftItem() == null && !player.IsItemEquiped(first),
                $"the flute again: alone in the hands ({HandsOf(player)})");
            player.EquipItem(second, false);
            yield return new WaitForSeconds(0.7f);
            c.Check(!(player.IsItemEquiped(flute) && player.IsItemEquiped(second)), $"the other knife is not paired with the flute either ({HandsOf(player)})");
            // And the knives still pair with each other afterwards.
            player.EquipItem(first, false);
            yield return new WaitForSeconds(0.7f);
            c.Check(player.IsItemEquiped(first) && player.IsItemEquiped(second) && !player.IsItemEquiped(flute), $"the two knives pair again afterwards ({HandsOf(player)})");
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }
}
#endif
