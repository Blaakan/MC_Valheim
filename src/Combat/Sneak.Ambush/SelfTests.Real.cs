#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;
using MC.Combat.SneakAmbushMod.Patches;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Combat.SneakAmbushMod;

// Debug build only (whole file). Self tests that do the real thing where older tests took a shortcut, and the tests
// that need a second game start. One small test per item of TESTING.md, so one can be fixed or dropped alone:
//   sneak.disabled.items      L02 game REALLY started with Enabled = false (second game of the run): Smoke Screens left
//                             in bag and chest by the first game still there, moved, dropped, picked up; recipe hidden,
//                             throw refused with the message, no stealth icon, bar = normal game, Status text
//   sneak.log-clean           L03 log file from game start: no warning, no error of this mod (start-up, main menu,
//                             world load, all its tests but sneak.recipe, other mods' tests so far); runs last
//   sneak.toggle-fight        L04 feature really off, Greydwarfs hit (they fight), on again, shot from the smoke on a
//                             two-star Greydwarf with its AI on: it live, see, come over and attack
//   sneak.smoke-unload        T28 real throw, player leave at once (far teleport: zone unload), back 20 s after the
//                             throw: cloud gone from the world at once, never hid, no smoke, no blind
//   sneak.mp.unload           T28 same on the dedicated server (nobody near the cloud while the player is away)
//   sneak.smoke-water         T14 real throw from a shore into deep water (shore found by the world's height rule, far
//                             teleport when none is near): one cloud on the surface
//   sneak.mist-real           T12 real Mistlands (far teleport), real mist area, no Wisplight: icon crouched only
//   sneak.foliage-oak         T09 crouched under an oak: In foliage icon
//   sneak.foliage-shade       T09 bush between the player and the noon sun: bar and light factor drop
//   sneak.relog.inactive-leave  M18 on one game: mod inactive in the world, real log out, mod turns on while the
//                             world closes: no warning, active at the menu and back in the world
// Far trips use the game's own teleport (loading screen, zones loaded) and bring the player home again; a test cut
// short sends him home from its finally.
internal static partial class SelfTests
{
    private const string DisabledItemsName = "sneak.disabled.items";
    private const string LogCleanName = "sneak.log-clean";
    private const string ToggleFightName = "sneak.toggle-fight";
    private const string SmokeUnloadName = "sneak.smoke-unload";
    private const string MpUnloadName = "sneak.mp.unload";
    private const string SmokeWaterName = "sneak.smoke-water";
    private const string MistRealName = "sneak.mist-real";
    private const string FoliageOakName = "sneak.foliage-oak";
    private const string FoliageShadeName = "sneak.foliage-shade";
    private const string RelogLeaveName = "sneak.relog.inactive-leave";

    // Status text of a mod turned off in its settings, as TESTING.md L02 spell it.
    private const string DisabledStatusText = "Off (disabled in settings).";
    private const string DisabledChestPrefab = "piece_chest_wood";
    private const string DisabledCarryChest = DisabledItemsName + ".chest";
    private const string DisabledCarryBag = DisabledItemsName + ".bag";
    private const int DisabledInBag = 3;
    private const int DisabledInChest = 2;
    private const string OakPrefab = "Oak1";
    private const string StepOrphan = "sneak.mp.s.orphan";

    private static void RegisterReal()
    {
        SelfTest.Register(ToggleFightName, RunToggleFight);
        SelfTest.Register(SmokeUnloadName, RunSmokeUnload);
        SelfTest.Register(SmokeWaterName, RunSmokeWater);
        SelfTest.Register(MistRealName, RunMistReal);
        SelfTest.Register(FoliageOakName, RunFoliageOak);
        SelfTest.Register(FoliageShadeName, RunFoliageShade);
        SelfTest.RegisterMultiplayer(MpUnloadName, SelfTest.Modded, RunMpUnload);
        SelfTest.RegisterRelog(RelogLeaveName, RelogLeaveBefore, RelogLeaveMenu, RelogLeaveAfter);
        // Server half stays registered like the others (see UnregisterMultiplayer).
        SelfTest.RegisterServerStep(StepOrphan, ServerOrphanStep);
    }

    private static void UnregisterReal()
    {
        SelfTest.Unregister(ToggleFightName);
        SelfTest.Unregister(SmokeUnloadName);
        SelfTest.Unregister(SmokeWaterName);
        SelfTest.Unregister(MistRealName);
        SelfTest.Unregister(FoliageOakName);
        SelfTest.Unregister(FoliageShadeName);
        SelfTest.UnregisterMultiplayer(MpUnloadName);
        SelfTest.UnregisterRelog(RelogLeaveName);
    }

    // Last test of this mod (after the one that kills the player: me only read the log file).
    private static void RegisterLast() => SelfTest.Register(LogCleanName, RunLogClean);

    private static void UnregisterLast() => SelfTest.Unregister(LogCleanName);

    // Plugin.BindConfig call me (Debug build): in the second game of a run me am off from the start, OnActivated
    // never run there. Never unregistered.
    internal static void RegisterDisabledStart() =>
        SelfTest.RegisterDisabledStart(DisabledItemsName, DisabledItemsLeave, DisabledItemsCheck);

    // ---------- far trips ----------

    // Where the player came from, and whether he is still away (finally send him home then).
    private sealed class Trip
    {
        internal bool Away;
        internal Vector3 Home;
        internal Quaternion HomeRot;
    }

    // The game's far teleport (loading screen, far zones loaded, waits for a floor). Real time: frames are long while
    // zones load. Ok = arrived.
    private static IEnumerator Travel(Player player, Vector3 target, Box result, float timeout = 50f)
    {
        result.Ok = false;
        result.Detail = "";
        var start = Time.realtimeSinceStartup;
        float Passed() => Time.realtimeSinceStartup - start;
        while (!player.TeleportTo(target, player.transform.rotation, true))
        {
            if (Passed() > 8f)
            {
                result.Detail = "the game refused the teleport for 8 s";
                yield break;
            }
            yield return null;
        }
        yield return null;
        while (player != null && player.IsTeleporting() && Passed() < timeout)
        {
            yield return null;
        }
        if (player == null || player.IsTeleporting())
        {
            result.Detail = $"still teleporting after {F(Passed())} s";
            yield break;
        }
        // Standing on the floor of the new place.
        var landed = Time.time + 4f;
        while (Time.time < landed && !player.IsOnGround() && !player.IsSwimming())
        {
            yield return null;
        }
        result.Ok = true;
        result.Detail = $"{F(Passed())} s";
    }

    // Finally of a test cut short far from home (no yield there): the game finish the trip after the test.
    private static void SendHome(Trip trip, Player player)
    {
        if (trip == null || !trip.Away || player == null)
        {
            return;
        }
        trip.Away = false;
        if (player.IsTeleporting())
        {
            player.m_teleportTargetPos = trip.Home;
            player.m_teleportTargetRot = trip.HomeRot;
            return;
        }
        // The game wait 2 s between two teleports: a trip that just ended would refuse this one.
        player.m_teleportCooldown = 10f;
        player.TeleportTo(trip.Home, trip.HomeRot, true);
    }

    // Dry land that far from a place, by the world's own height rule (no loaded zone needed there). Meadows first,
    // then Black Forest.
    private static bool LandNear(Vector3 from, float min, float max, out Vector3 land)
    {
        var gen = WorldGenerator.instance;
        var water = ZoneSystem.instance.m_waterLevel;
        if (gen != null)
        {
            foreach (var biome in new[] { Heightmap.Biome.Meadows, Heightmap.Biome.BlackForest })
            {
                for (var r = min; r <= max; r += 40f)
                {
                    for (var a = 0; a < 360; a += 20)
                    {
                        var p = from + Quaternion.Euler(0f, a, 0f) * Vector3.forward * r;
                        if (gen.GetBiome(p.x, p.z) != biome)
                        {
                            continue;
                        }
                        var h = gen.GetHeight(p.x, p.z);
                        if (h > water + 2f && h < water + 30f)
                        {
                            land = new Vector3(p.x, h, p.z);
                            return true;
                        }
                    }
                }
            }
        }
        land = from;
        return false;
    }

    // ---------- sneak.disabled.items (L02) ----------

