#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Exploration.ViewSpyglassMod;

// Debug build only. Me = self tests that go through a real log out or a real restart of the game (TESTING.md items in
// brackets), and the list of every test added with them (files SelfTestsReal.cs, SelfTestsRealInput.cs,
// SelfTestsOtherMods.cs, SelfTestsMultiplayerMore.cs). One small test per thing:
//   spyglass.relog.logout          raise, pause menu (it comes down behind it), raise again and log out with it up:
//                                  nothing of it on any frame of the main menu, character select and loading; back in
//                                  the world the view is normal, the spyglass came back with the character and a
//                                  click raises it; clean log all the way [T22 T23]
//   spyglass.relog.dropped         a spyglass on the ground is still there after the world was saved, closed and
//                                  loaded again (same save and load a dedicated server restart does), still the
//                                  spyglass model, can be picked up [M06 part]
//   spyglass.disabled.keeps-item   second game, every MC mod Enabled = false from the start: the spyglass left in the
//                                  inventory and the one on the ground are still there and whole, recipe hidden (not
//                                  in the Forge's list of a player who knows it), Status says off, clean log [T19 T23]
// Relog tests registered in OnActivated, disabled-start test in BindConfig (mod never active in the second game).
// Things left over a log out or restart carry a mark (item custom data): the part after finds and removes them.
internal static partial class SelfTests
{
    private const string RelogLogoutTest = "spyglass.relog.logout";
    private const string RelogDroppedTest = "spyglass.relog.dropped";
    private const string DisabledItemTest = "spyglass.disabled.keeps-item";

    private const string TestMarkKey = ModInfo.Guid + ".SelfTest";
    private const string RelogMark = "relog";
    private const string DroppedMark = "relog-dropped";
    private const string DisabledMark = "disabled";

    // In-world tests added after the first lot, in run order. The two death tests last: each leaves a new player
    // object.
    private static readonly KeyValuePair<string, Func<IEnumerator>>[] AutoTests =
    {
        new KeyValuePair<string, Func<IEnumerator>>(InputClickTest, RunInputClick),
        new KeyValuePair<string, Func<IEnumerator>>(InputEscTest, RunInputEsc),
        new KeyValuePair<string, Func<IEnumerator>>(InputSitTest, RunInputSit),
        new KeyValuePair<string, Func<IEnumerator>>(InputDualTest, RunInputDualWield),
        new KeyValuePair<string, Func<IEnumerator>>(InputDiveTest, RunInputDive),
        new KeyValuePair<string, Func<IEnumerator>>(PictureTest, RunPicture),
        new KeyValuePair<string, Func<IEnumerator>>(RenderScaleTest, RunRenderScale),
        new KeyValuePair<string, Func<IEnumerator>>(CloseRealTest, RunCloseReal),
        new KeyValuePair<string, Func<IEnumerator>>(HorizonsSettingsTest, RunHorizonsSettings),
        new KeyValuePair<string, Func<IEnumerator>>(HorizonsDetailTest, RunHorizonsDetail),
        new KeyValuePair<string, Func<IEnumerator>>(SortChestTest, RunSortChest),
        new KeyValuePair<string, Func<IEnumerator>>(SortCraftingTest, RunSortCrafting),
        new KeyValuePair<string, Func<IEnumerator>>(DeathTest, RunDeath),
        new KeyValuePair<string, Func<IEnumerator>>(DeathCameraBugTest, RunDeathCameraBug),
    };

    private static readonly KeyValuePair<string, Func<IEnumerator>>[] AutoMpTests =
    {
        new KeyValuePair<string, Func<IEnumerator>>(MpNoHorizonsTest, RunMpNoHorizons),
        // Last: it leaves this game at the main menu (probe joins again before the next test).
        new KeyValuePair<string, Func<IEnumerator>>(MpLeaveOffTest, RunMpLeaveModOff),
    };

    // OnActivated (Register), after every older test.
    private static void RegisterAuto()
    {
        foreach (var test in AutoTests)
        {
            SelfTest.Register(test.Key, test.Value);
        }
        // spyglass.log look back at every test before it: registered again = last again.
        SelfTest.Register(LogTest, RunLog);
        SelfTest.RegisterRelog(RelogLogoutTest, RelogLogoutBefore, RelogLogoutMenu, RelogLogoutAfter);
        SelfTest.RegisterRelog(RelogDroppedTest, RelogDroppedBefore, null, RelogDroppedAfter);
        foreach (var test in AutoMpTests)
        {
            SelfTest.RegisterMultiplayer(test.Key, SelfTest.Modded, test.Value);
        }
        SelfTest.RegisterServerStep(StepOffFor, ServerOffFor);
    }