    // First game, mod on, last thing before the game quits from the world (which saves): Smoke Screens into the bag
    // and into a chest beside the player. The second game find them.
    private static IEnumerator DisabledItemsLeave()
    {
        var player = Player.m_localPlayer;
        var scene = ZNetScene.instance;
        if (player == null || scene == null || ZoneSystem.instance == null)
        {
            SelfTest.Fail(DisabledItemsName, "first game: no local player or no world");
            yield break;
        }
        if (Plugin.TestBlocked)
        {
            // A staged test before me was cut short with its block still on: mod on again first.
            Plugin.TestBlocked = false;
            FeatureRegistry.RefreshAll();
            yield return Frames(3);
        }
        var chestPrefab = scene.GetPrefab(DisabledChestPrefab);
        if (!Plugin.FeatureActive || SmokeContent.ItemPrefab == null || chestPrefab == null)
        {
            SelfTest.Fail(DisabledItemsName, $"first game: the mod is not active ({Plugin.FeatureActive}), the Smoke Screen was not made "
                                             + $"({SmokeContent.ItemPrefab != null}) or there is no {DisabledChestPrefab} ({chestPrefab != null})");
            yield break;
        }
        var bag = player.GetInventory();
        var before = bag.CountItems(SmokeContent.DisplayName, -1, false);
        var at = Ground(player.transform.position + Flat(player.transform.right) * 2.5f);
        var chestGo = Object.Instantiate(chestPrefab, at, Quaternion.identity);
        yield return Frames(3);
        var chest = chestGo != null ? chestGo.GetComponent<Container>() : null;
        var view = chestGo != null ? chestGo.GetComponent<ZNetView>() : null;
        if (chest == null || view == null || !view.IsValid())
        {
            SelfTest.Fail(DisabledItemsName, "first game: could not place a chest");
            yield break;
        }
        chest.GetInventory().AddItem(SmokeContent.ItemName, DisabledInChest, 1, 0, 0L, "", false);
        bag.AddItem(SmokeContent.ItemName, DisabledInBag, 1, 0, 0L, "", false);
        yield return Frames(2);
        var inChest = chest.GetInventory().CountItems(SmokeContent.DisplayName, -1, false);
        var inBag = bag.CountItems(SmokeContent.DisplayName, -1, false) - before;
        // What the world save keeps of the chest (the game writes it at every change).
        var stored = view.GetZDO().GetString(ZDOVars.s_items, "").Length > 0;
        var p = chestGo.transform.position;
        var carried = SelfTest.Carry(DisabledCarryChest, $"{I(p.x)};{I(p.y)};{I(p.z)}")
                      && SelfTest.Carry(DisabledCarryBag, before.ToString(CultureInfo.InvariantCulture));
        if (inChest == DisabledInChest && inBag == DisabledInBag && stored && carried)
        {
            SelfTest.Pass(DisabledItemsName, $"first game: {inBag} Smoke Screens left in the inventory and {inChest} in a chest at {V(p)} for the game "
                                             + "that starts with the mod off");
        }
        else
        {
            SelfTest.Fail(DisabledItemsName, $"first game: {inBag} of {DisabledInBag} Smoke Screens in the inventory, {inChest} of {DisabledInChest} "
                                             + $"in the chest, chest content stored {stored}, place carried {carried}");
        }
    }

    private static Container ChestAt(Vector3 where)
    {
        Container best = null;
        var bestDistance = 1.5f;
        foreach (var box in Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
        {
            if (box == null)
            {
                continue;
            }
            var d = Vector3.Distance(box.transform.position, where);
            if (d < bestDistance)
            {
                best = box;
                bestDistance = d;
            }
        }
        return best;
    }

    // Does that station offer any recipe that makes a Smoke Screen to this player? The game's own list of the crafting
    // window (station set for this one call).
    private static bool OffersSmokeScreen(Player player, CraftingStation station)
    {
        var keep = player.m_currentStation;
        player.m_currentStation = station;
        try
        {
            var list = new List<Recipe>();
            player.GetAvailableRecipes(ref list);
            return list.Any(r => r != null && r.m_item != null && SmokeContent.IsSmokeScreen(r.m_item.m_itemData));
        }
        finally
        {
            player.m_currentStation = keep;
        }
    }

    // Second game: every MC mod has Enabled = false since the start. Same character, same world.
    private static IEnumerator DisabledItemsCheck()
    {
        var c = new Checks(DisabledItemsName);
        var rig = Rig.Create(DisabledItemsName);
        if (rig == null)
        {
            yield break;
        }
        Container chest = null;
        var bagBefore = -1;
        var dropsBefore = -1;
        var knewRecipe = true;
        var addedRecipe = false;
        try
        {
            var player = rig.Player;
            var found = FeatureRegistry.Find(ModInfo.Guid);
            c.Check(SelfTest.Stage == SelfTest.DisabledStage, $"this is not the game started with the mods off (stage '{SelfTest.Stage}')");
            c.Check(found != null && found.Value.IsDisabledByUser && !Plugin.FeatureActive,
                $"the mod is not off by its Enabled setting (state {(found != null ? found.Value.State : "not in the registry")})");
            c.Check(found != null && found.Value.Status == DisabledStatusText,
                $"Status reads '{(found != null ? found.Value.Status : "")}', expected '{DisabledStatusText}'");
            Tap.Install();
            var chestText = SelfTest.Carried(DisabledCarryChest);
            var bagText = SelfTest.Carried(DisabledCarryBag);
            var parts = (chestText ?? "").Split(';');
            var x = 0f;
            var y = 0f;
            var z = 0f;
            var known = parts.Length == 3
                        && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                        && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y)
                        && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z);
            var counted = 0;
            known = known && int.TryParse(bagText, NumberStyles.Integer, CultureInfo.InvariantCulture, out counted);
            if (!c.Check(known, $"the first game left no chest place or inventory count (carried '{chestText}', '{bagText}')"))
            {
                c.Report();
                yield break;
            }
            bagBefore = counted;
            var where = new Vector3(x, y, z);

            // Still a real item of the game while the mod is off.
            c.Check(SmokeContent.ItemPrefab != null && ObjectDB.instance.GetItemPrefab(SmokeContent.ItemName) == SmokeContent.ItemPrefab
                    && ZNetScene.instance.GetPrefab(SmokeContent.ItemName) == SmokeContent.ItemPrefab,
                "started with the mod off: the Smoke Screen is not registered as an item");

            // In the inventory after loading the character.
            var bag = player.GetInventory();
            var stack = rig.SmokeCount();
            c.Check(stack == bagBefore + DisabledInBag,
                $"the loaded character holds {stack} Smoke Screens, expected {bagBefore + DisabledInBag} ({DisabledInBag} left by the first game)");

            // In the chest after loading the world (objects of the zone come in a little after the spawn).
            var until = Time.time + 10f;
            while (chest == null && Time.time < until)
            {
                chest = ChestAt(where);
                if (chest == null)
                {
                    yield return null;
                }
            }
            if (!c.Check(chest != null, $"the chest the first game placed at {V(where)} is not in the loaded world"))
            {
                c.Report();
                yield break;
            }
            // The chest reads its saved content within its first second.
            yield return new WaitForSeconds(1.3f);
            var chestBag = chest.GetInventory();
            c.Check(chestBag.CountItems(SmokeContent.DisplayName, -1, false) == DisabledInChest,
                $"the chest holds {chestBag.CountItems(SmokeContent.DisplayName, -1, false)} Smoke Screens after loading, expected {DisabledInChest}");

            // Moved: chest to inventory and back.
            var inChest = chestBag.GetAllItems().FirstOrDefault(SmokeContent.IsSmokeScreen);
            if (c.Check(inChest != null, "no Smoke Screen in the chest to move"))
            {
                var take = inChest.m_stack;
                bag.MoveItemToThis(chestBag, inChest);
                c.Check(chestBag.CountItems(SmokeContent.DisplayName, -1, false) == 0 && rig.SmokeCount() == stack + take,
                    $"could not take the Smoke Screens out of the chest (inventory {rig.SmokeCount()}, expected {stack + take})");
                var one = bag.GetAllItems().FirstOrDefault(SmokeContent.IsSmokeScreen);
                c.Check(one != null && chestBag.MoveItemToThis(bag, one, take, 0, 0)
                        && chestBag.CountItems(SmokeContent.DisplayName, -1, false) == take && rig.SmokeCount() == stack,
                    "could not put the Smoke Screens back into the chest");
            }

            // Dropped and picked up.
            dropsBefore = SmokeDrops();
            var held = bag.GetAllItems().FirstOrDefault(SmokeContent.IsSmokeScreen);
            if (c.Check(held != null && player.DropItem(bag, held, 1), "could not drop a Smoke Screen"))
            {
                yield return Frames(3);
                var dropped = ItemDrop.s_instances.FirstOrDefault(d => d != null && d.m_itemData != null && SmokeContent.IsSmokeScreen(d.m_itemData));
                c.Check(dropped != null && SmokeDrops() == dropsBefore + 1 && rig.SmokeCount() == stack - 1, "the dropped Smoke Screen is not on the ground");
                if (dropped != null)
                {
                    c.Check(player.Pickup(dropped.gameObject, false, false), "could not pick the Smoke Screen up again");
                    yield return Frames(3);
                    c.Check(rig.SmokeCount() == stack && SmokeDrops() == dropsBefore, $"after the pick-up the inventory holds {rig.SmokeCount()} (expected {stack})");
                }
            }

            // Recipe hidden: a workbench offers no Smoke Screen recipe to a player who knows it.
            var benchGo = rig.Spawn(AmbushRules.DefaultRecipeStation, Ground(player.transform.position - Flat(player.transform.forward) * 3f),
                Quaternion.identity);
            var station = benchGo != null ? benchGo.GetComponent<CraftingStation>() : null;
            yield return Frames(3);
            if (c.Check(station != null, "could not spawn a workbench"))
            {
                knewRecipe = player.m_knownRecipes.Contains(SmokeContent.DisplayName);
                player.m_knownRecipes.Add(SmokeContent.DisplayName);
                addedRecipe = true;
                var recipe = SmokeContent.CraftRecipe;
                c.Check(!OffersSmokeScreen(player, station) && (recipe == null || !recipe.m_enabled),
                    "started with the mod off: the workbench offers the Smoke Screen recipe");
            }

            // Throw refused with the message, nothing used.
            yield return FreeHands(player);
            var item = rig.GiveAndEquip(0);
            if (c.Check(item != null, $"could not hold a Smoke Screen ({HandsText(player)})"))
            {
                yield return Frames(2);
                var clouds = new HashSet<SmokeCloud>(SmokeRegistry.All);
                stack = rig.SmokeCount();
                HumanoidPatches.TestResetMessageTimer();
                var told = Tap.Messages(RefusedThrowText, MessageHud.MessageType.TopLeft);
                c.Check(!player.StartAttack(null, false), "started with the mod off: a throw was allowed");
                c.Check(Tap.Messages(RefusedThrowText, MessageHud.MessageType.TopLeft) == told + 1,
                    $"the refused throw did not show the message TESTING.md names: '{Tap.MessageTexts("Smoke Screen")}'");
                yield return new WaitForSeconds(0.6f);
                c.Check(rig.SmokeCount() == stack && !player.InAttack() && NewClouds(clouds).Count == 0,
                    $"the refused throw used something: stack {stack} -> {rig.SmokeCount()}, attacking {player.InAttack()}, new clouds {NewClouds(clouds).Count}");
            }

            // No stealth icon, and the bar of the normal game, crouched and still for longer than the icon's delay.
            var crouched = new Box();
            yield return Crouch(player, crouched);
            yield return new WaitForSeconds(2.6f);
            yield return ForceRefresh(player);
            c.Check(crouched.Ok && !AnyCue(player), $"crouched and still for 2.6 s with the mod off: a stealth icon shows (crouched {crouched.Ok})");
            c.Check(Near(player.m_stealthFactorTarget, VanillaTarget(player), 0.02f),
                $"crouched with the mod off: bar target {F(player.m_stealthFactorTarget)}, the normal game's {F(VanillaTarget(player))}");
            player.SetCrouch(false);
            NoProblems(c);
            c.Report();
        }
        finally
        {
            // What the first game left for this check goes: the chest with its content, the Smoke Screens of the bag.
            try
            {
                var p = rig.Player;
                if (p != null)
                {
                    if (addedRecipe && !knewRecipe)
                    {
                        p.m_knownRecipes.Remove(SmokeContent.DisplayName);
                    }
                    if (chest != null)
                    {
                        chest.GetInventory().RemoveAll();
                        DestroyObject(chest.gameObject);
                    }
                    if (dropsBefore >= 0 && SmokeDrops() > dropsBefore)
                    {
                        foreach (var drop in ItemDrop.s_instances.ToList())
                        {
                            if (drop != null && drop.m_itemData != null && SmokeContent.IsSmokeScreen(drop.m_itemData)
                                && Vector3.Distance(drop.transform.position, p.transform.position) < 8f)
                            {
                                DestroyObject(drop.gameObject);
                            }
                        }
                    }
                    if (bagBefore >= 0)
                    {
                        var inventory = p.GetInventory();
                        foreach (var it in inventory.GetAllItems().ToList())
                        {
                            if (SmokeContent.IsSmokeScreen(it) && p.IsItemEquiped(it))
                            {
                                p.UnequipItem(it, false);
                            }
                        }
                        var extra = inventory.CountItems(SmokeContent.DisplayName, -1, false) - bagBefore;
                        if (extra > 0)
                        {
                            inventory.RemoveItem(SmokeContent.DisplayName, extra, -1, false);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                SelfTest.Fail(DisabledItemsName, $"clean-up of the first game's items failed: {e.Message}");
            }
            rig.Restore();
        }
    }

    // ---------- sneak.relog.inactive-leave (M18, on one game) ----------

    // M18 is about the moment a player leaves a server that has no Sneak Ambush (or has it off): the mod was inactive
    // there and turns on while the world closes (framework refresh at the world's end, a postfix of ZNet.OnDestroy),
    // when the game's item prefabs are already going. Here the same moment in single player: mod REALLY inactive in
    // the world (test block, as sneak.toggle), real log out, and the block lifted by a test-only prefix of
    // ZNet.OnDestroy, so the framework's own refresh right after it turns the mod on, as after such a server. (Lifted
    // earlier it would not hold: the framework also refreshes when its config file changes, and it saves its status
    // line there.) A real server without the mod stays a check by hand.
    private static bool _relogRecipeShown;
    private static bool _relogWorldEnded;
    private static bool _relogInactiveAtEnd;
    private static Harmony _relogHook;

    // Test-only prefix of ZNet.OnDestroy (own Harmony id, never a patch class of the mod).
    private static void RelogWorldEnding()
    {
        try
        {
            _relogWorldEnded = true;
            _relogInactiveAtEnd = !Plugin.FeatureActive;
            Plugin.TestBlocked = false;
        }
        catch
        {
            // Me never throw into the game's shutdown.
        }
    }

    private static void RelogUnhook()
    {
        if (_relogHook != null)
        {
            _relogHook.UnpatchSelf();
            _relogHook = null;
        }
    }

    private static IEnumerator RelogLeaveBefore()
    {
        var armed = false;
        try
        {
            var found = FeatureRegistry.Find(ModInfo.Guid);
            var recipe = SmokeContent.CraftRecipe;
            if (found == null || !found.Value.IsActive || recipe == null || Player.m_localPlayer == null)
            {
                SelfTest.Fail(RelogLeaveName, "before: the mod is not active in a world, or the Smoke Screen has no recipe");
                yield break;
            }
            var view = found.Value;
            _relogRecipeShown = recipe.m_enabled;
            _relogWorldEnded = false;
            _relogInactiveAtEnd = false;
            // Ears kept while the feature goes off and on (log lines only).
            Tap.Install(false);
            Tap.Hold = true;
            Plugin.TestBlocked = true;
            FeatureRegistry.RefreshAll();
            yield return Frames(3);
            if (view.IsActive || Plugin.FeatureActive || recipe.m_enabled)
            {
                SelfTest.Fail(RelogLeaveName, $"before: the mod did not go inactive (state {view.State}, recipe shown {recipe.m_enabled})");
                yield break;
            }
            RelogUnhook();
            var hook = new Harmony(ModInfo.Guid + ".selftest.relog");
            hook.Patch(AccessTools.Method(typeof(ZNet), nameof(ZNet.OnDestroy)), prefix: new HarmonyMethod(typeof(SelfTests), nameof(RelogWorldEnding)));
            _relogHook = hook;
            Tap.Hold = true;
            Tap.Clear();
            armed = true;
            SelfTest.Pass(RelogLeaveName, $"before: the mod is inactive in the world (\"{view.Status}\"), as on a server without it; it turns on when the world ends");
        }
        finally
        {
            if (!armed)
            {
                RelogUnhook();
                Plugin.TestBlocked = false;
                FeatureRegistry.RefreshAll();
                Tap.Release();
            }
        }
    }

    private static IEnumerator RelogLeaveMenu()
    {
        yield return null;
        RelogUnhook();
        var found = FeatureRegistry.Find(ModInfo.Guid);
        var active = found != null && found.Value.IsActive && Plugin.FeatureActive;
        var problems = Tap.Problems();
        if (active && _relogWorldEnded && _relogInactiveAtEnd && problems.Length == 0)
        {
            SelfTest.Pass(RelogLeaveName, "main menu: the mod was still inactive when the world ended, turned on while it closed and logged no warning and no error");
        }
        else
        {
            SelfTest.Fail(RelogLeaveName, $"main menu: mod active {active} (state {(found != null ? found.Value.State : "not in the registry")}), world end seen "
                                          + $"{_relogWorldEnded}, mod still inactive at that moment {_relogInactiveAtEnd}; warnings or errors of {ModInfo.Name} while the "
                                          + $"world closed: {(problems.Length > 0 ? problems : "none")}");
        }
        if (!active)
        {
            // Never leave the mod off for the rest of the run.
            Plugin.TestBlocked = false;
            FeatureRegistry.RefreshAll();
        }
    }

    private static IEnumerator RelogLeaveAfter()
    {
        try
        {
            yield return new WaitForSeconds(1f);
            var found = FeatureRegistry.Find(ModInfo.Guid);
            var recipe = SmokeContent.CraftRecipe;
            var active = found != null && found.Value.IsActive && Plugin.FeatureActive;
            var problems = Tap.Problems();
            if (active && recipe != null && recipe.m_enabled == _relogRecipeShown && problems.Length == 0)
            {
                SelfTest.Pass(RelogLeaveName, $"after: back in the world the mod is active, the recipe is {(recipe.m_enabled ? "shown" : "hidden")} as before, "
                                              + "and nothing was logged at Warning or Error level since the log out");
            }
            else
            {
                SelfTest.Fail(RelogLeaveName, $"after: mod active {active}, recipe shown {recipe != null && recipe.m_enabled} (before the test {_relogRecipeShown}); "
                                              + $"warnings or errors of {ModInfo.Name} since the log out: {(problems.Length > 0 ? problems : "none")}");
            }
        }
        finally
        {
            RelogUnhook();
            if (Plugin.TestBlocked)
            {
                Plugin.TestBlocked = false;
                FeatureRegistry.RefreshAll();
            }
            Tap.Release();
        }
    }

    // ---------- sneak.log-clean (L03) ----------

    // Log file of this game from its start: every line of this mod at Warning or Error level, and error lines of
    // others that run through this mod's code. That is start-up, main menu, world load, every test of this mod so far
    // (the play of T01-T26) and the tests of the mods before it, with this mod running. Left out: sneak.recipe, which
    // makes the mod warn about odd recipe settings on purpose and checks those lines itself. Registered last of this
    // mod. Own small test: other installed mods can make this mod say something.
    private static IEnumerator RunLogClean()
    {
        var c = new Checks(LogCleanName);
        yield return null;
        // BepInEx write the log file with a delay (flush every 2 s): last lines to disk first, or the end of the test
        // before me is not judged.
        try
        {
            foreach (var listener in BepInEx.Logging.Logger.Listeners)
            {
                if (listener is BepInEx.Logging.DiskLogListener disk && disk.LogWriter != null)
                {
                    disk.LogWriter.Flush();
                }
            }
        }
        catch (Exception e)
        {
            c.Note($"could not flush the log file before reading it (its last 2 s may be missing): {e.Message}");
        }
        var path = Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log");
        string[] lines = null;
        string error = null;
        try
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
            {
                lines = reader.ReadToEnd().Split('\n');
            }
        }
        catch (Exception e)
        {
            error = e.Message;
        }
        if (!c.Check(lines != null, $"could not read {path}: {error}"))
        {
            c.Report();
            yield break;
        }
        var begin = SelfTest.Prefix + " BEGIN ";
        var end = SelfTest.Prefix + " END ";
        const string code = "MC.Combat.SneakAmbushMod.";
        var bad = new List<string>();
        var ours = 0;
        var judged = 0;
        var ownTests = 0;
        var beforeFirstTest = 0;
        var anyTest = false;
        var foreignError = false;
        string current = null;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (line.Contains(SelfTest.Prefix))
            {
                var at = line.IndexOf(begin, StringComparison.Ordinal);
                if (at >= 0)
                {
                    // "[selftest] BEGIN <name> (<mod>)"
                    var rest = line.Substring(at + begin.Length);
                    var space = rest.IndexOf(' ');
                    current = space > 0 ? rest.Substring(0, space) : rest;
                    anyTest = true;
                    if (current.StartsWith("sneak.", StringComparison.Ordinal))
                    {
                        ownTests++;
                    }
                }
                else if (line.IndexOf(end, StringComparison.Ordinal) >= 0)
                {
                    current = null;
                }
                foreignError = false;
                continue;
            }
            // The one test that makes the mod warn on purpose (it checks those lines itself).
            var provoked = current == RecipeName;
            if (!line.StartsWith("[", StringComparison.Ordinal))
            {
                // More lines of the entry above (a stack trace): an error of another source that runs through our code
                // (frames of the test code itself are a failed test, which the test run reports).
                if (foreignError && !provoked && line.Contains(code) && !line.Contains(code + nameof(SelfTests)))
                {
                    bad.Add("stack trace: " + Short(line.Trim()));
                }
                continue;
            }
            foreignError = false;
            // "[Warning:Sneak Ambush] text": level padded to 7, then ':' and the source.
            var close = line.IndexOf(']');
            if (close < 10 || line[8] != ':')
            {
                continue;
            }
            var level = line.Substring(1, 7).Trim();
            var source = line.Substring(9, close - 9).Trim();
            var serious = level == "Warning" || level == "Error" || level == "Fatal";
            if (source == ModInfo.Name)
            {
                ours++;
                if (provoked)
                {
                    continue;
                }
                judged++;
                if (!anyTest)
                {
                    beforeFirstTest++;
                }
                if (serious)
                {
                    bad.Add((current != null ? $"during {current}: " : "") + Short(line));
                }
            }
            else if (!provoked && (level == "Error" || level == "Fatal"))
            {
                foreignError = true;
                if (line.Contains(code) && !line.Contains(code + nameof(SelfTests)))
                {
                    bad.Add((current != null ? $"during {current}: " : "") + Short(line));
                }
            }
        }
        c.Check(ours > 0 && beforeFirstTest > 0,
            $"found {ours} line(s) of {ModInfo.Name} in the log, {beforeFirstTest} before the first test: nothing to judge (log format or source name changed?)");
        c.Check(bad.Count == 0, $"{ModInfo.Name} logged {bad.Count} warning or error line(s) since the game started: " + string.Join(" | ", bad.Take(6).ToArray()));
        c.Note($"{judged} line(s) of {ModInfo.Name} judged ({beforeFirstTest} from start-up, main menu and world load), through {ownTests} own test(s); "
               + $"only {RecipeName} left out");
        c.Report();
    }