    // OnDeactivated (Unregister).
    private static void UnregisterAuto()
    {
        foreach (var test in AutoTests)
        {
            SelfTest.Unregister(test.Key);
        }
        SelfTest.UnregisterRelog(RelogLogoutTest);
        SelfTest.UnregisterRelog(RelogDroppedTest);
        foreach (var test in AutoMpTests)
        {
            SelfTest.UnregisterMultiplayer(test.Key);
        }
        SelfTest.UnregisterServerStep(StepOffFor);
    }

    // BindConfig (every start, also while me never get active): log listener from the first line on, the test of the
    // second game (every MC mod off) and the one of a server without me. Never unregistered.
    private static void RegisterStaged()
    {
        try
        {
            InstallTap();
            SelfTest.RegisterDisabledStart(DisabledItemTest, DisabledItemLeave, DisabledItemCheck);
            SelfTest.RegisterMultiplayer(MpLeaveNoModTest, VanillaServerScenario, RunMpLeaveNoMod);
        }
        catch (Exception e)
        {
            // Test plumbing must never stop the mod from starting.
            Log.Warning($"Self test set-up failed: {e}");
        }
    }

    // ---------- marked items ----------

    private static bool HasMark(ItemDrop.ItemData item, string mark) =>
        item != null && item.m_customData != null && item.m_customData.TryGetValue(TestMarkKey, out var m) && m == mark;

    private static ItemDrop.ItemData GiveMarked(Player player, string mark)
    {
        var item = player.GetInventory().AddItem(SpyglassContent.ItemName, 1, 1, 0, 0L, "", false);
        if (item != null)
        {
            item.m_customData[TestMarkKey] = mark;
        }
        return item;
    }

    private static List<ItemDrop.ItemData> MarkedItems(Inventory inventory, string mark)
    {
        var list = new List<ItemDrop.ItemData>();
        foreach (var item in inventory.GetAllItems())
        {
            if (HasMark(item, mark))
            {
                list.Add(item);
            }
        }
        return list;
    }

    private static void RemoveMarked(Player player, string mark)
    {
        if (player == null)
        {
            return;
        }
        foreach (var item in MarkedItems(player.GetInventory(), mark))
        {
            player.UnequipItem(item, false);
            player.GetInventory().RemoveItem(item);
        }
    }

    // Marked spyglasses lying in the loaded world.
    private static List<ItemDrop> MarkedDrops(string mark)
    {
        var list = new List<ItemDrop>();
        foreach (var drop in ItemDrop.s_instances)
        {
            if (drop != null && HasMark(drop.m_itemData, mark))
            {
                list.Add(drop);
            }
        }
        return list;
    }

    private static void DestroyDrops(string mark)
    {
        var scene = ZNetScene.instance;
        if (scene == null)
        {
            return;
        }
        foreach (var drop in MarkedDrops(mark))
        {
            var view = drop.GetComponent<ZNetView>();
            if (view != null && view.IsValid())
            {
                view.ClaimOwnership();
            }
            scene.Destroy(drop.gameObject);
        }
    }

    // World object loaded from the save: the game hands it to this player within a moment (single player owns the
    // zone). Not yet = taken by hand, and said.
    private static IEnumerator OwnDrop(Checks c, ItemDrop drop)
    {
        var view = drop != null ? drop.GetComponent<ZNetView>() : null;
        if (view == null || !view.IsValid())
        {
            yield break;
        }
        yield return WaitReal(() => view == null || !view.IsValid() || view.IsOwner(), 5f);
        if (view != null && view.IsValid() && !view.IsOwner())
        {
            c.Note("the dropped spyglass was not handed to this game within 5 s: ownership taken by hand before the pickup");
            view.ClaimOwnership();
        }
    }

    private static string PrefabName(ItemDrop.ItemData item) => item != null && item.m_dropPrefab != null ? item.m_dropPrefab.name : "";