    private static string Short(string line) => line.Length > 180 ? line.Substring(0, 180) : line;

    // ---------- sneak.toggle-fight (L04) ----------

    // The part of L04 that needs no restart, with the creature's AI on: feature REALLY off (patches removed), a few
    // Greydwarfs hit while off (the game's damage code runs unpatched, they fight), feature on again (patches applied
    // to code that already ran), then T20: shot from inside the smoke on a two-star Greydwarf. A game that STARTS
    // with Enabled = false and is turned on later stays a check by hand (a test may not write the Enabled setting).
    private static IEnumerator RunToggleFight()
    {
        var c = new Checks(ToggleFightName);
        var rig = Rig.Create(ToggleFightName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            var rules = XpRules(c);
            rules.ActivationDelay = 0f;
            rules.CloudDuration = 40f;

            // Feature going off clear every override: put back after each switch. Ears held through the switches.
            void Overrides()
            {
                ServerRules.TestRules = rules;
                SneakXp.TestShowMessage = true;
                Compat.TestOtherModPays = false;
            }

            Overrides();
            Tap.Install();
            Tap.Hold = true;
            rig.SaveAllSkills();
            rig.SetSneak(20f);
            yield return Stand(player);
            var place = new Spot();
            yield return FindOpenSpot(rig, 14f, false, (feet, d) => Lane(feet, -d, 9f), place);
            if (!place.Found)
            {
                c.Note("no open ground with 14 m free ahead and 9 m behind found within 48 m: the scenery may be in the way");
            }
            var dir = place.Dir;
            var side = Vector3.Cross(Vector3.up, dir).normalized;
            var origin = player.transform.position;
            var found = FeatureRegistry.Find(ModInfo.Guid);
            if (!c.Check(found != null, "this mod is not in the registry"))
            {
                c.Report();
                yield break;
            }
            var view = found.Value;

            // OFF, for real. Two Greydwarfs with their AI on, each hit once: the normal game makes them fight.
            Plugin.TestBlocked = true;
            FeatureRegistry.RefreshAll();
            Overrides();
            c.Check(!view.IsActive && !Plugin.FeatureActive, $"off: state {view.State}, status '{view.Status}'");
            var first = rig.Creature("Greydwarf", Ground(origin - dir * 6f), dir, true);
            var second = rig.Creature("Greydwarf", Ground(origin - dir * 6f + side * 3f), dir, true);
            if (!c.Check(first != null && second != null, "could not spawn two Greydwarfs"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(0.8f);
            var firstAi = (MonsterAI)first.GetBaseAI();
            var secondAi = (MonsterAI)second.GetBaseAI();
            Hit(first, player, Slash(2f));
            Hit(second, player, Slash(2f));
            var fighting = new Box();
            yield return WaitFor(() => first == null || second == null
                                       || (firstAi.IsAlerted() && ReferenceEquals(firstAi.m_targetCreature, player)
                                           && secondAi.IsAlerted() && ReferenceEquals(secondAi.m_targetCreature, player)), 3f, fighting);
            c.Check(first != null && second != null && fighting.Ok, "off: the two Greydwarfs that were hit do not fight the player");
            c.Check(Tap.Logged("Sneak XP") == 0 && Tap.Messages("Sneak attack") == 0, "off: a hit paid Sneak XP or showed a Sneak attack message");
            if (first != null)
            {
                rig.Remove(first.gameObject);
            }
            if (second != null)
            {
                rig.Remove(second.gameObject);
            }
            yield return Frames(3);

            // ON again.
            Plugin.TestBlocked = false;
            FeatureRegistry.RefreshAll();
            Overrides();
            c.Check(view.IsActive && Plugin.FeatureActive, $"on: state {view.State}, status '{view.Status}'");
            MovePlayer(player, origin + Vector3.up * 0.05f);
            yield return Stand(player);
            var slot = new CloudSlot();
            yield return rig.PutCloud(slot, origin);
            var cloud = slot.Cloud;
            yield return WaitFor(() => cloud == null || cloud.IsActive(SmokeRegistry.Now, rules), 2f);
            if (!c.Check(cloud != null && InSmoke(player, rules), "on: the player is not inside an active cloud"))
            {
                c.Report();
                yield break;
            }

            // T20 with its setup: unaware two-star Greydwarf 10 m away, outside the cloud, AI on.
            var g = rig.Creature("Greydwarf", Ground(origin + dir * 10f), -dir, true);
            if (!c.Check(g != null, "could not spawn the two-star Greydwarf"))
            {
                c.Report();
                yield break;
            }
            g.SetLevel(3);
            yield return new WaitForSeconds(1.2f);
            var ai = (MonsterAI)g.GetBaseAI();
            WithoutSmoke(ai, player, out var wouldSee, out _);
            c.Check(wouldSee, "on: the Greydwarf 10 m away would not see the standing player without smoke either (scenery?): nothing proven");
            c.Check(!ai.IsAlerted() && !ReferenceEquals(ai.m_targetCreature, player), "on: the Greydwarf outside noticed the player in the smoke before the shot");
            var bow = Shared("Bow");
            var arrow = Shared("ArrowWood");
            var noise = (bow != null && bow.m_attack != null ? bow.m_attack.m_attackHitNoise : 0f)
                        + (arrow != null && arrow.m_attack != null ? arrow.m_attack.m_attackHitNoise : 0f);
            var shot = new Box();
            yield return Shoot(rig, "Bow", "ArrowWood", g, 20f, noise, shot);
            if (c.Check(shot.Ok && g != null && !g.IsDead(), $"on: the arrow did not hit, or killed the two-star Greydwarf ({shot.Detail})"))
            {
                c.Check(AiMemory.IsRevealed(g.transform, player, SmokeRegistry.Now) && ai.CanSeeTarget(player),
                    "on: the Greydwarf shot from the smoke does not see the player");
                var from = Vector3.Distance(g.transform.position, player.transform.position);
                var came = false;
                var attacked = false;
                var watch = Time.time;
                while (Time.time - watch < 8f && !(came && attacked) && g != null)
                {
                    if (!InSmoke(player, rules))
                    {
                        // Pushed out by a hit: back into the smoke.
                        MovePlayer(player, origin + Vector3.up * 0.05f);
                    }
                    yield return null;
                    if (g == null)
                    {
                        break;
                    }
                    came |= Vector3.Distance(g.transform.position, player.transform.position) < from - 3f;
                    attacked |= g.InAttack();
                }
                c.Check(g != null && ReferenceEquals(ai.m_targetCreature, player) && ai.IsAlerted(), "on: the shot Greydwarf does not have the player as its target");
                c.Check(came, "on: the shot Greydwarf did not come over within 8 s");
                c.Check(attacked, "on: the shot Greydwarf did not attack the player within 8 s");
            }
            NoProblems(c);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.smoke-unload, sneak.mp.unload (T28) ----------

    private static IEnumerator RunSmokeUnload()
    {
        var c = new Checks(SmokeUnloadName);
        var rig = Rig.Create(SmokeUnloadName);
        if (rig == null)
        {
            yield break;
        }
        var trip = new Trip();
        try
        {
            var rules = new AmbushRules();
            ServerRules.TestRules = rules;
            Tap.Install();
            yield return UnloadAndReturn(c, rig, rules, trip, false);
            c.Report();
        }
        finally
        {
            SendHome(trip, rig.Player);
            rig.Restore();
        }
    }

    // Dedicated server: while the player is away nobody is near the cloud, so no game holds it; the server keeps it.
    private static IEnumerator RunMpUnload()
    {
        var c = new Checks(MpUnloadName);
        var rig = Rig.Create(MpUnloadName);
        if (rig == null)
        {
            yield break;
        }
        var trip = new Trip();
        try
        {
            var ready = new Box();
            yield return ClientReady(c, ready);
            if (!ready.Ok)
            {
                c.Report();
                yield break;
            }
            Tap.Install();
            yield return UnloadAndReturn(c, rig, ServerRules.Current, trip, true);
            c.Report();
        }
        finally
        {
            SendHome(trip, rig.Player);
            rig.Restore();
        }
    }

    // Server half: the cloud with that start time now belongs to a player who is not on the server (what the cloud
    // of a thrower who left looks like). Arg = CloudStart in ms.
    private static IEnumerator ServerOrphanStep(string arg, object[] reply)
    {
        yield return null;
        long.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var start);
        var cloud = ServerZdos(SmokeContent.CloudName).FirstOrDefault(z => z.GetLong(SmokeCloud.StartKey, 0L) == start);
        if (cloud == null || start == 0L)
        {
            SelfTest.Answer(reply, false, $"no cloud with start {arg} on the server");
            yield break;
        }
        var before = cloud.GetOwner();
        cloud.SetOwner(DepartedPlayer);
        SelfTest.Answer(reply, cloud.GetOwner() == DepartedPlayer, $"owner={before}->{cloud.GetOwner()}");
    }

    // Real throw, away at once with the game's far teleport (the cloud's zone unloads: its object goes, the world
    // keeps it), back more than 20 s after the throw. The cloud that loads again is past its end.
    private static IEnumerator UnloadAndReturn(Checks c, Rig rig, AmbushRules rules, Trip trip, bool dedicated)
    {
        var player = rig.Player;
        var reply = new SelfTest.ServerReply();
        yield return Stand(player);
        yield return FreeHands(player);
        var dir = ClearDirection(player, 10f, out _);
        var side = Vector3.Cross(Vector3.up, dir).normalized;
        trip.Home = player.transform.position;
        trip.HomeRot = player.transform.rotation;
        if (!c.Check(LandNear(trip.Home, 400f, 760f, out var far), "no dry land 400 to 760 m away found to leave to"))
        {
            yield break;
        }
        var item = rig.GiveAndEquip(1);
        if (!c.Check(item != null, $"could not equip a Smoke Screen ({HandsText(player)})"))
        {
            yield break;
        }
        rig.Aim(dir, 35f);
        yield return new WaitForSeconds(0.6f);
        var before = new HashSet<SmokeCloud>(SmokeRegistry.All);
        c.Check(player.StartAttack(null, false), "StartAttack with a Smoke Screen refused");
        var slot = new CloudSlot();
        yield return WaitForCloud(before, 5f, slot);
        var cloud = slot.Cloud;
        var zdo = cloud != null && cloud.View != null ? cloud.View.GetZDO() : null;
        if (!c.Check(zdo != null && cloud.Ready, "no cloud within 5 s of the throw"))
        {
            yield break;
        }
        var id = zdo.m_uid;
        var startMs = zdo.GetLong(SmokeCloud.StartKey, 0L);
        var start = cloud.StartTime;
        var spot = cloud.Base;
        var life = cloud.Duration + SmokeCloud.FadeSeconds;
        double Age() => SmokeRegistry.Now - start;

        // Away at once.
        trip.Away = true;
        var went = new Box();
        yield return Travel(player, far + Vector3.up * 3f, went);
        if (!c.Check(went.Ok, $"could not travel away: {went.Detail}"))
        {
            yield break;
        }
        var distance = Utils.DistanceXZ(player.transform.position, spot);
        c.Check(distance > 300f, $"the player is only {F(distance)} m from the cloud after leaving");
        c.Check(cloud == null, $"the cloud is still loaded {F(distance)} m away ({F(Age())} s after the throw)");
        // Unloaded before its end (the zone goes about 2 s after the teleport starts), so the world still holds it:
        // only a game that loads it again can end it.
        c.Check(ZDOMan.instance.GetZDO(id) != null || dedicated,
            "the world no longer holds the cloud after its zone unloaded (it should until someone loads it again): nothing proven");
        if (dedicated)
        {
            yield return Ask(c, StepCloud, "", reply);
            c.Check(reply.Ok && Field(reply.Detail, "start") == startMs.ToString(CultureInfo.InvariantCulture),
                $"the server no longer holds the thrown cloud while the player is away: {reply.Detail}");
            c.Note($"cloud on the server while the player is {F(distance)} m away: owner {Field(reply.Detail, "owner")}, the player is {Field(reply.Detail, "player")}");
            // As after a rejoin: the game that threw it is no player of the server any more, so the cloud that
            // loads again is not this game's at first.
            yield return Ask(c, StepOrphan, startMs.ToString(CultureInfo.InvariantCulture), reply);
            c.Note($"cloud handed to a player who left, on the server: {reply.Detail}");
            // This game still hold its own copy of the cloud's entry (a game keep entries of far objects), and that
            // copy still say "mine": back home it would end the cloud as owner at once, like in single player, before
            // the server's word arrive. A game that rejoin has no copy: it get the entry from the server, owned by
            // the player who left. So the copy here is made to say what the server's say.
            var kept = ZDOMan.instance.GetZDO(id);
            if (kept != null)
            {
                kept.SetOwner(DepartedPlayer);
                c.Check(!kept.IsOwner(), "this game's copy of the cloud's entry still says the cloud is its own: the way back would not show what a rejoin finds");
            }
            else
            {
                c.Note("this game holds no copy of the cloud's entry while away: it gets it from the server on the way back, owned by the player who left");
            }
        }
        c.Note($"away {F(Age())} s after the throw, {F(distance)} m from the cloud ({went.Detail} for the trip)");

        // Back more than 20 s after the throw (default cloud: hides 15 s, fades 3 s).
        var wait = Math.Max(20d, life + 2d);
        // World clock that stand still must not hang me until the runner cut the test: real-time limit, then said.
        var waitBegan = Time.realtimeSinceStartup;
        while (Age() < wait && Time.realtimeSinceStartup - waitBegan < 45f)
        {
            yield return null;
        }
        if (!c.Check(Age() >= wait, $"the world's clock only reached {F(Age())} s after the throw in {F(Time.realtimeSinceStartup - waitBegan)} s of waiting "
                                     + $"({F(wait)} s needed before coming back)"))
        {
            yield break;
        }
        var backAfter = Age();
        Tap.Clear();
        var hid = false;
        var seen = 0;
        // Whose the old cloud was when this game saw it loaded again, said in a NOTE (single player: this game's; on a
        // server, after the hand to a player who left: not this game's, unless the server's hand-over came first).
        var firstOwner = "";
        // Blind list is emptied when the last cloud goes, so a count read after the trip says nothing: me look at it
        // every frame of the way back instead.
        var blindAtReturn = AiMemory.BlindCount;
        var blindRose = false;
        var back = new Box();
        var began = Time.realtimeSinceStartup;
        var leaving = player.TeleportTo(trip.Home + Vector3.up * 0.5f, trip.HomeRot, true);
        while (!leaving && Time.realtimeSinceStartup - began < 8f)
        {
            yield return null;
            leaving = player.TeleportTo(trip.Home + Vector3.up * 0.5f, trip.HomeRot, true);
        }
        if (!c.Check(leaving, "the game refused the teleport back home for 8 s"))
        {
            yield break;
        }
        yield return null;
        // Every frame of the way back and 3 s more: does a cloud at that spot ever hide?
        var arrivedAt = -1f;
        while (Time.realtimeSinceStartup - began < 60f)
        {
            var now = SmokeRegistry.Now;
            foreach (var late in SmokeRegistry.All)
            {
                if (late != null && !before.Contains(late))
                {
                    if (seen == 0)
                    {
                        firstOwner = late.View != null && late.View.IsValid()
                            ? (late.View.IsOwner() ? "this game's" : "not this game's")
                            : "without a valid world entry";
                    }
                    seen++;
                    hid |= late.IsActive(now, rules);
                }
            }
            hid |= SmokeRegistry.InActiveCloud(spot + Vector3.up, rules, now);
            blindRose |= AiMemory.BlindCount > blindAtReturn;
            if (!player.IsTeleporting())
            {
                if (arrivedAt < 0f)
                {
                    arrivedAt = Time.realtimeSinceStartup;
                    back.Ok = true;
                }
                // At home, the area there, the cloud's entry gone from the world: done (at most 3 s after arriving
                // in single player; a server may hand the cloud over a moment later).
                var gone = ZDOMan.instance.GetZDO(id) == null;
                var waited = Time.realtimeSinceStartup - arrivedAt;
                if ((gone && waited > 1f) || waited > (dedicated ? 12f : 3f))
                {
                    break;
                }
            }
            yield return null;
        }
        if (!c.Check(back.Ok, "the player did not get back home within 60 s"))
        {
            yield break;
        }
        trip.Away = false;
        var sinceArrival = Time.realtimeSinceStartup - arrivedAt;
        c.Check(Utils.DistanceXZ(player.transform.position, trip.Home) < 5f, $"back {F(Utils.DistanceXZ(player.transform.position, trip.Home))} m from the start");
        c.Check(ZDOMan.instance.GetZDO(id) == null,
            $"{F(Age())} s after the throw and {F(sinceArrival)} s after coming back, the old cloud is still in the world");
        c.Check(NewClouds(before).Count == 0, $"{NewClouds(before).Count} cloud(s) loaded at the spot after coming back");
        c.Check(!hid, "the cloud found after its end hid something");
        c.Check(CloudVisual(spot) == null, "there is smoke at the spot of the old cloud");
        // No pursuer stands at the spot when the old cloud loads (the zone was unloaded), so this only catches a burst
        // on a creature that happens to be there: the burst of a late cloud on a real pursuer is sneak.smoke-late.
        c.Check(!blindRose && Tap.Logged("Smoke Screen burst") == 0, "the cloud found after its end blinded a creature");
        c.Note($"came back {F(backAfter)} s after the throw; the old cloud was seen loaded in {seen} frame(s)"
               + (seen > 0 ? $" ({firstOwner} when first seen)" : "") + $" and was out of the world {F(sinceArrival)} s after arriving");
        if (dedicated)
        {
            yield return Ask(c, StepCloud, "gone|" + startMs.ToString(CultureInfo.InvariantCulture), reply);
            c.Check(reply.Ok, $"the server still holds the old cloud: {reply.Detail}");
        }

        // Hides nobody: the player on the old spot, an onlooker 8 m away sees him.
        MovePlayer(player, spot + Vector3.up * 0.1f);
        rig.MarkMoved();
        yield return Stand(player);
        var onlooker = rig.Creature("Greydwarf", Ground(spot + side * 8f), -side, false);
        if (c.Check(onlooker != null, "could not spawn an onlooker"))
        {
            yield return new WaitForSeconds(0.6f);
            Place(onlooker, onlooker.transform.position, player.transform.position - onlooker.transform.position);
            yield return Frames(2);
            c.Check(!InSmoke(player, rules), "the player on the old cloud's spot counts as in smoke");
            c.Check(onlooker.GetBaseAI().CanSeeTarget(player), "an onlooker 8 m away does not see the player standing on the old cloud's spot (scenery, or the old cloud?)");
        }
        NoProblems(c);
    }

    // ---------- sneak.smoke-water (T14) ----------

    // Shore by the world's own height rule (works for places that are not loaded): dry land with water at least
    // 1.6 m deep 14, 22 and 30 m out in one direction. Nearest first. Feet = the land spot, Dir = toward the water.
    private static IEnumerator FindShore(Vector3 from, float maxRange, Spot shore)
    {
        var gen = WorldGenerator.instance;
        var water = ZoneSystem.instance.m_waterLevel;
        shore.Found = false;
        for (var r = 0f; gen != null && r <= maxRange && !shore.Found; r += 24f)
        {
            var steps = r < 1f ? 1 : Mathf.Max(8, Mathf.CeilToInt(2f * Mathf.PI * r / 24f));
            for (var a = 0; a < steps && !shore.Found; a++)
            {
                var p = from + Quaternion.Euler(0f, 360f * a / steps, 0f) * Vector3.forward * r;
                var h = gen.GetHeight(p.x, p.z);
                if (h < water + 0.8f || h > water + 5f)
                {
                    continue;
                }
                for (var d = 0; d < 8; d++)
                {
                    var dir = Quaternion.Euler(0f, d * 45f, 0f) * Vector3.forward;
                    if (gen.GetHeight(p.x + dir.x * 14f, p.z + dir.z * 14f) < water - 1.6f
                        && gen.GetHeight(p.x + dir.x * 22f, p.z + dir.z * 22f) < water - 1.6f
                        && gen.GetHeight(p.x + dir.x * 30f, p.z + dir.z * 30f) < water - 1.6f)
                    {
                        shore.Found = true;
                        shore.Feet = new Vector3(p.x, h, p.z);
                        shore.Dir = dir;
                        break;
                    }
                }
            }
            // One frame per ring: search never freeze the game.
            yield return null;
        }
    }

    // Where a thrown projectile comes down to that height, by the game's own flight rule (Projectile.FixedUpdate:
    // gravity, drag, step). flight = seconds in the air.
    private static Vector3 ThrowLanding(Vector3 spawn, Vector3 velocity, float gravity, float drag, float level, out float flight)
    {
        var dt = Time.fixedDeltaTime;
        var p = spawn;
        flight = 0f;
        for (var i = 0; i < 1500; i++)
        {
            velocity += Vector3.down * (gravity * dt);
            var loss = velocity.sqrMagnitude * drag * dt;
            velocity += loss * -velocity.normalized;
            p += velocity * dt;
            flight += dt;
            if (p.y < level)
            {
                break;
            }
        }
        return p;
    }

    private static bool DeepAt(Vector3 p, float level, float depth) =>
        ZoneSystem.instance.GetGroundHeight(p, out var ground) && ground < level - depth;

    private static IEnumerator RunSmokeWater()
    {
        var c = new Checks(SmokeWaterName);
        var rig = Rig.Create(SmokeWaterName);
        if (rig == null)
        {
            yield break;
        }
        var trip = new Trip();
        try
        {
            var player = rig.Player;
            var rules = new AmbushRules();
            ServerRules.TestRules = rules;
            Tap.Install();
            yield return Stand(player);
            yield return FreeHands(player);
            trip.Home = player.transform.position;
            trip.HomeRot = player.transform.rotation;
            var level = ZoneSystem.instance.m_waterLevel;

            // A shore with deep water in front of it.
            var shore = new Spot();
            yield return FindShore(trip.Home, 2400f, shore);
            if (!c.Check(shore.Found, "no shore with deep water in front of it within 2400 m of the player (the world's height rule)"))
            {
                c.Report();
                yield break;
            }
            var away = Utils.DistanceXZ(shore.Feet, trip.Home);
            if (away > 40f || ZNetScene.instance.OutsideActiveArea(shore.Feet))
            {
                trip.Away = true;
                var went = new Box();
                yield return Travel(player, shore.Feet + Vector3.up * 3f, went);
                if (!c.Check(went.Ok, $"could not travel to the shore {F(away)} m away: {went.Detail}"))
                {
                    c.Report();
                    yield break;
                }
            }
            else
            {
                MovePlayer(player, Ground(shore.Feet) + Vector3.up * 0.05f);
                rig.MarkMoved();
                yield return WaitFor(() => player.IsOnGround(), 2f);
            }
            yield return new WaitForSeconds(0.5f);
            var feet = player.transform.position;
            if (!c.Check(player.IsOnGround() && !player.IsSwimming() && feet.y > level, $"the player does not stand on dry land at the shore ({V(feet)}, water level {F(level)})"))
            {
                c.Report();
                yield break;
            }

            // The real ground now: the direction with the longest stretch of deep water within throwing range.
            var toWater = shore.Dir;
            var bestFrom = 0f;
            var bestTo = 0f;
            for (var i = 0; i < 16; i++)
            {
                var d = Quaternion.Euler(0f, i * 22.5f, 0f) * shore.Dir;
                var from = -1f;
                var to = -1f;
                for (var s = 4f; s <= 44f; s += 1f)
                {
                    if (DeepAt(feet + d * s, level, 1.5f))
                    {
                        if (from < 0f)
                        {
                            from = s;
                        }
                        to = s;
                    }
                    else if (from >= 0f)
                    {
                        break;
                    }
                }
                if (from >= 0f && to - from > bestTo - bestFrom)
                {
                    bestFrom = from;
                    bestTo = to;
                    toWater = d;
                }
            }
            if (!c.Check(bestTo - bestFrom >= 8f, $"no stretch of deep water of 8 m or more within 44 m of the shore spot {V(feet)} (best {F(bestFrom)} to {F(bestTo)} m)"))
            {
                c.Report();
                yield break;
            }

            // The throw that lands in the middle of it: pitch chosen by the game's own flight rule.
            var item = rig.GiveAndEquip(1);
            var attack = item != null ? item.m_shared.m_attack : null;
            var flying = SmokeContent.ProjectilePrefab != null ? SmokeContent.ProjectilePrefab.GetComponent<Projectile>() : null;
            if (!c.Check(attack != null && flying != null, $"could not equip a Smoke Screen ({HandsText(player)})"))
            {
                c.Report();
                yield break;
            }
            Face(rig, toWater);
            yield return new WaitForSeconds(0.3f);
            var body = player.transform;
            var spawn = body.position + body.up * attack.m_attackHeight + body.forward * attack.m_attackRange + body.right * attack.m_attackOffset;
            var speedMax = attack.m_projectileVel;
            var speedMin = attack.m_randomVelocity ? attack.m_projectileVelMin : attack.m_projectileVel;
            var pitch = float.NaN;
            var planned = 0f;
            foreach (var candidate in new[] { 20f, 10f, 0f, -10f, -20f, -30f, 30f, -40f, 40f })
            {
                var aim = Quaternion.LookRotation(toWater) * Quaternion.Euler(candidate, 0f, 0f) * Vector3.forward;
                if (attack.m_launchAngle != 0f)
                {
                    aim = Quaternion.AngleAxis(attack.m_launchAngle, Vector3.Cross(Vector3.up, aim)) * aim;
                }
                var near = Utils.DistanceXZ(ThrowLanding(spawn, aim * speedMin, flying.m_gravity, flying.m_drag, level, out var flightMin), feet);
                var farthest = Utils.DistanceXZ(ThrowLanding(spawn, aim * speedMax, flying.m_gravity, flying.m_drag, level, out var flightMax), feet);
                if (Mathf.Min(near, farthest) >= bestFrom + 2f && Mathf.Max(near, farthest) <= bestTo - 2f
                    && Mathf.Max(flightMin, flightMax) < flying.m_ttl - 0.4f)
                {
                    pitch = candidate;
                    planned = (near + farthest) * 0.5f;
                    break;
                }
            }
            if (!c.Check(!float.IsNaN(pitch),
                    $"no throw from the shore lands in the deep water {F(bestFrom)} to {F(bestTo)} m out (speed {F(speedMin)}-{F(speedMax)}, gravity {F(flying.m_gravity)})"))
            {
                c.Report();
                yield break;
            }
            rig.Aim(toWater, pitch);
            yield return new WaitForSeconds(0.6f);
            var before = new HashSet<SmokeCloud>(SmokeRegistry.All);
            var stack = rig.SmokeCount();
            var drops = SmokeDrops();
            c.Check(player.StartAttack(null, false), "StartAttack with a Smoke Screen refused");
            var slot = new CloudSlot();
            yield return WaitForCloud(before, 8f, slot);
            var cloud = slot.Cloud;
            if (c.Check(cloud != null, $"no cloud within 8 s of the throw into the water (planned to land {F(planned)} m out)"))
            {
                yield return new WaitForSeconds(0.8f);
                c.Check(NewClouds(before).Count == 1, $"{NewClouds(before).Count} clouds from one throw into the water");
                c.Check(rig.SmokeCount() == stack - 1, $"stack {stack} -> {rig.SmokeCount()} after one throw");
                c.Check(SmokeDrops() == drops, "a Smoke Screen was dropped back after the throw into the water");
                if (cloud != null)
                {
                    CheckCloud(c, cloud, rules, "throw into deep water");
                    var at = cloud.Base;
                    var surface = Floating.GetLiquidLevel(at);
                    var bottom = ZoneSystem.instance.GetGroundHeight(at);
                    c.Check(Mathf.Abs(at.y - surface) < 0.5f && bottom < at.y - 1f,
                        $"cloud base {F(at.y)} is not on the surface of deep water (water {F(surface)}, bottom {F(bottom)})");
                    c.Check(cloud.IsActive(SmokeRegistry.Now, rules) && SmokeRegistry.InActiveCloud(at + Vector3.up, rules, SmokeRegistry.Now),
                        $"the cloud on the water does not hide {F(SmokeRegistry.Now - cloud.StartTime)} s after the burst");
                    c.Note($"burst {F(Utils.DistanceXZ(at, feet))} m out (planned {F(planned)} m, pitch {F(pitch)}): cloud base {F(at.y)}, water {F(surface)}, bottom {F(bottom)}; "
                           + $"shore {F(away)} m from the start");
                    SelfTest.Screenshot(SmokeWaterName, "cloud-on-water");
                    yield return null;
                    yield return null;
                }
            }
            NoProblems(c);
            if (trip.Away)
            {
                // Clouds made here go before the zone unloads (they would wait in the world save otherwise).
                foreach (var made in NewClouds(before))
                {
                    DestroyObject(made.gameObject);
                }
                yield return null;
                var home = new Box();
                yield return Travel(player, trip.Home + Vector3.up * 0.5f, home);
                trip.Away = !home.Ok;
                c.Check(home.Ok, $"could not travel back home: {home.Detail}");
            }
            c.Report();
        }
        finally
        {
            SendHome(trip, rig.Player);
            rig.Restore();
        }
    }

    // ---------- sneak.mist-real (T12) ----------

    // Mistlands land by the world's own biome rule (rings round the world's middle, where the biome lies), a few
    // spots far from each other.
    private static IEnumerator FindMistlands(List<Vector3> found, int want)
    {
        var gen = WorldGenerator.instance;
        var water = ZoneSystem.instance.m_waterLevel;
        for (var r = 6200f; gen != null && r <= 9800f && found.Count < want; r += 300f)
        {
            for (var a = 0; a < 360 && found.Count < want; a += 6)
            {
                var p = Quaternion.Euler(0f, a, 0f) * Vector3.forward * r;
                if (gen.GetBiome(p.x, p.z) != Heightmap.Biome.Mistlands)
                {
                    continue;
                }
                var h = gen.GetHeight(p.x, p.z);
                if (h < water + 2f || h > water + 25f)
                {
                    continue;
                }
                // Mistlands all around too (mist areas come with the biome's zones).
                if (gen.GetBiome(p.x + 60f, p.z) != Heightmap.Biome.Mistlands || gen.GetBiome(p.x - 60f, p.z) != Heightmap.Biome.Mistlands
                    || gen.GetBiome(p.x, p.z + 60f) != Heightmap.Biome.Mistlands || gen.GetBiome(p.x, p.z - 60f) != Heightmap.Biome.Mistlands)
                {
                    continue;
                }
                var spot = new Vector3(p.x, h, p.z);
                if (found.All(f => Utils.DistanceXZ(f, spot) > 400f))
                {
                    found.Add(spot);
                }
            }
            yield return null;
        }
    }

    // Dry ground inside a mist area of the game (Mister objects of the loaded zones), nearest first.
    private static bool MistGround(Player player, out Vector3 ground)
    {
        var level = ZoneSystem.instance.m_waterLevel;
        var origin = player.transform.position;
        var best = float.MaxValue;
        ground = origin;
        foreach (var mister in Mister.GetMisters())
        {
            if (mister == null)
            {
                continue;
            }
            var p = mister.transform.position;
            var d = Utils.DistanceXZ(p, origin);
            if (d > 110f || d >= best || !ZoneSystem.instance.GetGroundHeight(p, out var height) || height < level + 0.5f)
            {
                continue;
            }
            var feet = new Vector3(p.x, height, p.z);
            if (Mister.InsideMister(feet + Vector3.up * 0.9f))
            {
                best = d;
                ground = feet;
            }
        }
        return best < float.MaxValue;
    }

    private static IEnumerator RunMistReal()
    {
        var c = new Checks(MistRealName);
        var rig = Rig.Create(MistRealName);
        if (rig == null)
        {
            yield break;
        }
        var trip = new Trip();
        try
        {
            var player = rig.Player;
            // Only the mist: no holding-still bonus in the way of the icon row.
            var rules = new AmbushRules { StillBonus = 0 };
            ServerRules.TestRules = rules;
            StealthCues.TestShowCues = true;
            Tap.Install();
            yield return Stand(player);
            trip.Home = player.transform.position;
            trip.HomeRot = player.transform.rotation;
            c.Check(!Tap.ForceMist, "the forced mist of another test is still on");
            c.Check(!player.GetSEMan().GetStatusEffects().Any(se => se is SE_Demister), "the player wears a Wisplight: nothing to check");
            c.Check(!StealthCues.Has(player, CueKind.Mist), "In the mist icon before going to the Mistlands");

            var spots = new List<Vector3>();
            yield return FindMistlands(spots, 2);
            if (!c.Check(spots.Count > 0, "no Mistlands land found in this world (6200 to 9800 m from its middle)"))
            {
                c.Report();
                yield break;
            }
            var inMist = false;
            var tried = "";
            foreach (var spot in spots)
            {
                trip.Away = true;
                var went = new Box();
                yield return Travel(player, spot + Vector3.up * 3f, went);
                if (!went.Ok)
                {
                    tried += $"{V(spot)}: {went.Detail}; ";
                    continue;
                }
                // Biome known to the player, mist areas of the loaded zones there.
                yield return WaitFor(() => player.GetCurrentBiome() == Heightmap.Biome.Mistlands, 4f);
                var ground = Vector3.zero;
                var have = new Box();
                yield return WaitFor(() => MistGround(player, out ground), 5f, have);
                if (!have.Ok)
                {
                    tried += $"{V(spot)}: biome {player.GetCurrentBiome()}, {Mister.GetMisters().Count} mist area(s) loaded, none over dry ground within 110 m; ";
                    continue;
                }
                MovePlayer(player, ground + Vector3.up * 0.2f);
                player.m_maxAirAltitude = player.transform.position.y;
                yield return WaitFor(() => player.IsOnGround(), 3f);
                // The game's own answer (it looks at its mist areas ten times a second).
                var real = new Box();
                yield return WaitFor(() => ParticleMist.IsInMist(player.GetCenterPoint()), 5f, real);
                if (real.Ok && !player.IsSwimming())
                {
                    inMist = true;
                    break;
                }
                tried += $"{V(ground)}: the game says in mist {real.Ok}, swimming {player.IsSwimming()}; ";
            }
            if (!c.Check(inMist, $"could not stand in real Mistlands mist: {tried}"))
            {
                c.Report();
                yield break;
            }
            c.Check(player.GetCurrentBiome() == Heightmap.Biome.Mistlands, $"the player is in {player.GetCurrentBiome()}, not in the Mistlands");
            c.Note($"in real mist at {V(player.transform.position)}, {F(Utils.DistanceXZ(player.transform.position, trip.Home))} m from the start");

            // Standing: no icon. Crouched: icon without a number. Standing up: gone.
            yield return Stand(player);
            yield return new WaitForSeconds(0.7f);
            c.Check(ParticleMist.IsInMist(player.GetCenterPoint()) && !StealthCues.Has(player, CueKind.Mist), "standing in the mist: an In the mist icon shows");
            var crouched = new Box();
            yield return Crouch(player, crouched);
            var shown = new Box();
            yield return WaitFor(() => StealthCues.Has(player, CueKind.Mist), 2.5f, shown);
            c.Check(crouched.Ok && ParticleMist.IsInMist(player.GetCenterPoint()), $"the crouched player is not in mist any more (crouched {crouched.Ok})");
            if (c.Check(shown.Ok, "crouched in real Mistlands mist without a Wisplight: no In the mist icon"))
            {
                var effect = player.GetSEMan().GetStatusEffect(StealthCues.NameHash(CueKind.Mist));
                c.Check(effect != null && effect.GetIconText() == "", $"In the mist icon shows '{(effect != null ? effect.GetIconText() : "nothing")}', expected no number");
                SelfTest.Screenshot(MistRealName, "crouched-in-mist");
                yield return null;
                yield return null;
            }
            player.SetCrouch(false);
            var gone = new Box();
            yield return WaitFor(() => !StealthCues.Has(player, CueKind.Mist), 2.5f, gone);
            c.Check(gone.Ok, "standing up in the mist: the In the mist icon stays");
            NoProblems(c);

            var home = new Box();
            yield return Travel(player, trip.Home + Vector3.up * 0.5f, home);
            trip.Away = !home.Ok;
            c.Check(home.Ok, $"could not travel back home: {home.Detail}");
            if (home.Ok)
            {
                yield return new WaitForSeconds(0.7f);
                c.Check(!StealthCues.Has(player, CueKind.Mist), "back in the Meadows: an In the mist icon shows");
            }
            c.Report();
        }
        finally
        {
            SendHome(trip, rig.Player);
            rig.Restore();
        }
    }

    // ---------- sneak.foliage-oak, sneak.foliage-shade (T09) ----------

    private static IEnumerator RunFoliageOak()
    {
        var c = new Checks(FoliageOakName);
        var rig = Rig.Create(FoliageOakName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            var rules = new AmbushRules { StillBonus = 0, FogBonus = 0 };
            ServerRules.TestRules = rules;
            StealthCues.TestShowCues = true;
            Tap.Install();
            rig.SetEnv("Clear");
            rig.SetTime(0.5f);
            rig.SetSneak(0f);
            yield return Stand(player);
            var settled = new Box();
            yield return SettleEnv("Clear", settled);
            var place = new Spot();
            yield return FindOpenSpot(rig, 14f, true, null, place);
            if (!place.Found)
            {
                c.Note("no sunlit open ground found within 48 m of the player: other foliage may be over the spot already");
            }
            var dir = place.Dir;
            var crouched = new Box();
            yield return Crouch(player, crouched);
            if (!c.Check(crouched.Ok, "the player did not crouch"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(0.6f);
            yield return ForceRefresh(player);
            var openLight = LightFactor(player);
            c.Check(!StealthCues.Has(player, CueKind.Foliage), "In foliage icon on open ground before the oak");
            var feet = player.transform.position;
            if (!c.Check(ZNetScene.instance.GetPrefab(OakPrefab) != null, $"this game has no prefab named {OakPrefab}"))
            {
                c.Report();
                yield break;
            }
            // Trunk 4 m away: the player crouches under the crown, not in the trunk.
            var tree = rig.Spawn(OakPrefab, Ground(feet + dir * 4f), Quaternion.identity);
            if (c.Check(tree != null, $"could not spawn {OakPrefab}"))
            {
                yield return new WaitForSeconds(1.5f);
                var light = LightFactor(player);
                c.Check(StealthState.FoliageTouching || StealthState.FoliageCrown,
                    $"crouched under {OakPrefab}, 4 m from its trunk: no foliage found (player moved {V(player.transform.position - feet)})");
                var effect = player.GetSEMan().GetStatusEffect(StealthCues.NameHash(CueKind.Foliage));
                if (c.Check(effect != null, $"crouched under {OakPrefab}: no In foliage icon"))
                {
                    c.Check(effect.GetIconText() == "", $"under {OakPrefab}: In foliage icon shows '{effect.GetIconText()}', expected no number");
                }
                c.Note($"under {OakPrefab} at noon: touching {StealthState.FoliageTouching}, crown {StealthState.FoliageCrown}, light factor {F(light)} "
                       + $"(open ground {F(openLight)}); {SunText()}");
                // The oak gone (as stepping into the open): icon about 1 s more, then gone.
                rig.Remove(tree);
                var left = Time.time;
                var away = new Box();
                yield return WaitFor(() => !StealthCues.Has(player, CueKind.Foliage), 2.6f, away);
                c.Check(away.Ok, $"no oak any more: the In foliage icon is still there after {F(Time.time - left)} s");
            }
            NoProblems(c);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // "At noon the bar drops a little (shade)": bush on the sun's side of the crouched player, who is pressed against
    // its stem. Bar and light factor compared with the same spot without the bush.
    private static IEnumerator RunFoliageShade()
    {
        var c = new Checks(FoliageShadeName);
        var rig = Rig.Create(FoliageShadeName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            // Default rules but no holding-still and no fog bonus: the bar is the normal game's light curve times the
            // early-game factor, so only shade can move it. FoliageBonus is 0 by default.
            var rules = new AmbushRules { StillBonus = 0, FogBonus = 0 };
            ServerRules.TestRules = rules;
            StealthCues.TestShowCues = true;
            Tap.Install();
            rig.SetEnv("Clear");
            rig.SetTime(0.5f);
            rig.SetSneak(0f);
            yield return Stand(player);
            var settled = new Box();
            yield return SettleEnv("Clear", settled);
            var place = new Spot();
            yield return FindOpenSpot(rig, 6f, true, null, place);
            if (!c.Check(place.Found, "no sunlit open ground found within 48 m of the player: nothing to compare the shade with"))
            {
                c.Report();
                yield break;
            }
            var crouched = new Box();
            yield return Crouch(player, crouched);
            if (!c.Check(crouched.Ok, "the player did not crouch"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(0.6f);
            var open = new Box();
            yield return Settle(player, open, 8f);
            var openLight = LightFactor(player);
            var openBar = player.m_stealthFactor;
            c.Check(open.Ok && openLight >= 0.9f, $"clear noon on open ground: light factor {F(openLight)}, bar {F(openBar)} (settled {open.Ok}); full light expected");
            var feet = player.transform.position;
            var sun = EnvMan.instance.m_dirLight;
            var toSun = sun != null ? -sun.transform.forward : Vector3.up;
            var tilt = Vector3.Angle(toSun, Vector3.up);
            var sunSide = tilt > 2f ? Flat(toSun) : place.Dir;
            c.Note($"noon sun {F(tilt)} degrees from straight up; {SunText()}");

            foreach (var name in new[] { "RaspberryBush", "Bush01" })
            {
                var at = feet + sunSide * (player.GetRadius() + BushStemRadius + 0.02f);
                at.y = ZoneSystem.instance.GetSolidHeight(at, out var solid, 2) ? solid : Ground(at).y;
                var bush = rig.Spawn(name, at, Quaternion.identity);
                if (!c.Check(bush != null, $"could not spawn {name}"))
                {
                    continue;
                }
                yield return new WaitForSeconds(1.2f);
                var shaded = new Box();
                yield return Settle(player, shaded, 8f);
                var light = LightFactor(player);
                var bar = player.m_stealthFactor;
                var moved = Utils.DistanceXZ(player.transform.position, feet);
                c.Check(StealthCues.Has(player, CueKind.Foliage), $"crouched against {name}: no In foliage icon");
                c.Check(shaded.Ok && light < openLight - 0.01f && bar < openBar - 0.005f,
                    $"crouched against {name} at noon, the bush on the sun's side: bar {F(openBar)} -> {F(bar)}, light factor {F(openLight)} -> {F(light)}: "
                    + $"no drop (player moved {F(moved)} m; {Colliders(bush)})");
                rig.Remove(bush);
                MovePlayer(player, feet + Vector3.up * 0.02f);
                yield return new WaitForSeconds(1.6f);
                yield return Settle(player, open, 8f);
                openLight = LightFactor(player);
                openBar = player.m_stealthFactor;
            }
            NoProblems(c);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }
}
#endif