    // What the hands held before a staged test took them: by prefab name, never a test item.
    private static void HoldAgain(Player player, string prefab)
    {
        if (player == null || string.IsNullOrEmpty(prefab))
        {
            return;
        }
        foreach (var item in player.GetInventory().GetAllItems())
        {
            if (PrefabName(item) == prefab && (item.m_customData == null || !item.m_customData.ContainsKey(TestMarkKey)))
            {
                if (!player.IsItemEquiped(item))
                {
                    player.EquipItem(item, false);
                }
                return;
            }
        }
    }

    // What the spyglass shows or holds right now, readable without a player (main menu too). "" = nothing.
    private static string OnScreen()
    {
        var bad = new List<string>();
        if (Scope.State != ScopeState.Idle)
        {
            bad.Add("state " + Scope.State);
        }
        if (ScopeOverlay.Shown)
        {
            bad.Add("round view shown");
        }
        // Find gives switched-on objects only (the round view object outlives scenes, switched off).
        if (GameObject.Find(ModInfo.Guid + ".Overlay") != null)
        {
            bad.Add("round view object switched on");
        }
        if (ScopeCapture.TestOn)
        {
            bad.Add("frame copy for the blur running");
        }
        if (ScopeCamera.BodyHidden)
        {
            bad.Add("body hidden");
        }
        if (ScopeCamera.FogApplied)
        {
            bad.Add("fog cleared");
        }
        var slot = ViewBoost.Peek();
        if (slot != null && slot[3] > 0.0)
        {
            bad.Add("far-view message on");
        }
        return string.Join(", ", bad.ToArray());
    }

    // Error lines of anyone since the mark that name this mod's code (namespace or GUID, as in a stack trace).
    private static List<string> ErrorsNamingMe(int mark)
    {
        var list = new List<string>();
        if (_tap == null)
        {
            return list;
        }
        var space = typeof(SelfTests).Namespace ?? "";
        foreach (var line in _tap.ErrorsSince(mark))
        {
            if ((space.Length > 0 && line.IndexOf(space + ".", StringComparison.Ordinal) >= 0)
                || line.IndexOf(ModInfo.Guid, StringComparison.Ordinal) >= 0)
            {
                list.Add(line);
            }
        }
        return list;
    }

    // Warning and error lines of this mod since the count taken before.
    private static List<string> ProblemsSince(int count)
    {
        var all = _tap != null ? _tap.Problems() : new List<string>();
        return all.GetRange(Mathf.Min(Mathf.Max(0, count), all.Count), Mathf.Max(0, all.Count - Mathf.Max(0, count)));
    }

    // Me look at every frame between two parts of a relog test (main menu, character select, loading screen, first
    // frames of the world): first thing the spyglass showed, "" = nothing ever.
    private sealed class ScreenWatch : MonoBehaviour
    {
        internal string Seen = "";
        internal int Frames;
        internal int MenuFrames;

        private void LateUpdate()
        {
            Frames++;
            if (FejdStartup.instance != null)
            {
                MenuFrames++;
            }
            if (Seen.Length == 0)
            {
                Seen = OnScreen();
            }
        }
    }

    // ---------- spyglass.relog.logout (T22, T23) ----------

    private static bool _relogLeft;
    private static string _relogRight = "";
    private static string _relogLeftHand = "";
    private static int _relogProblems = -1;
    private static int _relogErrors;
    private static ScreenWatch _relogWatch;

    private static void DropRelogWatch()
    {
        if (_relogWatch != null)
        {
            Object.Destroy(_relogWatch.gameObject);
        }
        _relogWatch = null;
    }

    private static IEnumerator RelogLogoutBefore()
    {
        const string T = RelogLogoutTest;
        var c = new Checks(T);
        _relogLeft = false;
        DropRelogWatch();
        if (!Ready(T, out var player, out _))
        {
            yield break;
        }
        var menu = Menu.instance;
        var ghost = player.InGhostMode();
        var done = false;
        try
        {
            _relogProblems = _tap != null ? _tap.Problems().Count : -1;
            _relogErrors = _tap != null ? _tap.ErrorMark : 0;
            _relogRight = PrefabName(player.GetRightItem());
            _relogLeftHand = PrefabName(player.GetLeftItem());
            // Leftover of a run cut short.
            RemoveMarked(player, RelogMark);
            // A hit would lower it: no monster sees the player meanwhile.
            player.SetGhostMode(true);
            var gone = SendAwayEnemies(player);
            if (gone.Length > 0)
            {
                c.Note("hostile creature(s) at the test player removed before the test: " + gone);
            }
            var spy = GiveMarked(player, RelogMark);
            if (spy != null)
            {
                player.EquipItem(spy, false);
            }
            if (!c.Check(spy != null && player.GetRightItem() == spy && menu != null && _tap != null,
                    "spyglass given and in the right hand, pause menu there, log listened to"))
            {
                c.Report();
                yield break;
            }
            _relogLeft = true;
            // Click to look, whatever the player's HoldToLook says (the player's controller stays on and sends "no
            // button held" every tick). Cleared at the main menu.
            Scope.TestHoldToLook = false;
            yield return RaiseUp(c, player, "before the menu");

            // Esc: the game pauses, the spyglass still comes down behind the menu (real Esc key: spyglass.input.esc).
            menu.Show();
            yield return Seconds(0.3f);
            var paused = Game.IsPaused() && Time.timeScale == 0f;
            yield return WaitReal(() => Scope.State == ScopeState.Idle, 2f);
            var behind = NotBack(player);
            // The round view object itself too, not only the mod's own flag.
            var drawn = OnScreen();
            c.Check(paused, $"the pause menu stops the game (paused {Game.IsPaused()}, time scale {F(Time.timeScale)})");
            c.Check(Scope.State == ScopeState.Idle && behind.Length == 0 && drawn.Length == 0,
                "the spyglass comes down behind the menu although the game is paused" + (behind.Length == 0 ? "" : ": " + behind) + (drawn.Length == 0 ? "" : ": " + drawn));
            // Menu closed again before the log out: the probe logs out from a coroutine, after this frame's updates, so
            // the menu would never get to un-pause the game and the next world would start frozen.
            menu.Hide();
            yield return WaitReal(() => !Menu.IsVisible() && Time.timeScale > 0f, 3f);
            c.Check(!Menu.IsVisible() && Time.timeScale > 0f, "menu closed, game running again");

            // Harder than the item asks: the world closes with the spyglass still up (a lost connection does that).
            yield return RaiseUp(c, player, "before the log out");
            yield return Frames(5);
            c.Check(Scope.State == ScopeState.Raised && ScopeOverlay.Shown, $"logging out with the spyglass up and the round view on screen ({Scope.State})");
            done = Scope.State == ScopeState.Raised;
            c.Report();
        }
        finally
        {
            if (!done)
            {
                // Nothing to carry over the log out: everything back now.
                ClearOverrides();
                Scope.Abort();
                if (menu != null && menu.m_root != null && menu.m_root.gameObject.activeSelf)
                {
                    menu.Hide();
                }
                if (player != null)
                {
                    player.SetGhostMode(ghost);
                    RemoveMarked(player, RelogMark);
                    HoldAgain(player, _relogRight);
                    HoldAgain(player, _relogLeftHand);
                }
                _relogLeft = false;
            }
        }
    }

    private static IEnumerator RelogLogoutMenu()
    {
        const string T = RelogLogoutTest;
        var c = new Checks(T);
        // First frame of the menu already: what is left right after the world closed.
        var first = OnScreen();
        ClearOverrides();
        var go = new GameObject("MC_SpyglassTestScreenWatch");
        Object.DontDestroyOnLoad(go);
        _relogWatch = go.AddComponent<ScreenWatch>();
        yield return Frames(10);
        yield return new WaitForEndOfFrame();
        var left = OnScreen();
        c.Check(FejdStartup.instance != null && Player.m_localPlayer == null && GameCamera.instance == null,
            "at the main menu: no player and no world camera (nothing there can raise a spyglass)");
        c.Check(first.Length == 0 && left.Length == 0,
            first.Length == 0 && left.Length == 0
                ? "main menu: no round view, blur, dark ring, cleared fog or far-view message, from its first frame on"
                : $"main menu: on its first frame '{first}', ten frames later '{left}'");
        SelfTest.Screenshot(T, "menu");
        yield return null;
        yield return null;
        c.Report();
    }

    private static IEnumerator RelogLogoutAfter()
    {
        const string T = RelogLogoutTest;
        var c = new Checks(T);
        var watch = _relogWatch;
        var watched = watch != null;
        var seen = watched ? watch.Seen : "";
        var frames = watched ? watch.Frames : 0;
        var menuFrames = watched ? watch.MenuFrames : 0;
        DropRelogWatch();
        if (!Ready(T, out var player, out var cam))
        {
            _relogLeft = false;
            yield break;
        }
        var rig = new SpyRig(player, T);
        try
        {
            c.Check(watched && menuFrames > 0 && frames > menuFrames && seen.Length == 0,
                seen.Length == 0
                    ? $"nothing of the spyglass on any of {frames} frames from the main menu through the character select and the loading to the world ({menuFrames} of them in the menu)"
                    : $"the spyglass showed between the main menu and the world: {seen}");
            yield return Frames(5);
            yield return new WaitForEndOfFrame();
            var left = NotBack(player);
            var gap = (cam.transform.position - player.m_eye.position).magnitude;
            c.Check(left.Length == 0 && OnScreen().Length == 0, "world loaded again: nothing of the spyglass on screen" + (left.Length == 0 ? "" : ": " + left));
            c.Check(Near(cam.m_camera.fieldOfView, cam.m_fov, 0.01f) && gap > 0.5f && Hud.instance.m_crosshair.enabled,
                $"normal view: zoom {F(cam.m_camera.fieldOfView)} of {F(cam.m_fov)}, camera {F(gap)} m from the eye, crosshair on");
            c.Check(BodyFar(player) == !player.m_lodVisible, "own body as the game wants it");

            var kept = MarkedItems(player.GetInventory(), RelogMark);
            foreach (var item in kept)
            {
                rig.Track(item);
            }
            if (c.Check(_relogLeft && kept.Count == 1 && SpyglassContent.IsSpyglass(kept[0]),
                    $"the spyglass held at the log out came back with the character ({kept.Count} found)"))
            {
                var spy = kept[0];
                rig.TakeControls();
                Scope.TestHoldToLook = false;
                Scope.TestSetMagnification(4f);
                if (player.GetRightItem() != spy)
                {
                    player.EquipItem(spy, false);
                }
                yield return WaitReal(() => Scope.CanRaise(player), 6f);
                c.Check(player.GetRightItem() == spy && Scope.State == ScopeState.Idle, $"it is in the right hand, down ({Scope.State})");
                yield return Click(player);
                yield return WaitReal(() => Scope.State == ScopeState.Raised, 3f);
                yield return Frames(5);
                yield return new WaitForEndOfFrame();
                c.Check(Scope.State == ScopeState.Raised && ScopeOverlay.Shown && Flag(player)
                        && Near(cam.m_camera.fieldOfView, ScopeCamera.ZoomedFov(cam.m_fov, 4f), 0.05f),
                    $"a click raises it again: round view and zoom x4 ({Scope.State}, fov {F(cam.m_camera.fieldOfView)})");
                yield return LowerDown();
                yield return Frames(3);
                c.Check(NotBack(player).Length == 0 && Hud.instance.m_crosshair.enabled, "and it comes down again");
            }
            var problems = ProblemsSince(_relogProblems);
            var named = ErrorsNamingMe(_relogErrors);
            c.Check(_tap != null && _relogProblems >= 0 && problems.Count == 0 && named.Count == 0,
                problems.Count == 0 && named.Count == 0
                    ? "no warning or error from the spyglass, and no error naming its code, from the raise through the log out, the main menu and the reload"
                    : $"{problems.Count} warning or error line(s) from the spyglass and {named.Count} error(s) naming its code since the log out: {First(problems.Count > 0 ? problems : named)}");
            c.Report();
        }
        finally
        {
            rig.Restore();
            RemoveMarked(player, RelogMark);
            HoldAgain(player, _relogRight);
            HoldAgain(player, _relogLeftHand);
            _relogLeft = false;
        }
    }

    // ---------- spyglass.relog.dropped (M06: saved and loaded world) ----------

    private static bool _droppedLeft;
    private static Vector3 _droppedAt;
    private static bool _droppedAutoPickup;

    private static IEnumerator RelogDroppedBefore()
    {
        const string T = RelogDroppedTest;
        var c = new Checks(T);
        _droppedLeft = false;
        if (!Ready(T, out var player, out _))
        {
            yield break;
        }
        _droppedAutoPickup = Player.m_enableAutoPickup;
        var done = false;
        try
        {
            DestroyDrops(DroppedMark);
            RemoveMarked(player, DroppedMark);
            // Out of pickup reach, and nobody picks it up by itself until the part after looked at it.
            Player.m_enableAutoPickup = false;
            var item = GiveMarked(player, DroppedMark);
            if (!c.Check(item != null, "a spyglass to drop"))
            {
                c.Report();
                yield break;
            }
            var at = player.transform.position + player.transform.forward * 3.5f + Vector3.up * 0.5f;
            var drop = ItemDrop.DropItem(item, 1, at, Quaternion.Euler(0f, 30f, 0f));
            player.GetInventory().RemoveItem(item);
            yield return new WaitForSeconds(2f);
            var view = drop != null ? drop.GetComponent<ZNetView>() : null;
            var zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (c.Check(drop != null && zdo != null && HasMark(drop.m_itemData, DroppedMark),
                    "a spyglass lies on the ground as a world object"))
            {
                _droppedAt = drop.transform.position;
                c.Check(zdo.Persistent && zdo.GetPrefab() == SpyglassContent.ItemHash,
                    $"it is saved with the world, under the spyglass's prefab (saved {zdo.Persistent})");
                c.Check(_droppedAt.y > ZoneSystem.instance.GetGroundHeight(_droppedAt) - 0.3f, $"on the ground at {F(_droppedAt)}");
                done = true;
                _droppedLeft = true;
            }
            c.Report();
        }
        finally
        {
            if (!done)
            {
                DestroyDrops(DroppedMark);
                RemoveMarked(player, DroppedMark);
                Player.m_enableAutoPickup = _droppedAutoPickup;
            }
        }
    }

    private static IEnumerator RelogDroppedAfter()
    {
        const string T = RelogDroppedTest;
        var c = new Checks(T);
        var player = Player.m_localPlayer;
        try
        {
            if (!c.Check(player != null && _droppedLeft && ZNetScene.instance != null, "back in the world with a spyglass left on the ground before the log out"))
            {
                c.Report();
                yield break;
            }
            // Objects of the zone come back as the area loads.
            yield return WaitReal(() => MarkedDrops(DroppedMark).Count > 0, 15f);
            var drops = MarkedDrops(DroppedMark);
            if (c.Check(drops.Count == 1, $"the dropped spyglass is in the loaded world again ({drops.Count} found)"))
            {
                var drop = drops[0];
                var moved = Vector3.Distance(drop.transform.position, _droppedAt);
                var view = drop.GetComponent<ZNetView>();
                c.Check(moved < 2f, $"where it was left ({F(moved)} m away)");
                c.Check(SpyglassContent.IsSpyglass(drop.m_itemData) && view != null && view.IsValid() && view.GetZDO().GetPrefab() == SpyglassContent.ItemHash,
                    "it is a spyglass, loaded from the save under its own prefab");
                c.Check(FindDeep(drop.transform, SpyglassModel.ModelName) != null && drop.GetComponentInChildren<MeshRenderer>() != null,
                    "it shows the spyglass model");
                var before = CountSpyglasses(player.GetInventory());
                yield return OwnDrop(c, drop);
                var picked = player.Pickup(drop.gameObject, false, false);
                yield return Frames(3);
                c.Check(picked && CountSpyglasses(player.GetInventory()) == before + 1 && MarkedItems(player.GetInventory(), DroppedMark).Count == 1,
                    "it can be picked up: one more spyglass in the inventory");
                yield return Frames(3);
                c.Check(MarkedDrops(DroppedMark).Count == 0, "and it is gone from the ground");
            }
            c.Report();
        }
        finally
        {
            DestroyDrops(DroppedMark);
            RemoveMarked(player, DroppedMark);
            Player.m_enableAutoPickup = _droppedAutoPickup;
            _droppedLeft = false;
        }
    }

    // ---------- spyglass.disabled.keeps-item (T19, T23) ----------

    private const string CarryBag = DisabledItemTest + ".bag";
    private const string CarryGround = DisabledItemTest + ".ground";
    private const string CarryTaught = DisabledItemTest + ".taught";

    // First game, mod on: one spyglass in the bag, one on the ground, and a player who knows the recipe (so "no
    // recipe" in the second game is the mod being off, not a player who never saw Crystal).
    private static IEnumerator DisabledItemLeave()
    {
        const string T = DisabledItemTest;
        var player = Player.m_localPlayer;
        var recipe = SpyglassContent.CraftRecipe;
        if (player == null || ZNetScene.instance == null || !SpyglassContent.Built || recipe == null || recipe.m_item == null)
        {
            SelfTest.Fail(T, "first game: no player or no spyglass item");
            yield break;
        }
        var problems = new List<string>();
        RemoveMarked(player, DisabledMark);
        DestroyDrops(DisabledMark);
        var bag = GiveMarked(player, DisabledMark);
        var ground = GiveMarked(player, DisabledMark);
        ItemDrop drop = null;
        if (ground != null)
        {
            // Well out of pickup reach (2 m): it must still lie there when the second game looks for it.
            var at = player.transform.position - player.transform.forward * 6f + Vector3.up * 0.5f;
            drop = ItemDrop.DropItem(ground, 1, at, Quaternion.identity);
            player.GetInventory().RemoveItem(ground);
        }
        yield return new WaitForSeconds(2f);
        if (bag == null || MarkedItems(player.GetInventory(), DisabledMark).Count != 1)
        {
            problems.Add("no spyglass could be left in the inventory");
        }
        if (drop == null || MarkedDrops(DisabledMark).Count != 1)
        {
            problems.Add("no spyglass could be left on the ground");
        }
        var name = recipe.m_item.m_itemData.m_shared.m_name;
        var taught = player.m_knownRecipes.Add(name);
        var carried = SelfTest.Carry(CarryBag, "1")
                      && SelfTest.Carry(CarryGround, drop != null ? F(drop.transform.position.x) + "|" + F(drop.transform.position.y) + "|" + F(drop.transform.position.z) : "")
                      && SelfTest.Carry(CarryTaught, taught ? "1" : "0");
        if (!carried)
        {
            problems.Add("the run folder could not be written (no probe run?)");
        }
        if (SelfTest.Stage.Length != 0 || !Plugin.FeatureActive || !recipe.m_enabled)
        {
            problems.Add($"not the first game with the mod on (stage '{SelfTest.Stage}', feature {Plugin.FeatureActive}, recipe shown {recipe.m_enabled})");
        }
        if (problems.Count == 0)
        {
            SelfTest.Pass(T, "first game, mod on: one spyglass left in the inventory, one on the ground, the recipe is shown and the player knows it");
        }
        else
        {
            SelfTest.Fail(T, "first game: " + string.Join("; ", problems.ToArray()));
        }
    }

    private static IEnumerator DisabledItemCheck()
    {
        const string T = DisabledItemTest;
        var c = new Checks(T);
        if (!Ready(T, out var player, out _))
        {
            yield break;
        }
        var rig = new SpyRig(player, T);
        var taught = SelfTest.Carried(CarryTaught) == "1";
        var recipe = SpyglassContent.CraftRecipe;
        var name = recipe != null && recipe.m_item != null ? recipe.m_item.m_itemData.m_shared.m_name : null;
        var autoPickup = Player.m_enableAutoPickup;
        try
        {
            Player.m_enableAutoPickup = false;
            var own = FeatureRegistry.Find(ModInfo.Guid);
            var state = own.HasValue ? own.Value.State : "not registered";
            var status = own.HasValue ? own.Value.Status : "";
            c.Check(SelfTest.Stage == SelfTest.DisabledStage && own.HasValue && own.Value.IsDisabledByUser && !Plugin.FeatureActive,
                $"second game: the mod is off from the start (stage '{SelfTest.Stage}', state {state})");
            c.Check(status.StartsWith("Off", StringComparison.Ordinal), $"Status says off: \"{status}\"");

            // Still in the inventory, and whole.
            var bag = MarkedItems(player.GetInventory(), DisabledMark);
            foreach (var item in bag)
            {
                rig.Track(item);
            }
            if (c.Check(SelfTest.Carried(CarryBag) == "1" && bag.Count == 1 && SpyglassContent.IsSpyglass(bag[0]),
                    $"the spyglass left in the inventory is still there ({bag.Count} found)"))
            {
                var s = bag[0].m_shared;
                c.Check(bag[0].m_dropPrefab != null && s.m_name == SpyglassContent.DisplayName && s.m_icons != null && s.m_icons.Length > 0 && s.m_icons[0] != null
                        && s.m_itemType == ItemDrop.ItemData.ItemType.Tool,
                    $"it is whole: name \"{s.m_name}\", icon, a tool");
                c.Check(SurvivesLoad(player.GetInventory()), "and a saved inventory loads it back again");
                rig.TakeControls();
                player.EquipItem(bag[0], false);
                yield return Frames(5);
                c.Check(player.GetRightItem() == bag[0], "it can still be held in the right hand");
                yield return DeadClicks(c, player);
            }
            c.Check(ObjectDB.instance.GetItemPrefab(SpyglassContent.ItemName) != null && ZNetScene.instance.GetPrefab(SpyglassContent.ItemHash) != null,
                "the item is still known to the game (item database and network list)");

            // The one on the ground.
            var where = (SelfTest.Carried(CarryGround) ?? "").Split('|');
            yield return WaitReal(() => MarkedDrops(DisabledMark).Count > 0, 15f);
            var drops = MarkedDrops(DisabledMark);
            if (c.Check(where.Length == 3 && drops.Count == 1 && SpyglassContent.IsSpyglass(drops[0].m_itemData),
                    $"the spyglass left on the ground is still there ({drops.Count} found)"))
            {
                c.Check(FindDeep(drops[0].transform, SpyglassModel.ModelName) != null, "it still shows the spyglass model");
                yield return OwnDrop(c, drops[0]);
                var picked = player.Pickup(drops[0].gameObject, false, false);
                yield return Frames(3);
                var now = MarkedItems(player.GetInventory(), DisabledMark);
                foreach (var item in now)
                {
                    rig.Track(item);
                }
                c.Check(picked && now.Count == 2, $"and it can be picked up ({now.Count} marked spyglasses in the inventory now)");
            }

            // No recipe, for a player who knows it.
            if (c.Check(recipe != null && name != null && ObjectDB.instance.m_recipes.Contains(recipe), "the recipe object is still registered"))
            {
                c.Check(!recipe.m_enabled, "the recipe is hidden");
                c.Check(player.m_knownRecipes.Contains(name), "the player knows the recipe from the first game");
                KeepKnown(rig);
                var station = SpawnStation(c, rig);
                if (station != null)
                {
                    // Control: one recipe of the game at the same station, known for the test, must be listed (else
                    // an empty list would say nothing about the spyglass).
                    Recipe other = null;
                    foreach (var r in ObjectDB.instance.m_recipes)
                    {
                        if (r != null && r != recipe && r.m_enabled && r.m_item != null && r.m_craftingStation != null
                            && r.m_craftingStation.m_name == station.m_name && r.m_minStationLevel <= 1
                            && string.IsNullOrEmpty(r.m_item.m_itemData.m_shared.m_dlc))
                        {
                            other = r;
                            break;
                        }
                    }
                    if (other != null)
                    {
                        player.m_knownRecipes.Add(other.m_item.m_itemData.m_shared.m_name);
                    }
                    var row = new int[1];
                    yield return OpenStation(player, station, row);
                    var control = false;
                    foreach (var pair in InventoryGui.instance.m_availableRecipes)
                    {
                        control |= other != null && ReferenceEquals(pair.Recipe, other);
                    }
                    c.Check(control, $"the craft list of the spyglass's station is open and lists a recipe of the game the player knows ({(other != null ? other.name : "none found")})");
                    c.Check(row[0] < 0, "no spyglass in that craft list");
                    yield return CloseStation(player);
                }
            }

            var problems = _tap != null ? _tap.Problems() : null;
            var named = ErrorsNamingMe(0);
            c.Check(problems != null && problems.Count == 0 && named.Count == 0,
                problems == null ? "the log was not listened to"
                : problems.Count == 0 && named.Count == 0 ? "no warning or error from the spyglass, and no error naming its code, since this game started with the mod off"
                : $"{problems.Count} warning or error line(s) from the spyglass and {named.Count} error(s) naming its code since this game started: {First(problems.Count > 0 ? problems : named)}");
            c.Report();
        }
        finally
        {
            rig.Restore();
            RemoveMarked(player, DisabledMark);
            DestroyDrops(DisabledMark);
            if (taught && name != null && player != null)
            {
                player.m_knownRecipes.Remove(name);
            }
            Player.m_enableAutoPickup = autoPickup;
        }
    }
}
#endif
