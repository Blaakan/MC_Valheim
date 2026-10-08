#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Exploration.ViewSpyglassMod;

// Debug build only. Me = in-world self tests around the spyglass (TESTING.md items in brackets): recipe and crafting,
// hands, what round view draw, close walls, fog with and without Distant Horizons, live toggle, other MC mods, log.
//   spyglass.recipe          recipe learned only once Bronze and Crystal are known ("New recipe" with the icon), Forge
//                            list, cost shown, a real craft (2 + 2 -> 1), tooltip [T01]
//   spyglass.hands           torch, shield, sword, axe against the spyglass; R hangs it where the hammer hangs [T02]
//   spyglass.overlay         round view numbers (size, blur, dark edge), no crosshair, no hover name, HUD still on,
//                            canvas order [T04 T17]
//   spyglass.settings        each view setting changes the view on the next frames; zoom capped by the rules [T16]
//   spyglass.blur-off        EdgeBlur turned off while looking: no blur ring; raised with it off: no frame copy
//   spyglass.bug.blur-off-capture   (fail today) EdgeBlur turned off while looking also stops the frame copy
//   spyglass.close          near plane at the game's minimum: a wall or roof right before the face stays drawn; own
//                            body hidden on every frame the camera is in the head or at the feet [T20 T21]
//   spyglass.no-horizons     without Distant Horizons the fog is never touched [T11]
//   spyglass.horizons        with it: less haze in clear weather, message to it follows the look, all back when
//                            lowered [T12 X01]
//   spyglass.rain            with it: rain keeps its fog [T13]
//   spyglass.toggle          feature off while looking: everything back at once, spyglass kept, clicks do nothing,
//                            recipe hidden, inventory still loads it; on again: works [T15 T18 T19]
//   spyglass.compat.swim     falling into deep water with the spyglass up: view back when swimming starts [X05 T10]
//   spyglass.compat.dualwield, .towershield, .sort   other MC mods [X03 X04 X02]
//   spyglass.bug.missing-knife-error   (fail with some other mods) no false "Flint Knife is missing" error at the
//                            main menu [T23]
//   spyglass.log             no other warning or error of this mod since it started [T23]
internal static partial class SelfTests
{
    private const string RecipeTest = "spyglass.recipe";
    private const string HandsTest = "spyglass.hands";
    private const string OverlayTest = "spyglass.overlay";
    private const string SettingsTest = "spyglass.settings";
    private const string BlurOffTest = "spyglass.blur-off";
    private const string BlurOffBugTest = "spyglass.bug.blur-off-capture";
    private const string KnifeBugTest = "spyglass.bug.missing-knife-error";
    private const string CloseTest = "spyglass.close";
    private const string NoHorizonsTest = "spyglass.no-horizons";
    private const string HorizonsTest = "spyglass.horizons";
    private const string RainTest = "spyglass.rain";
    private const string ToggleTest = "spyglass.toggle";
    private const string SwimTest = "spyglass.compat.swim";
    private const string DualTest = "spyglass.compat.dualwield";
    private const string TowerTest = "spyglass.compat.towershield";
    private const string SortTest = "spyglass.compat.sort";
    private const string LogTest = "spyglass.log";

    private const string DualWieldGuid = "MC.Combat.Weapons.DualWield";
    private const string TowerWallGuid = "MC.Combat.Shields.TowerWall";

    // Every test of this file and of SelfTestsInput.cs, in run order (spyglass.log last: it look back at all of them).
    private static readonly KeyValuePair<string, Func<IEnumerator>>[] MoreTests =
    {
        new KeyValuePair<string, Func<IEnumerator>>(RecipeTest, RunRecipe),
        new KeyValuePair<string, Func<IEnumerator>>(HandsTest, RunHands),
        new KeyValuePair<string, Func<IEnumerator>>(ClickTest, RunClick),
        new KeyValuePair<string, Func<IEnumerator>>(FeetTest, RunFeet),
        new KeyValuePair<string, Func<IEnumerator>>(WheelTest, RunWheel),
        new KeyValuePair<string, Func<IEnumerator>>(ZoomPickTest, RunZoomPick),
        new KeyValuePair<string, Func<IEnumerator>>(ZoomPickBugTest, RunZoomPickBug),
        new KeyValuePair<string, Func<IEnumerator>>(HoldTest, RunHold),
        new KeyValuePair<string, Func<IEnumerator>>(LowersTest, RunLowers),
        new KeyValuePair<string, Func<IEnumerator>>(ChatTest, RunChat),
        new KeyValuePair<string, Func<IEnumerator>>(AbortsTest, RunAborts),
        new KeyValuePair<string, Func<IEnumerator>>(StaggerTest, RunStagger),
        new KeyValuePair<string, Func<IEnumerator>>(CannotTest, RunCannot),
        new KeyValuePair<string, Func<IEnumerator>>(HelmTest, RunHelm),
        new KeyValuePair<string, Func<IEnumerator>>(PendingTest, RunPending),
        new KeyValuePair<string, Func<IEnumerator>>(OverlayTest, RunOverlay),
        new KeyValuePair<string, Func<IEnumerator>>(SettingsTest, RunSettings),
        new KeyValuePair<string, Func<IEnumerator>>(BlurOffTest, RunBlurOff),
        new KeyValuePair<string, Func<IEnumerator>>(BlurOffBugTest, RunBlurOffBug),
        new KeyValuePair<string, Func<IEnumerator>>(CloseTest, RunClose),
        new KeyValuePair<string, Func<IEnumerator>>(NoHorizonsTest, RunNoHorizons),
        new KeyValuePair<string, Func<IEnumerator>>(HorizonsTest, RunHorizons),
        new KeyValuePair<string, Func<IEnumerator>>(RainTest, RunRain),
        new KeyValuePair<string, Func<IEnumerator>>(ToggleTest, RunToggle),
        new KeyValuePair<string, Func<IEnumerator>>(SwimTest, RunSwim),
        new KeyValuePair<string, Func<IEnumerator>>(DualTest, RunDualWield),
        new KeyValuePair<string, Func<IEnumerator>>(TowerTest, RunTowerShield),
        new KeyValuePair<string, Func<IEnumerator>>(SortTest, RunSort),
        new KeyValuePair<string, Func<IEnumerator>>(KnifeBugTest, RunKnifeBug),
        new KeyValuePair<string, Func<IEnumerator>>(LogTest, RunLog),
    };

    private static void RegisterMore()
    {
        InstallTap();
        foreach (var test in MoreTests)
        {
            SelfTest.Register(test.Key, test.Value);
        }
        RegisterMultiplayer();
    }

    private static void UnregisterMore()
    {
        foreach (var test in MoreTests)
        {
            SelfTest.Unregister(test.Key);
        }
        UnregisterMultiplayer();
    }

    // ---------- helpers ----------

    private static bool OtherModActive(string guid) => FeatureRegistry.Find(guid) is FeatureView view && view.IsActive;

    // Me find free ground near player (nothing solid in capsule of that radius), in front first, then around.
    private static bool FreeSpot(Player player, float[] distances, float clear, out Vector3 spot)
    {
        var mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "vehicle", "character");
        var zs = ZoneSystem.instance;
        var center = player.transform.position;
        var forward = player.transform.forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        foreach (var d in distances)
        {
            for (var step = 0; step < 24; step++)
            {
                var angle = (step % 2 == 0 ? 1f : -1f) * ((step + 1) / 2) * 15f;
                var p = center + Quaternion.Euler(0f, angle, 0f) * forward * d;
                p.y = zs.GetGroundHeight(p);
                if (p.y < zs.m_waterLevel + 0.5f || Mathf.Abs(p.y - center.y) > 1.5f)
                {
                    continue;
                }
                if (Physics.CheckCapsule(p + Vector3.up * (clear + 0.3f), p + Vector3.up * 2.5f, clear, mask, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }
                spot = p;
                return true;
            }
        }
        spot = Vector3.zero;
        return false;
    }

    private static int CountSpyglasses(Inventory inv)
    {
        var n = 0;
        foreach (var item in inv.GetAllItems())
        {
            if (SpyglassContent.IsSpyglass(item))
            {
                n += item.m_stack;
            }
        }
        return n;
    }

    private static string SharedName(string prefab)
    {
        var go = ObjectDB.instance.GetItemPrefab(prefab);
        var drop = go != null ? go.GetComponent<ItemDrop>() : null;
        return drop != null ? drop.m_itemData.m_shared.m_name : null;
    }

    private static string Text(string token) => Localization.instance != null ? Localization.instance.Localize(token) : token;

    // Same object in the list? (Plain objects: the game's message types are private.)
    private static bool Holds(object[] list, object item)
    {
        foreach (var entry in list)
        {
            if (ReferenceEquals(entry, item))
            {
                return true;
            }
        }
        return false;
    }

    // What player know (recipes, materials, stations) and unlock messages still waiting: me put back at Restore.
    // Call me before OnRestore steps that take test items out of inventory: those run first (last added, first run),
    // so no item teach the player again after this.
    private static void KeepKnown(SpyRig rig)
    {
        var p = rig.Player;
        var recipes = new HashSet<string>(p.m_knownRecipes);
        var materials = new HashSet<string>(p.m_knownMaterial);
        var stations = new Dictionary<string, int>(p.m_knownStations);
        var hud = MessageHud.instance;
        var queued = hud != null ? hud.m_unlockMsgQueue.ToArray() : null;
        rig.OnRestore("what the player knows", () =>
        {
            p.m_knownRecipes.Clear();
            foreach (var r in recipes)
            {
                p.m_knownRecipes.Add(r);
            }
            p.m_knownMaterial.Clear();
            foreach (var m in materials)
            {
                p.m_knownMaterial.Add(m);
            }
            p.m_knownStations.Clear();
            foreach (var s in stations)
            {
                p.m_knownStations[s.Key] = s.Value;
            }
            if (hud != null && queued != null)
            {
                var now = hud.m_unlockMsgQueue.ToArray();
                hud.m_unlockMsgQueue.Clear();
                foreach (var m in now)
                {
                    if (Holds(queued, m))
                    {
                        hud.m_unlockMsgQueue.Enqueue(m);
                    }
                }
                hud.m_unlockMsgCount = Mathf.Min(hud.m_unlockMsgCount, hud.m_unlockMsgQueue.Count);
            }
        });
    }

    // Materials given by name (stacks may join stacks player had): surplus go at Restore.
    private static void KeepCount(SpyRig rig, string sharedName)
    {
        var inv = rig.Player.GetInventory();
        var had = inv.CountItems(sharedName);
        rig.OnRestore("materials " + sharedName, () =>
        {
            var extra = inv.CountItems(sharedName) - had;
            if (extra > 0)
            {
                inv.RemoveItem(sharedName, extra);
            }
        });
    }

    // Spyglasses the test made (crafted, picked up): gone at Restore like given ones.
    private static void TrackNewSpyglasses(SpyRig rig, HashSet<ItemDrop.ItemData> before)
    {
        foreach (var item in rig.Player.GetInventory().GetAllItems())
        {
            if (SpyglassContent.IsSpyglass(item) && !before.Contains(item))
            {
                rig.Track(item);
            }
        }
    }

    // Recipe's station (Forge with default rules) next to player, inside its discover range; gone at Restore.
    private static CraftingStation SpawnStation(Checks c, SpyRig rig)
    {
        var name = ServerRules.Current.RecipeStation;
        var prefab = string.IsNullOrEmpty(name) ? null : ZNetScene.instance.GetPrefab(name);
        if (!c.Check(prefab != null && prefab.GetComponentInChildren<CraftingStation>(true) != null, $"station prefab '{name}' exists"))
        {
            return null;
        }
        if (!c.Check(FreeSpot(rig.Player, new[] { 3f, 3.4f, 2.6f }, 1.2f, out var spot), "free ground for the station within 3.4 m"))
        {
            return null;
        }
        var toPlayer = rig.Player.transform.position - spot;
        toPlayer.y = 0f;
        var go = Object.Instantiate(prefab, spot, Quaternion.LookRotation(toPlayer.normalized));
        var player = rig.Player;
        rig.OnRestore("station", () =>
        {
            if (InventoryGui.instance != null && InventoryGui.IsVisible())
            {
                InventoryGui.instance.Hide();
            }
            player.SetCraftingStation(null);
            if (go != null && ZNetScene.instance != null)
            {
                ZNetScene.instance.Destroy(go);
            }
        });
        var station = go.GetComponentInChildren<CraftingStation>();
        if (c.Check(station != null, "station spawned"))
        {
            // Window stay open from where player stand.
            station.m_useDistance = 50f;
        }
        return station;
    }

    // Station window like player open it; row[0] = row of spyglass in its craft list (-1 = not listed).
    private static IEnumerator OpenStation(Player player, CraftingStation station, int[] row)
    {
        var gui = InventoryGui.instance;
        player.SetCraftingStation(station);
        gui.Show(null, 3);
        yield return new WaitForSecondsRealtime(1f);
        row[0] = -1;
        for (var i = 0; i < gui.m_availableRecipes.Count; i++)
        {
            if (ReferenceEquals(gui.m_availableRecipes[i].Recipe, SpyglassContent.CraftRecipe))
            {
                row[0] = i;
            }
        }
    }

    private static IEnumerator CloseStation(Player player)
    {
        InventoryGui.instance.Hide();
        player.SetCraftingStation(null);
        yield return WaitReal(() => !InventoryGui.IsVisible(), 3f);
        yield return null;
    }

    // Craft button on that row; the craft time skipped.
    private static IEnumerator CraftRow(int row)
    {
        var gui = InventoryGui.instance;
        gui.SetRecipe(row, false);
        yield return null;
        yield return null;
        gui.OnCraftPressed();
        yield return null;
        if (gui.m_craftTimer >= 0f)
        {
            gui.m_craftTimer = 1000f;
        }
        yield return Frames(3);
    }

    // "Name amount, Name amount" of the cost rows the craft panel shows now ("" = panel not readable).
    private static string ShownCost()
    {
        var parts = new List<string>();
        foreach (var go in InventoryGui.instance.m_recipeRequirementList)
        {
            var name = go != null ? go.transform.Find("res_name") : null;
            var amount = go != null ? go.transform.Find("res_amount") : null;
            if (name == null || amount == null || !name.gameObject.activeSelf)
            {
                continue;
            }
            var n = name.GetComponent<TMPro.TMP_Text>();
            var a = amount.GetComponent<TMPro.TMP_Text>();
            if (n != null && a != null)
            {
                parts.Add(n.text + " " + a.text);
            }
        }
        return string.Join(", ", parts.ToArray());
    }

    // Station learned by standing next to it (game look once a second), else me teach by hand and say so.
    private static IEnumerator KnowStation(Checks c, Player player, CraftingStation station)
    {
        yield return WaitReal(() => player.m_knownStations.ContainsKey(station.m_name), 5f);
        if (!player.m_knownStations.ContainsKey(station.m_name))
        {
            c.Note("the station was not noticed by itself within 5 s: taught by hand");
            player.AddKnownStation(station);
        }
    }

    // ---------- spyglass.recipe ----------

    private static IEnumerator RunRecipe()
    {
        var c = new Checks(RecipeTest);
        if (!Ready(RecipeTest, out var player, out _))
        {
            yield break;
        }
        var rig = new SpyRig(player, RecipeTest);
        try
        {
            rig.TakeControls();
            rig.Noon();
            // Default rules, whatever player's settings say.
            ServerRules.TestRules = new SpyglassRules();
            SpyglassContent.Rebuild();
            rig.OnRestore("recipe", () => SpyglassContent.Rebuild());
            var recipe = SpyglassContent.CraftRecipe;
            var inv = player.GetInventory();
            var hud = MessageHud.instance;
            var bronze = SharedName("Bronze");
            var crystal = SharedName("Crystal");
            if (!c.Check(recipe != null && recipe.m_enabled && recipe.m_item != null && bronze != null && crystal != null && hud != null,
                    "recipe on, Bronze and Crystal exist"))
            {
                c.Report();
                yield break;
            }
            var spyName = recipe.m_item.m_itemData.m_shared.m_name;
            var cost = new List<string>();
            foreach (var r in recipe.m_resources)
            {
                cost.Add(r.m_resItem.name + ":" + r.m_amount);
            }
            c.Check(string.Join(",", cost.ToArray()) == "Bronze:2,Crystal:2" && recipe.m_amount == 1,
                $"recipe: {string.Join(",", cost.ToArray())} makes {recipe.m_amount}");

            // Character that never saw Crystal (nor Bronze, recipe, station): me make it forget for the test.
            KeepKnown(rig);
            var held = new List<ItemDrop.ItemData>();
            foreach (var item in inv.GetAllItems())
            {
                if (item.m_shared.m_name == crystal || item.m_shared.m_name == bronze)
                {
                    held.Add(item);
                }
            }
            rig.OnRestore("materials the player had", () =>
            {
                foreach (var item in held)
                {
                    inv.AddItem(item);
                }
            });
            foreach (var item in held)
            {
                inv.RemoveItem(item);
            }
            KeepCount(rig, bronze);
            KeepCount(rig, crystal);
            var spyglasses = new HashSet<ItemDrop.ItemData>();
            foreach (var item in inv.GetAllItems())
            {
                if (SpyglassContent.IsSpyglass(item))
                {
                    spyglasses.Add(item);
                }
            }
            var station = SpawnStation(c, rig);
            if (station == null)
            {
                c.Report();
                yield break;
            }
            player.m_knownRecipes.Remove(spyName);
            player.m_knownMaterial.Remove(crystal);
            player.m_knownMaterial.Remove(bronze);
            player.m_knownStations.Remove(station.m_name);

            // Bronze picked up next to a Forge: no recipe yet.
            inv.AddItem("Bronze", 2, 1, 0, 0L, "", false);
            yield return KnowStation(c, player, station);
            player.UpdateKnownRecipesList();
            c.Check(player.m_knownMaterial.Contains(bronze) && !player.m_knownMaterial.Contains(crystal), "Bronze picked up: known; Crystal not known");
            c.Check(!player.IsRecipeKnown(spyName), "Bronze and a Forge only: no spyglass recipe yet");
            var row = new int[1];
            yield return OpenStation(player, station, row);
            c.Check(row[0] < 0, "not in the Forge's craft list yet");
            yield return CloseStation(player);

            // Crystal picked up: "New recipe" with the spyglass icon.
            var waiting = hud.m_unlockMsgQueue.ToArray();
            inv.AddItem("Crystal", 2, 1, 0, 0L, "", false);
            c.Check(player.IsRecipeKnown(spyName), "Crystal picked up: spyglass recipe learned");
            var announced = false;
            var iconOk = false;
            foreach (var m in hud.m_unlockMsgQueue)
            {
                if (!Holds(waiting, m) && m.m_description == Text(spyName))
                {
                    announced = m.m_topic == Text("$msg_newrecipe");
                    iconOk = m.m_icon != null && m.m_icon == recipe.m_item.m_itemData.GetIcon();
                }
            }
            c.Check(announced, $"unlock message \"{Text("$msg_newrecipe")}\" for {Text(spyName)}");
            c.Check(iconOk && recipe.m_item.m_itemData.GetIcon().name == "MC_Spyglass", "the message carries the spyglass icon");

            // At the Forge: listed, cost shown, one craft.
            yield return OpenStation(player, station, row);
            if (c.Check(row[0] >= 0, "listed in the Forge's craft list"))
            {
                var gui = InventoryGui.instance;
                c.Check(gui.m_availableRecipes[row[0]].CanCraft, "can be crafted with 2 Bronze and 2 Crystal");
                gui.SetRecipe(row[0], false);
                yield return null;
                yield return null;
                var shown = ShownCost();
                if (shown.Length == 0)
                {
                    c.Note("craft panel cost rows not readable (another interface mod?): cost checked by the craft only");
                }
                else
                {
                    c.Check(shown == $"{Text(bronze)} 2, {Text(crystal)} 2", $"craft panel shows the cost: {shown}");
                }
                var had = CountSpyglasses(inv);
                yield return CraftRow(row[0]);
                TrackNewSpyglasses(rig, spyglasses);
                c.Check(CountSpyglasses(inv) == had + 1, $"one craft makes one spyglass ({CountSpyglasses(inv) - had})");
                c.Check(inv.CountItems(bronze) == 0 && inv.CountItems(crystal) == 0,
                    $"and takes 2 Bronze and 2 Crystal ({inv.CountItems(bronze)} and {inv.CountItems(crystal)} left)");
            }
            yield return CloseStation(player);

            // Tooltip: two-handed like the hammer, no damage, the controls.
            var data = recipe.m_item.m_itemData;
            var tip = ItemDrop.ItemData.GetTooltip(data, 1, false, Game.m_worldLevel);
            var hammer = ObjectDB.instance.GetItemPrefab("Hammer");
            var hammerTip = hammer != null ? ItemDrop.ItemData.GetTooltip(hammer.GetComponent<ItemDrop>().m_itemData, 1, false, Game.m_worldLevel) : "";
            c.Check(tip.Contains("$item_twohanded") && hammerTip.Contains("$item_twohanded"), "tooltip says two-handed, like the hammer's");
            var damage = new[] { "$inventory_damage", "$inventory_blunt", "$inventory_slash", "$inventory_pierce", "$item_knockback", "$item_backstab", "$item_staminause", "$item_durability", "$item_blockpower", "$item_blockarmor" };
            var found = new List<string>();
            foreach (var token in damage)
            {
                if (tip.Contains(token))
                {
                    found.Add(token);
                }
            }
            c.Check(found.Count == 0, "tooltip shows no damage, block or durability" + (found.Count == 0 ? "" : " (found " + string.Join(" ", found.ToArray()) + ")"));
            var d = SpyglassContent.Description;
            c.Check(tip.Contains(d) && d.Contains("attack to raise") && d.Contains("block to lower") && d.Contains("mouse wheel zooms") && d.Contains("cannot walk"),
                "tooltip description explains the controls (attack, block, wheel, no walking)");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- spyglass.hands ----------

    private static IEnumerator RunHands()
    {
        var c = new Checks(HandsTest);
        if (!Ready(HandsTest, out var player, out _))
        {
            yield break;
        }
        var rig = new SpyRig(player, HandsTest);
        try
        {
            rig.TakeControls();
            var shieldName = "ShieldWood";
            if (ObjectDB.instance.GetItemPrefab(shieldName) == null)
            {
                shieldName = null;
                foreach (var prefab in ObjectDB.instance.m_items)
                {
                    var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                    if (drop != null && drop.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield
                        && drop.m_itemData.m_shared.m_icons != null && drop.m_itemData.m_shared.m_icons.Length > 0)
                    {
                        shieldName = prefab.name;
                        break;
                    }
                }
                c.Note($"ShieldWood is not an item of this game: using {shieldName ?? "nothing"}");
            }
            var spy = rig.Give(SpyglassContent.ItemName);
            var torch = rig.Give("Torch");
            var sword = rig.Give("SwordIron");
            var axe = rig.Give("AxeBronze");
            var hammer = rig.Give("Hammer");
            var shield = shieldName != null ? rig.Give(shieldName) : null;
            if (!c.Check(spy != null && torch != null && sword != null && axe != null && hammer != null && shield != null,
                    "test items given (spyglass, Torch, SwordIron, AxeBronze, Hammer, a shield)"))
            {
                c.Report();
                yield break;
            }
            void Empty()
            {
                player.UnequipItem(player.GetRightItem(), false);
                player.UnequipItem(player.GetLeftItem(), false);
            }
            string Hands() => $"right {(player.GetRightItem() != null ? player.GetRightItem().m_shared.m_name : "-")}, left {(player.GetLeftItem() != null ? player.GetLeftItem().m_shared.m_name : "-")}";

            // Torch, then the spyglass.
            Empty();
            player.EquipItem(torch, false);
            c.Check(player.IsItemEquiped(torch), "torch in hand");
            player.EquipItem(spy, false);
            c.Check(player.GetRightItem() == spy && player.GetLeftItem() == null && !player.IsItemEquiped(torch),
                $"torch then spyglass: spyglass in the right hand, torch put away ({Hands()})");

            // Shield in the left hand, then the spyglass.
            Empty();
            player.EquipItem(shield, false);
            c.Check(player.GetLeftItem() == shield, $"{shieldName} in the left hand ({Hands()})");
            player.EquipItem(spy, false);
            c.Check(player.GetRightItem() == spy && player.GetLeftItem() == null && !player.IsItemEquiped(shield),
                $"shield then spyglass: shield put away, both hands taken ({Hands()})");

            // Torch, sword, axe: each puts the spyglass away.
            player.EquipItem(torch, false);
            c.Check(player.IsItemEquiped(torch) && !player.IsItemEquiped(spy), $"spyglass then torch: spyglass put away ({Hands()})");
            Empty();
            player.EquipItem(spy, false);
            player.EquipItem(sword, false);
            c.Check(player.GetRightItem() == sword && !player.IsItemEquiped(spy), $"spyglass then sword: spyglass put away ({Hands()})");
            Empty();
            player.EquipItem(spy, false);
            player.EquipItem(axe, false);
            c.Check(player.GetRightItem() == axe && !player.IsItemEquiped(spy), $"spyglass then axe: spyglass put away ({Hands()})");

            // R: on the back joint of the tools, where the hammer hangs.
            Empty();
            player.EquipItem(spy, false);
            yield return Frames(5);
            var vis = player.m_visEquipment;
            player.HideHandItems();
            yield return Frames(10);
            c.Check(player.GetRightItem() == null && player.m_hiddenRightItem == spy, "R puts the spyglass away");
            var back = vis.m_rightBackItemInstance;
            c.Check(vis.m_currentRightBackItemHash == SpyglassContent.ItemHash && back != null && vis.m_backTool != null
                    && back.transform.IsChildOf(vis.m_backTool) && FindDeep(back.transform, SpyglassModel.ModelName) != null,
                "the spyglass model hangs on the tool joint of the back");
            SelfTest.Screenshot(HandsTest, "on-the-back");
            yield return null;
            yield return null;
            player.ShowHandItems();
            yield return Frames(5);
            c.Check(player.GetRightItem() == spy, "R again: back in the right hand");
            player.EquipItem(hammer, false);
            yield return Frames(5);
            player.HideHandItems();
            yield return Frames(10);
            var hammerBack = vis.m_rightBackItemInstance;
            c.Check(player.m_hiddenRightItem == hammer && hammerBack != null && hammerBack.transform.IsChildOf(vis.m_backTool),
                "the hammer hangs on the same joint");
            player.UnequipItem(hammer, false);
            yield return Frames(3);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- spyglass.overlay ----------

    private static Canvas RootCanvasOf(GameObject go)
    {
        var canvas = go != null ? go.GetComponentInParent<Canvas>(true) : null;
        return canvas != null ? canvas.rootCanvas : null;
    }

    private static IEnumerator RunOverlay()
    {
        var c = new Checks(OverlayTest);
        if (!Ready(OverlayTest, out var player, out var cam))
        {
            yield break;
        }
        var rig = new SpyRig(player, OverlayTest);
        try
        {
            rig.Noon();
            var spy = Hold(rig, c);
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            // Defaults, whatever player's settings say.
            ServerRules.TestRules = new SpyglassRules();
            ScopeOverlay.TestClearViewSize = ScopeOverlay.DefaultClearSize;
            ScopeOverlay.TestEdgeBlur = true;
            ScopeOverlay.TestEdgeDarkness = ScopeOverlay.DefaultEdgeDarkness;
            Scope.TestAimSensitivity = 1f;
            Scope.TestSetMagnification(4f);

            // Something with a name right in front: a chest. (The ground is "pointed at" too: me wait for a name.)
            bool Named() => Hud.instance.m_hoverName != null && Hud.instance.m_hoverName.text.Length > 0;
            var chestPrefab = ZNetScene.instance.GetPrefab("piece_chest_wood");
            GameObject chest = null;
            var spot = Vector3.zero;
            var room = chestPrefab != null && FreeSpot(player, new[] { 2f, 2.3f, 1.8f }, 0.6f, out spot);
            if (c.Check(room, "a chest and free ground in front"))
            {
                chest = Object.Instantiate(chestPrefab, spot, Quaternion.identity);
                rig.OnRestore("chest", () =>
                {
                    if (chest != null && ZNetScene.instance != null)
                    {
                        ZNetScene.instance.Destroy(chest);
                    }
                });
                var to = spot - player.transform.position;
                var yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                yield return new WaitForSeconds(0.3f);
                foreach (var side in new[] { 0f, -6f, 6f, -12f, 12f })
                {
                    for (var pitch = 10f; pitch <= 65f && !Named(); pitch += 2.5f)
                    {
                        rig.Look(yaw + side, pitch);
                        yield return Frames(3);
                    }
                }
            }
            var pointing = Named() && player.GetHoverObject() != null;
            var nameBefore = Hud.instance.m_hoverName != null ? Hud.instance.m_hoverName.text : "";
            c.Check(pointing && nameBefore.Length > 0, $"before the raise the crosshair names what you point at (\"{nameBefore.Replace("\n", " / ")}\")");
            bool Health() => Hud.instance.m_rootObject.activeInHierarchy && Hud.instance.m_healthPanel != null && Hud.instance.m_healthPanel.gameObject.activeInHierarchy;
            bool Hotbar() => Object.FindAnyObjectByType<HotkeyBar>() != null;
            bool Map() => Minimap.instance != null && Minimap.instance.m_smallRoot != null && Minimap.instance.m_smallRoot.activeInHierarchy;
            var hudBefore = Health() && Hotbar() && (Map() || Game.m_noMap);
            c.Check(hudBefore, $"before the raise: health {Health()}, hotbar {Hotbar()}, map {Map()}");
            var vanillaFov = cam.m_camera.fieldOfView;

            yield return RaiseUp(c, player, "view");
            yield return Frames(15);
            yield return new WaitForEndOfFrame();
            var dark = ScopeOverlay.TestDark;
            var blur = ScopeOverlay.TestBlur;
            var rect = ScopeOverlay.TestRect;
            c.Check(ScopeOverlay.Shown && Near(dark.x, 0.7f) && Near(dark.y, 1f), $"round view fully closed in, 70% of the screen height (size {F(dark.x)}, iris {F(dark.y)})");
            c.Check(Mathf.Abs(rect.height - Screen.height) <= 2f && Mathf.Abs(rect.width - Screen.width) <= 2f,
                $"drawn over the whole screen ({F(rect.width)} x {F(rect.height)} of {Screen.width} x {Screen.height})");
            c.Check(Near(dark.z, ScopeOverlay.DefaultEdgeDarkness), $"dark edge at its default strength ({F(dark.z)})");
            c.Check(ScopeOverlay.TestBlurOn && ScopeCapture.TestOn && Near(blur.x, 0.7f) && Near(blur.z, 1f),
                $"blur ring drawn around the same circle (on {ScopeOverlay.TestBlurOn}, frame copy {ScopeCapture.TestOn}, size {F(blur.x)})");
            c.Check(!Hud.instance.m_crosshair.enabled && ScopeHud.Hidden, "no crosshair");
            c.Check(player.GetHoverObject() == null && Hud.instance.m_hoverName.text == "", "no name of what you point at");
            c.Check(Health() && Hotbar() && (Map() || Game.m_noMap), $"health, hotbar and map still on screen ({Health()}, {Hotbar()}, {Map()})");
            c.Check(Near(ScopeCamera.CurrentMagnification, 4f, 0.01f) && Near(cam.m_camera.fieldOfView, ScopeCamera.ZoomedFov(vanillaFov, 4f), 0.05f),
                $"zoom x4 (fov {F(cam.m_camera.fieldOfView)})");

            // Canvas order: under the HUD, over the render-scale picture.
            var hudCanvas = RootCanvasOf(Hud.instance.m_rootObject);
            var scaler = Object.FindAnyObjectByType<FrameBufferScaler>(FindObjectsInactive.Include);
            var frameCanvas = RootCanvasOf(scaler != null ? scaler.gameObject : null);
            c.Note("overlay: " + ScopeOverlay.Placement);
            c.Check(hudCanvas != null && ScopeOverlay.SortingOrder < hudCanvas.sortingOrder,
                $"round view drawn under the HUD (order {ScopeOverlay.SortingOrder}, HUD {(hudCanvas != null ? hudCanvas.sortingOrder.ToString() : "none")})");
            if (frameCanvas != null && frameCanvas != hudCanvas)
            {
                c.Check(ScopeOverlay.SortingOrder > frameCanvas.sortingOrder,
                    $"and over the render-scale picture (order {ScopeOverlay.SortingOrder}, picture {frameCanvas.sortingOrder})");
            }
            else
            {
                c.Note("no separate render-scale canvas in this game: only the HUD order checked");
            }
            SelfTest.Screenshot(OverlayTest, "default");
            yield return null;
            yield return null;

            yield return LowerDown();
            yield return Frames(3);
            c.Check(NotBack(player).Length == 0 && Hud.instance.m_crosshair.enabled, "lowered: round view gone, crosshair back");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- spyglass.settings ----------

    private static IEnumerator RunSettings()
    {
        var c = new Checks(SettingsTest);
        if (!Ready(SettingsTest, out var player, out var cam))
        {
            yield break;
        }
        var rig = new SpyRig(player, SettingsTest);
        try
        {
            rig.Noon();
            var spy = Hold(rig, c);
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            // Defaults first, whatever player's settings say.
            ServerRules.TestRules = new SpyglassRules();
            ScopeOverlay.TestClearViewSize = ScopeOverlay.DefaultClearSize;
            ScopeOverlay.TestEdgeBlur = true;
            ScopeOverlay.TestEdgeDarkness = ScopeOverlay.DefaultEdgeDarkness;
            Scope.TestAimSensitivity = 1f;
            Scope.TestSetMagnification(4f);
            rig.Look(OpenYaw(player, out _), -1f);
            yield return new WaitForSeconds(0.5f);
            var vanillaFov = cam.m_camera.fieldOfView;
            yield return RaiseUp(c, player, "settings");
            yield return Frames(15);
            c.Check(Near(ScopeOverlay.TestDark.x, 0.7f) && Near(ScopeOverlay.TestDark.z, ScopeOverlay.DefaultEdgeDarkness) && ScopeOverlay.TestBlurOn,
                "default view first");

            // Each view setting, live: new numbers reach the rings AND Unity build their meshes again from them
            // (what is on screen), within two frames. Nothing changed = no new build (so a build mean the change).
            var darkBuilds = ScopeOverlay.TestDarkBuilds;
            var blurBuilds = ScopeOverlay.TestBlurBuilds;
            yield return Frames(4);
            var idleBuilds = (ScopeOverlay.TestDarkBuilds - darkBuilds) + (ScopeOverlay.TestBlurBuilds - blurBuilds);
            c.Note($"ring mesh builds over 4 frames with nothing changed: {idleBuilds}");
            darkBuilds = ScopeOverlay.TestDarkBuilds;
            blurBuilds = ScopeOverlay.TestBlurBuilds;
            ScopeOverlay.TestClearViewSize = 0.4f;
            yield return Frames(2);
            c.Check(Near(ScopeOverlay.TestDark.x, 0.4f) && Near(ScopeOverlay.TestBlur.x, 0.4f), $"ClearViewSize 0.4 shows on the next frame ({F(ScopeOverlay.TestDark.x)})");
            c.Check(ScopeOverlay.TestDarkBuilds > darkBuilds && ScopeOverlay.TestBlurBuilds > blurBuilds,
                $"both rings drawn again for it (dark ring built {ScopeOverlay.TestDarkBuilds - darkBuilds}x, blur ring {ScopeOverlay.TestBlurBuilds - blurBuilds}x)");
            darkBuilds = ScopeOverlay.TestDarkBuilds;
            blurBuilds = ScopeOverlay.TestBlurBuilds;
            ScopeOverlay.TestClearViewSize = 1f;
            yield return Frames(2);
            c.Check(Near(ScopeOverlay.TestDark.x, 1f) && Near(ScopeOverlay.TestBlur.x, 1f), $"ClearViewSize 1 ({F(ScopeOverlay.TestDark.x)})");
            c.Check(ScopeOverlay.TestDarkBuilds > darkBuilds && ScopeOverlay.TestBlurBuilds > blurBuilds,
                $"both rings drawn again for it (dark ring built {ScopeOverlay.TestDarkBuilds - darkBuilds}x, blur ring {ScopeOverlay.TestBlurBuilds - blurBuilds}x)");
            ScopeOverlay.TestClearViewSize = ScopeOverlay.DefaultClearSize;
            ScopeOverlay.TestEdgeBlur = false;
            yield return Frames(2);
            c.Check(!ScopeOverlay.TestBlurOn && ScopeOverlay.Shown && Near(ScopeOverlay.TestDark.z, ScopeOverlay.DefaultEdgeDarkness),
                "EdgeBlur off: no blur ring, the dark edge stays");
            SelfTest.Screenshot(SettingsTest, "no-blur");
            yield return null;
            yield return null;
            ScopeOverlay.TestEdgeBlur = true;
            yield return Frames(4);
            c.Check(ScopeOverlay.TestBlurOn && ScopeCapture.TestOn, "EdgeBlur on again");
            darkBuilds = ScopeOverlay.TestDarkBuilds;
            ScopeOverlay.TestEdgeDarkness = 0f;
            yield return Frames(2);
            c.Check(Near(ScopeOverlay.TestDark.z, 0f) && ScopeOverlay.TestDarkBuilds > darkBuilds,
                $"EdgeDarkness 0: no dark edge ({F(ScopeOverlay.TestDark.z)}), ring drawn again ({ScopeOverlay.TestDarkBuilds - darkBuilds}x)");
            darkBuilds = ScopeOverlay.TestDarkBuilds;
            ScopeOverlay.TestEdgeDarkness = 1f;
            yield return Frames(2);
            c.Check(Near(ScopeOverlay.TestDark.z, 1f) && ScopeOverlay.TestDarkBuilds > darkBuilds,
                $"EdgeDarkness 1: black edge ({F(ScopeOverlay.TestDark.z)}), ring drawn again ({ScopeOverlay.TestDarkBuilds - darkBuilds}x)");
            ScopeOverlay.TestEdgeDarkness = ScopeOverlay.DefaultEdgeDarkness;
            Scope.TestAimSensitivity = 0.5f;
            var yaw0 = player.m_lookYaw.eulerAngles.y;
            player.SetMouseLook(new Vector2(80f, 0f));
            var turned = Mathf.DeltaAngle(yaw0, player.m_lookYaw.eulerAngles.y);
            player.SetMouseLook(new Vector2(-80f, 0f));
            c.Check(Near(turned, 80f * 0.5f / 4f, 0.3f), $"AimSensitivity 0.5: 80 in turns {F(turned)} deg at x4");
            Scope.TestAimSensitivity = 1f;
            Scope.TestSetMagnification(8f);
            ServerRules.TestRules = new SpyglassRules { MaxMagnification = 3f };
            yield return Frames(12);
            yield return new WaitForEndOfFrame();
            c.Check(Near(Scope.Magnification, 3f, 0.001f) && Near(ScopeCamera.CurrentMagnification, 3f, 0.01f)
                    && Near(cam.m_camera.fieldOfView, ScopeCamera.ZoomedFov(vanillaFov, 3f), 0.05f),
                $"MaxMagnification 3: zoom capped at x3 (x{F(ScopeCamera.CurrentMagnification)})");
            ServerRules.TestRules = new SpyglassRules();
            yield return Frames(3);
            var dark = ScopeOverlay.TestDark;
            c.Check(Near(dark.x, 0.7f) && Near(dark.z, ScopeOverlay.DefaultEdgeDarkness) && ScopeOverlay.TestBlurOn, "settings put back: default view again");

            yield return LowerDown();
            c.Check(NotBack(player).Length == 0, "lowered at the end");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- spyglass.blur-off ----------

    // What EdgeBlur off does that player can see (no blur ring) and what a raise with it off cost (no frame copy).
    // The frame copy left running after a switch-off while looking has own test below.
    private static IEnumerator RunBlurOff()
    {
        var c = new Checks(BlurOffTest);
        if (!Ready(BlurOffTest, out var player, out _))
        {
            yield break;
        }
        var rig = new SpyRig(player, BlurOffTest);
        try
        {
            var spy = Hold(rig, c);
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            ScopeOverlay.TestEdgeBlur = true;
            yield return RaiseUp(c, player, "blur");
            yield return Frames(12);
            c.Check(ScopeOverlay.TestBlurOn && ScopeCapture.TestOn, "EdgeBlur on: blur ring drawn from a copy of the frame");
            ScopeOverlay.TestEdgeBlur = false;
            yield return Frames(4);
            c.Check(!ScopeOverlay.TestBlurOn && ScopeOverlay.Shown, "EdgeBlur off while looking: no blur ring, round view still shown");
            yield return LowerDown();
            c.Check(!ScopeCapture.TestOn, "lowered: the frame is no longer copied");
            // Raised with EdgeBlur already off: copy never start.
            yield return RaiseUp(c, player, "no blur");
            yield return Frames(8);
            c.Check(!ScopeOverlay.TestBlurOn && !ScopeCapture.TestOn && ScopeOverlay.Shown, "raised with EdgeBlur off: no blur ring and no frame copy");
            yield return LowerDown();
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- spyglass.bug.blur-off-capture ----------

    // Known to fail today, alone here so spyglass.blur-off pass: ScopeOverlay.Show stop drawing the blur ring but
    // leave ScopeCapture copying and shrinking every frame until spyglass lowered (ScopeCapture.Disable only called
    // from Hide). Nothing to see on screen: cost only.
    private static IEnumerator RunBlurOffBug()
    {
        var c = new Checks(BlurOffBugTest);
        if (!Ready(BlurOffBugTest, out var player, out _))
        {
            yield break;
        }
        var rig = new SpyRig(player, BlurOffBugTest);
        try
        {
            var spy = Hold(rig, c);
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            ScopeOverlay.TestEdgeBlur = true;
            yield return RaiseUp(c, player, "blur");
            yield return Frames(12);
            var copied = ScopeCapture.TestOn;
            ScopeOverlay.TestEdgeBlur = false;
            yield return Frames(4);
            c.Check(copied && !ScopeCapture.TestOn,
                $"EdgeBlur off while looking: the frame is no longer copied for the blur (copied before {copied}, still copied {ScopeCapture.TestOn}, blur ring drawn {ScopeOverlay.TestBlurOn})");
            yield return LowerDown();
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- spyglass.close ----------

    // Plain solid slab on piece layer (no game prefab: nothing to name, nothing saved in world).
    private static GameObject Slab(string name, Vector3 centre, Quaternion rotation, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.layer = LayerMask.NameToLayer("piece");
        go.transform.SetPositionAndRotation(centre, rotation);
        go.transform.localScale = size;
        return go;
    }

    // Spyglass up then down again, me watch every drawn frame.
    private static IEnumerator UpAndDownWatched(Player player, GameCamera cam, DrawWatch watch, bool[] done)
    {
        yield return WaitReal(() => Scope.CanRaise(player), 4f);
        Scope.TestRequestRaise();
        var t = Time.unscaledTime;
        var lowered = false;
        done[0] = false;
        while (Time.unscaledTime - t < 5f)
        {
            yield return new WaitForEndOfFrame();
            watch.Sample(player, cam, false);
            if (!lowered && Scope.State == ScopeState.Raised && Time.unscaledTime - t > 1f)
            {
                Scope.TestRequestLower();
                lowered = true;
            }
            else if (lowered && Scope.State == ScopeState.Idle)
            {
                done[0] = true;
                break;
            }
        }
    }

    private static IEnumerator RunClose()
    {
        var c = new Checks(CloseTest);
        if (!Ready(CloseTest, out var player, out var cam))
        {
            yield break;
        }
        var rig = new SpyRig(player, CloseTest);
        GameObject wall = null;
        GameObject roof = null;
        GameObject behind = null;
        try
        {
            rig.KeepPlace();
            rig.Noon();
            var spy = Hold(rig, c);
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            Scope.TestSetMagnification(4f);
            var yaw = player.transform.eulerAngles.y;
            var fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var feet = player.transform.position;
            var radius = player.m_collider != null ? player.m_collider.radius : 0.4f;
            var pieces = LayerMask.GetMask("piece");
            rig.Look(yaw, 0f);
            yield return new WaitForSeconds(0.5f);
            c.Note($"near plane before the raise {F(cam.m_camera.nearClipPlane)} (the game's minimum {F(cam.m_nearClipPlaneMin)}), body radius {F(radius)}");

            // Face first at a wall: its near side a hand's width from the body.
            wall = Slab("MC_SpyglassTestWall", feet + fwd * (radius + 0.25f) + Vector3.up * 1.5f, Quaternion.LookRotation(fwd), new Vector3(3f, 3f, 0.2f));
            yield return FixedSteps(3);
            yield return RaiseUp(c, player, "wall");
            yield return Frames(5);
            yield return new WaitForEndOfFrame();
            var near = cam.m_camera.nearClipPlane;
            var seen = Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 3f, pieces) && hit.collider != null && hit.collider.gameObject == wall;
            c.Check(near <= cam.m_nearClipPlaneMin + 0.0001f, $"near plane at the game's minimum while up ({F(near)})");
            c.Check(seen && hit.distance > near && hit.distance < 1f,
                $"the wall {(seen ? F(hit.distance) : "?")} m before the eye is behind the near plane: drawn, not cut away");
            yield return LowerDown();
            Object.Destroy(wall);
            wall = null;

            // A roof just over the head, looking up at it.
            var top = player.m_eye.position.y - feet.y + 0.3f;
            roof = Slab("MC_SpyglassTestRoof", feet + Vector3.up * (top + 0.1f), Quaternion.identity, new Vector3(3f, 0.2f, 3f));
            rig.Look(yaw, -60f);
            yield return FixedSteps(3);
            yield return RaiseUp(c, player, "roof");
            yield return Frames(5);
            yield return new WaitForEndOfFrame();
            near = cam.m_camera.nearClipPlane;
            seen = Physics.Raycast(cam.transform.position, cam.transform.forward, out hit, 3f, pieces) && hit.collider != null && hit.collider.gameObject == roof;
            c.Check(near <= cam.m_nearClipPlaneMin + 0.0001f && seen && hit.distance > near && hit.distance < 1.2f,
                $"the roof {(seen ? F(hit.distance) : "?")} m over the eye is drawn too (near plane {F(near)})");
            yield return LowerDown();
            Object.Destroy(roof);
            roof = null;

            // Looking up steeply (game camera come down to the feet): own body never drawn from inside.
            rig.Look(yaw, -70f);
            yield return new WaitForSeconds(0.7f);
            var watch = new DrawWatch();
            var done = new bool[1];
            yield return UpAndDownWatched(player, cam, watch, done);
            c.Check(done[0], "raised and lowered looking up steeply");
            c.Check(!watch.BodySeen && !watch.BodyGone,
                $"looking up: own body hidden on every frame the camera is in the head or within 2 m of the feet, shown on the others ({watch.Frames} frames; seen {watch.BodySeen}, wrongly hidden {watch.BodyGone})");
            yield return FixedSteps(3);
            c.Check(BodyFar(player) == !player.m_lodVisible, "afterwards the body follows the game's own rule again");

            // Back about 1 m from a wall.
            behind = Slab("MC_SpyglassTestBackWall", feet - fwd * 1.1f + Vector3.up * 1.5f, Quaternion.LookRotation(fwd), new Vector3(4f, 3f, 0.2f));
            rig.Look(yaw, 0f);
            yield return new WaitForSeconds(0.7f);
            var pulled = (cam.transform.position - player.m_eye.position).magnitude;
            watch = new DrawWatch();
            yield return UpAndDownWatched(player, cam, watch, done);
            c.Check(done[0], "raised and lowered with the back to a wall");
            c.Check(!watch.BodySeen && !watch.BodyGone,
                $"back to a wall (camera {F(pulled)} m from the eye before): same rule on every frame ({watch.Frames} frames; seen {watch.BodySeen}, wrongly hidden {watch.BodyGone})");
            yield return FixedSteps(3);
            c.Check(BodyFar(player) == !player.m_lodVisible, "afterwards the game's own rule again");
            c.Report();
        }
        finally
        {
            rig.Restore();
            foreach (var go in new[] { wall, roof, behind })
            {
                if (go != null)
                {
                    Object.Destroy(go);
                }
            }
        }
    }

    // ---------- fog: spyglass.no-horizons, spyglass.horizons, spyglass.rain ----------

    // Weather still on its way to another one, or forced weather (console "env") not the current one yet. Game blend
    // old weather into new one over EnvMan.m_transitionDuration: about 10 s in the run (fog 0.03 of Rain was at
    // 0.0083 after 8 s on the way to Clear), so test after a rain test must wait that long.
    private static bool WeatherBlending()
    {
        var man = EnvMan.instance;
        if (man == null)
        {
            return false;
        }
        if (man.m_nextEnv != null)
        {
            return true;
        }
        return !string.IsNullOrEmpty(man.m_debugEnv) && (man.m_currentEnv == null || man.m_currentEnv.m_name != man.m_debugEnv);
    }

    // Fog density once it stop changing (weather change, time of day set), read at end of frame: weather blend
    // over, then same value for 20 frames and 1 s of game time (a long frame alone never count as "stood still").
    private static IEnumerator FogSettled(float[] density, float timeout)
    {
        var end = Time.realtimeSinceStartup + timeout;
        var last = RenderSettings.fogDensity;
        var since = Time.time;
        var frames = 0;
        while (Time.realtimeSinceStartup < end)
        {
            yield return new WaitForEndOfFrame();
            var now = RenderSettings.fogDensity;
            if (WeatherBlending() || Mathf.Abs(now - last) > Mathf.Max(0.0000001f, last * 0.002f))
            {
                last = now;
                since = Time.time;
                frames = 0;
            }
            else if (++frames >= 20 && Time.time - since > 1f)
            {
                break;
            }
        }
        density[0] = RenderSettings.fogDensity;
    }

    private static string D(float density) => density.ToString("0.#######", System.Globalization.CultureInfo.InvariantCulture);

    private static IEnumerator RunNoHorizons()
    {
        var c = new Checks(NoHorizonsTest);
        if (!Ready(NoHorizonsTest, out var player, out var cam))
        {
            yield break;
        }
        var rig = new SpyRig(player, NoHorizonsTest);
        try
        {
            rig.Noon();
            rig.ClearWeather();
            var spy = Hold(rig, c);
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = new SpyglassRules();
            rig.Look(OpenYaw(player, out _), -1f);
            var fog = new float[1];
            yield return FogSettled(fog, 15f);
            var before = fog[0];
            c.Check(!WeatherBlending(), $"clear weather settled before the raise (fog density {D(before)})");
            var problems = _tap != null ? _tap.Problems().Count : 0;
            // Game without Distant Horizons (it may well be installed in this run: me say it is not).
            ScopeCamera.TestDistantHorizons = false;
            Scope.TestSetMagnification(8f);
            yield return RaiseUp(c, player, "no Distant Horizons");
            c.Check(!ScopeCamera.DistantHorizonsSeen, "raised as without Distant Horizons");
            var touched = false;
            var worst = 0f;
            for (var i = 0; i < 60; i++)
            {
                yield return new WaitForEndOfFrame();
                touched |= ScopeCamera.FogApplied;
                worst = Mathf.Max(worst, Mathf.Abs(RenderSettings.fogDensity - before));
            }
            c.Check(!touched && worst <= before * 0.02f + 0.0000001f,
                $"fog left as it is for 60 frames at x8 (density {D(RenderSettings.fogDensity)}, before {D(before)}, largest change {D(worst)})");
            c.Check(Near(ScopeCamera.CurrentMagnification, 8f, 0.01f), $"zoom x8 all the same (x{F(ScopeCamera.CurrentMagnification)})");
            SelfTest.Screenshot(NoHorizonsTest, "x8");
            yield return null;
            yield return null;
            yield return LowerDown();
            var after = _tap != null ? _tap.Problems() : new List<string>();
            c.Check(_tap != null && after.Count == problems, "no warning or error from the spyglass meanwhile" + (after.Count > problems ? ": " + after[after.Count - 1] : ""));
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // Own test: need Distant Horizons running in this game.
    private static IEnumerator RunHorizons()
    {
        var c = new Checks(HorizonsTest);
        if (!Ready(HorizonsTest, out var player, out var cam))
        {
            yield break;
        }
        var rig = new SpyRig(player, HorizonsTest);
        try
        {
            if (!c.Check(OtherModActive(ScopeCamera.DistantHorizonsGuid), "Distant Horizons is active in this run (not: nothing tested)"))
            {
                c.Report();
                yield break;
            }
            rig.Noon();
            rig.ClearWeather();
            var spy = Hold(rig, c);
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = new SpyglassRules();
            rig.Look(OpenYaw(player, out _), -1f);
            var fog = new float[1];
            yield return FogSettled(fog, 15f);
            var before = fog[0];
            var weather = 1f - Mathf.Clamp01((before - 0.006f) / (0.02f - 0.006f));
            c.Check(weather > 0.5f, $"clear weather: fog density {D(before)} (clearing share {F(weather)})");
            Scope.TestSetMagnification(8f);
            yield return RaiseUp(c, player, "Distant Horizons");
            yield return Frames(10);
            yield return new WaitForEndOfFrame();
            var rules = ServerRules.Current;
            var want = before * (1f - rules.FogClearing * weather);
            var now = RenderSettings.fogDensity;
            c.Check(ScopeCamera.DistantHorizonsSeen && ScopeCamera.FogApplied, "Distant Horizons seen, fog clearing on");
            c.Check(now < before * 0.999f && Mathf.Abs(now - want) <= want * 0.05f + 0.0000001f,
                $"less haze: density {D(now)}, before {D(before)}, expected {D(want)} (FogClearing {F(rules.FogClearing)})");
            var slot = ViewBoost.Peek();
            var f = cam.transform.forward;
            c.Check(slot != null && Time.frameCount - slot[1] <= 1 && Math.Abs(slot[2] - 8) < 0.01 && slot[3] > 0.99,
                "Distant Horizons told every frame: zoom x8, full weight");
            c.Check(slot != null && f.x * slot[4] + f.y * slot[5] + f.z * slot[6] > 0.9999 && Math.Abs(slot[8] - cam.m_camera.fieldOfView * 0.5) < 0.05,
                "with where the spyglass looks and how wide");
            // Turning: message follow.
            player.SetMouseLook(new Vector2(400f, 0f));
            yield return Frames(3);
            yield return new WaitForEndOfFrame();
            slot = ViewBoost.Peek();
            var f2 = cam.transform.forward;
            c.Check(Vector3.Angle(f, f2) > 20f && slot != null && f2.x * slot[4] + f2.y * slot[5] + f2.z * slot[6] > 0.9999,
                $"turned {F(Vector3.Angle(f, f2))} deg: the new direction is told");
            SelfTest.Screenshot(HorizonsTest, "x8");
            yield return null;
            yield return null;

            yield return LowerDown();
            yield return FogSettled(fog, 4f);
            slot = ViewBoost.Peek();
            c.Check(!ScopeCamera.FogApplied && Mathf.Abs(fog[0] - before) <= before * 0.03f + 0.0000001f,
                $"lowered: haze back (density {D(fog[0])}, before {D(before)})");
            c.Check(slot != null && slot[3] <= 0.0, "lowered: Distant Horizons told to drop the extra detail");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // Own test: need Distant Horizons running, and a rain weather in game's list.
    private static IEnumerator RunRain()
    {
        var c = new Checks(RainTest);
        if (!Ready(RainTest, out var player, out _))
        {
            yield break;
        }
        var rig = new SpyRig(player, RainTest);
        try
        {
            if (!c.Check(OtherModActive(ScopeCamera.DistantHorizonsGuid), "Distant Horizons is active in this run (not: nothing tested)"))
            {
                c.Report();
                yield break;
            }
            EnvSetup rain = null;
            foreach (var env in EnvMan.instance.m_environments)
            {
                if (env != null && env.m_name == "Rain")
                {
                    rain = env;
                }
            }
            if (!c.Check(rain != null, "the game has a weather called Rain"))
            {
                c.Report();
                yield break;
            }
            c.Note($"Rain: fog density by day {D(rain.m_fogDensityDay)}");
            var wet = player.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectWet);
            rig.OnRestore("wet", () =>
            {
                if (!wet)
                {
                    player.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectWet, true);
                }
            });
            rig.Noon();
            rig.Weather("Rain");
            var spy = Hold(rig, c);
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = new SpyglassRules();
            rig.Look(OpenYaw(player, out _), -1f);
            var fog = new float[1];
            yield return FogSettled(fog, 15f);
            var before = fog[0];
            // The rain really is here (else "fog kept" would say nothing about rain).
            var weather = EnvMan.instance.GetCurrentEnvironment();
            c.Check(weather != null && weather.m_name == "Rain" && before > 0f,
                $"it rains when the spyglass goes up (weather {(weather != null ? weather.m_name : "none")}, fog density {D(before)})");
            Scope.TestSetMagnification(4f);
            yield return RaiseUp(c, player, "rain");
            c.Check(ScopeCamera.DistantHorizonsSeen, "Distant Horizons seen");
            var lowest = float.MaxValue;
            var up = Scope.State == ScopeState.Raised;
            for (var i = 0; i < 40; i++)
            {
                yield return new WaitForEndOfFrame();
                lowest = Mathf.Min(lowest, RenderSettings.fogDensity);
                up &= Scope.State == ScopeState.Raised;
            }
            // Spyglass down = fog kept anyway: the 40 frames only count with it up all the way.
            c.Check(up, $"spyglass up for all 40 frames in the rain ({Scope.State})");
            c.Check(lowest >= before * 0.97f, $"rain keeps its fog (density {D(lowest)} at the lowest, {D(before)} before the raise)");
            c.Note($"fog density {D(before)} in the rain before the raise, {D(lowest)} at the lowest with the spyglass up");
            SelfTest.Screenshot(RainTest, "rain-x4");
            yield return null;
            yield return null;
            yield return LowerDown();
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- spyglass.toggle ----------

    // What feature must give back the moment it turn off (same frame), "" = all of it.
    private static string OffNow(Player player)
    {
        var bad = NotBack(player);
        var more = new List<string>();
        if (bad.Length > 0)
        {
            more.Add(bad);
        }
        if (ScopeOverlay.TestBuilt)
        {
            more.Add("round view object still there");
        }
        if (ScopeCapture.TestExists)
        {
            more.Add("frame copy still on the camera");
        }
        if (ScopeHud.Hidden || !Hud.instance.m_crosshair.enabled)
        {
            more.Add("crosshair still off");
        }
        if (BodyFar(player) == player.m_lodVisible)
        {
            more.Add("body not as the game wants it");
        }
        return string.Join(", ", more.ToArray());
    }

    // Feature off: clicks with spyglass in hand do nothing (no raise, no punch).
    private static IEnumerator DeadClicks(Checks c, Player player)
    {
        yield return Click(player);
        yield return FixedSteps(3);
        var left = Scope.State == ScopeState.Idle && !player.InAttack() && player.m_queuedAttackTimer <= 0f;
        yield return new WaitForFixedUpdate();
        Controls(player, secondary: true, secondaryHold: true);
        yield return FixedSteps(2);
        Controls(player);
        yield return FixedSteps(3);
        var middle = Scope.State == ScopeState.Idle && !player.InAttack() && player.m_queuedSecondAttackTimer <= 0f;
        yield return new WaitForSeconds(0.4f);
        c.Check(left && Scope.State == ScopeState.Idle && !ScopeOverlay.Shown, $"off: left click does nothing ({Scope.State})");
        c.Check(middle && !player.InAttack(), "off: middle click does nothing, no punch");
    }

    // Copy of inventory saved and loaded again (what game do with a character) still hold a spyglass.
    private static bool SurvivesLoad(Inventory inv)
    {
        var pkg = new ZPackage();
        inv.Save(pkg);
        pkg.SetPos(0);
        var copy = new Inventory("MC_SpyglassTest", null, inv.GetWidth(), inv.GetHeight());
        copy.Load(pkg);
        return CountSpyglasses(copy) == CountSpyglasses(inv) && CountSpyglasses(copy) > 0;
    }

    private static IEnumerator RunToggle()
    {
        var c = new Checks(ToggleTest);
        if (!Ready(ToggleTest, out var player, out var cam))
        {
            yield break;
        }
        var rig = new SpyRig(player, ToggleTest);
        try
        {
            rig.Noon();
            rig.ClearWeather();
            var spy = Hold(rig, c);
            var recipe = SpyglassContent.CraftRecipe;
            if (spy == null || !c.Check(recipe != null && Plugin.FeatureActive, "feature on, recipe registered"))
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = new SpyglassRules();
            SpyglassContent.Rebuild();
            c.Check(recipe.m_enabled, "recipe shown while the feature is on");
            // Forge and a player who know the recipe: me look at craft list with feature off and on.
            KeepKnown(rig);
            player.m_knownRecipes.Add(recipe.m_item.m_itemData.m_shared.m_name);
            var station = SpawnStation(c, rig);
            var row = new int[1];
            rig.Look(OpenYaw(player, out _), -1f);
            Scope.TestSetMagnification(4f);
            // Test before this one may leave rain: wait out the whole weather blend (8 s was too short, the value
            // "before" was taken half way and fog was thinner afterwards).
            var fog = new float[1];
            yield return FogSettled(fog, 15f);
            var fogBefore = fog[0];
            c.Check(!WeatherBlending(), $"clear weather settled before the raise (fog density {D(fogBefore)})");
            var vanillaFov = cam.m_camera.fieldOfView;
            yield return RaiseUp(c, player, "toggle");
            yield return Frames(10);
            // What is drawn (game write fog fresh on physics ticks, spyglass take its share after).
            yield return new WaitForEndOfFrame();
            var fogUp = RenderSettings.fogDensity;
            // Fog really cleared while looking (Distant Horizons runs), else "fog as before" below say little.
            if (ScopeCamera.FogApplied)
            {
                c.Check(fogUp < fogBefore * 0.9f, $"looking: fog cleared before the turn-off (density {D(fogUp)}, {D(fogBefore)} before the raise)");
            }
            else
            {
                c.Note("fog not cleared while looking (Distant Horizons not active): \"fog as before\" only says nothing was left changed");
            }
            // Turn-off below in the frame's update part, like a click on the tick box.
            yield return null;

            // Off while looking. Whatever happen, feature go on again at the end.
            rig.OnRestore("feature on", () =>
            {
                if (!Plugin.FeatureActive)
                {
                    Plugin.TestLiveToggle(true);
                }
            });
            // Stamina use count in this part (the run has it switched off).
            var staminaKey = ZoneSystem.instance.GetGlobalKey(GlobalKeys.StaminaRate);
            if (staminaKey)
            {
                rig.OnRestore("stamina rate", () => ZoneSystem.instance.SetGlobalKey(GlobalKeys.StaminaRate, 0f));
                ZoneSystem.instance.RemoveGlobalKey(GlobalKeys.StaminaRate);
            }
            c.Check(Plugin.TestLiveToggle(false) && !Plugin.FeatureActive, "feature turned off while looking");
            var left = OffNow(player);
            c.Check(left.Length == 0, "off: everything back in the same frame" + (left.Length == 0 ? "" : ": " + left));
            c.Check(player.GetRightItem() == spy, "off: the spyglass stays in the hand");
            c.Check(!recipe.m_enabled, "off: recipe hidden");
            yield return Frames(3);
            yield return new WaitForEndOfFrame();
            var gap = (cam.transform.position - player.m_eye.position).magnitude;
            c.Check(Near(cam.m_camera.fieldOfView, vanillaFov, 0.01f) && gap > 0.5f, $"off: camera and zoom normal (fov {F(cam.m_camera.fieldOfView)}, {F(gap)} m from the eye)");
            yield return FogSettled(fog, 4f);
            c.Check(!ScopeCamera.FogApplied && Mathf.Abs(fog[0] - fogBefore) <= fogBefore * 0.05f + 0.0000001f,
                $"off: fog as before the raise (density {D(fog[0])}, before {D(fogBefore)}, {D(fogUp)} while looking)");
            yield return new WaitForSeconds(0.5f);
            c.Note($"stamina rate {F(Game.m_staminaRate)} for the click part");
            var stamina = player.GetStamina();
            yield return DeadClicks(c, player);
            c.Check(player.GetStamina() >= stamina - 0.01f, $"off: no stamina used (stamina {F(player.GetStamina())}, was {F(stamina)})");
            c.Check(SurvivesLoad(player.GetInventory()) && ObjectDB.instance.GetItemPrefab(SpyglassContent.ItemName) != null
                    && ZNetScene.instance.GetPrefab(SpyglassContent.ItemHash) != null,
                "off: the spyglass is still a known item, a saved inventory loads it back");
            if (station != null)
            {
                yield return OpenStation(player, station, row);
                c.Check(row[0] < 0, "off: the spyglass is gone from the Forge's craft list");
                yield return CloseStation(player);
                // A crafting station make the game put hand items away: back in the hand.
                player.EquipItem(spy, false);
            }

            // On again.
            c.Check(Plugin.TestLiveToggle(true) && Plugin.FeatureActive, "feature turned on again");
            ServerRules.TestRules = new SpyglassRules();
            SpyglassContent.Rebuild();
            c.Check(recipe.m_enabled, "on: recipe back");
            if (station != null)
            {
                yield return OpenStation(player, station, row);
                c.Check(row[0] >= 0, "on: the spyglass is in the Forge's craft list again");
                yield return CloseStation(player);
                player.EquipItem(spy, false);
            }
            c.Check(player.GetRightItem() == spy, "spyglass in the right hand for the click");
            yield return WaitReal(() => Scope.CanRaise(player), 4f);
            yield return Click(player);
            yield return WaitReal(() => Scope.State == ScopeState.Raised, 3f);
            yield return Frames(5);
            yield return new WaitForEndOfFrame();
            c.Check(Scope.State == ScopeState.Raised && ScopeOverlay.Shown && !Hud.instance.m_crosshair.enabled
                    && Near(cam.m_camera.fieldOfView, ScopeCamera.ZoomedFov(vanillaFov, 4f), 0.05f),
                $"on: a click raises it again, round view and zoom back ({Scope.State})");
            yield return LowerDown();
            c.Check(NotBack(player).Length == 0, "lowered at the end");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- spyglass.compat.swim ----------

    private static bool FindWater(Vector3 origin, out Vector3 spot)
    {
        var gen = WorldGenerator.instance;
        var level = ZoneSystem.instance.m_waterLevel;
        for (var r = 100f; r <= 5000f; r += 50f)
        {
            for (var a = 0; a < 360; a += 10)
            {
                var x = origin.x + Mathf.Sin(a * Mathf.Deg2Rad) * r;
                var z = origin.z + Mathf.Cos(a * Mathf.Deg2Rad) * r;
                if (x * x + z * z > 9500f * 9500f || gen.GetHeight(x, z) > level - 8f)
                {
                    continue;
                }
                var biome = gen.GetBiome(x, z);
                if (biome == Heightmap.Biome.AshLands || biome == Heightmap.Biome.DeepNorth)
                {
                    continue;
                }
                if (gen.GetHeight(x + 12f, z) > level - 5f || gen.GetHeight(x - 12f, z) > level - 5f
                    || gen.GetHeight(x, z + 12f) > level - 5f || gen.GetHeight(x, z - 12f) > level - 5f)
                {
                    continue;
                }
                spot = new Vector3(x, level, z);
                return true;
            }
        }
        spot = Vector3.zero;
        return false;
    }

    private static IEnumerator RunSwim()
    {
        var c = new Checks(SwimTest);
        if (!Ready(SwimTest, out var player, out var cam))
        {
            yield break;
        }
        if (WorldGenerator.instance == null || ZoneSystem.instance == null)
        {
            SelfTest.Fail(SwimTest, "no world generator");
            yield break;
        }
        var rig = new SpyRig(player, SwimTest);
        try
        {
            if (!c.Check(FindWater(rig.Origin, out var water), "deep water within 5 km of the spawn"))
            {
                c.Report();
                yield break;
            }
            c.Note($"deep water {F(Vector3.Distance(water, rig.Origin))} m away; Swim Dive {(OtherModActive("MC.Exploration.Swimming.Dive") ? "active" : "not active")}");
            var spy = Hold(rig, c);
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            // The swim leave the player wet: dry again at the end when it was dry before.
            var wet = player.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectWet);
            rig.OnRestore("wet", () =>
            {
                if (!wet)
                {
                    player.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectWet, true);
                }
            });
            Scope.TestSetMagnification(4f);
            // Feet locked while looking, so nobody walk into water with it up: player drop into it from 35 m
            // (teleport end in the air above the water).
            yield return rig.Travel(water + Vector3.up * 35f, Quaternion.identity, c, 40f);
            rig.TakeControls();
            Scope.TestRequestRaise();
            yield return WaitReal(() => Scope.State == ScopeState.Raised || player.IsSwimming(), 2f);
            if (!c.Check(Scope.State == ScopeState.Raised && !player.IsSwimming(),
                    $"spyglass up while falling toward the water ({Scope.State}, {F(player.transform.position.y - water.y)} m above it)"))
            {
                yield return rig.GoHome(c);
                c.Report();
                yield break;
            }
            // Fall: frame by frame until swimming starts.
            var t0 = Time.realtimeSinceStartup;
            while (!player.IsSwimming() && Time.realtimeSinceStartup - t0 < 10f)
            {
                yield return null;
            }
            var swimming = player.IsSwimming();
            var stateThen = Scope.State;
            yield return null;
            yield return null;
            c.Check(swimming, "swimming started");
            c.Check(Scope.State == ScopeState.Idle && !ScopeOverlay.Shown && !Flag(player),
                $"swimming: view back at once ({stateThen} on the first swimming frame, {Scope.State} two frames later)");
            yield return Frames(3);
            yield return new WaitForEndOfFrame();
            c.Check(NotBack(player).Length == 0 && Near(cam.m_camera.fieldOfView, cam.m_fov, 0.01f) && Hud.instance.m_crosshair.enabled,
                "swimming: zoom, body, crosshair normal" + (NotBack(player).Length == 0 ? "" : " (" + NotBack(player) + ")"));
            yield return WaitReal(() => player.GetRightItem() == null, 3f);
            c.Check(player.GetRightItem() == null && player.m_hiddenRightItem == spy, "the game put the spyglass away for swimming");
            // T10: clicking while swimming does nothing.
            Scope.TestRequestRaise();
            yield return Click(player);
            yield return Frames(3);
            c.Check(Scope.State == ScopeState.Idle && !ScopeOverlay.Shown, $"swimming: a click raises nothing ({Scope.State})");
            yield return rig.GoHome(c);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- other MC mods ----------

    // Own test: need Dual Wielding active.
    private static IEnumerator RunDualWield()
    {
        var c = new Checks(DualTest);
        if (!Ready(DualTest, out var player, out _))
        {
            yield break;
        }
        var rig = new SpyRig(player, DualTest);
        try
        {
            if (!c.Check(OtherModActive(DualWieldGuid), "Dual Wielding is active in this run (not: nothing tested)"))
            {
                c.Report();
                yield break;
            }
            rig.TakeControls();
            var spy = rig.Give(SpyglassContent.ItemName);
            var sword = rig.Give("SwordIron");
            var axe = rig.Give("AxeBronze");
            if (!c.Check(spy != null && sword != null && axe != null, "spyglass, SwordIron and AxeBronze given"))
            {
                c.Report();
                yield break;
            }
            var errors = _tap != null ? _tap.ErrorMark : 0;
            player.UnequipItem(player.GetRightItem(), false);
            player.UnequipItem(player.GetLeftItem(), false);
            player.EquipItem(sword, false);
            player.EquipItem(axe, false);
            yield return Frames(3);
            if (c.Check(player.GetRightItem() == sword && player.GetLeftItem() == axe, "a pair in hand (sword right, axe left)"))
            {
                player.EquipItem(spy, false);
                yield return Frames(3);
                c.Check(player.GetRightItem() == spy && player.GetLeftItem() == null && !player.IsItemEquiped(sword) && !player.IsItemEquiped(axe),
                    "spyglass equipped: both weapons put away, both hands taken");
                // Never paired: weapon equipped next take its place, it never go to the off hand.
                player.EquipItem(axe, false);
                yield return Frames(3);
                c.Check(player.GetRightItem() == axe && player.GetLeftItem() == null && !player.IsItemEquiped(spy),
                    "a weapon equipped with the spyglass in hand replaces it (no pair with the spyglass)");
                player.EquipItem(spy, false);
                yield return Frames(3);
                c.Check(player.GetRightItem() == spy && player.GetLeftItem() == null && !player.IsItemEquiped(axe),
                    "the spyglass equipped with one weapon in hand replaces it (never in the off hand)");
                yield return RaiseUp(c, player, "after the pair");
                yield return LowerDown();
            }
            var logged = _tap != null ? _tap.ErrorsSince(errors) : new List<string>();
            c.Check(_tap != null && logged.Count == 0, "no error logged meanwhile" + (logged.Count == 0 ? "" : ": " + First(logged)));
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // Own test: need Tower Shield Wall active.
    private static IEnumerator RunTowerShield()
    {
        var c = new Checks(TowerTest);
        if (!Ready(TowerTest, out var player, out _))
        {
            yield break;
        }
        var rig = new SpyRig(player, TowerTest);
        try
        {
            if (!c.Check(OtherModActive(TowerWallGuid), "Tower Shield Wall is active in this run (not: nothing tested)"))
            {
                c.Report();
                yield break;
            }
            rig.TakeControls();
            var spy = rig.Give(SpyglassContent.ItemName);
            var tower = rig.Give("ShieldWoodTower");
            if (!c.Check(spy != null && tower != null, "spyglass and ShieldWoodTower given"))
            {
                c.Report();
                yield break;
            }
            var errors = _tap != null ? _tap.ErrorMark : 0;
            player.UnequipItem(player.GetRightItem(), false);
            player.UnequipItem(player.GetLeftItem(), false);
            player.EquipItem(tower, false);
            yield return Frames(3);
            if (c.Check(player.IsItemEquiped(tower), "tower shield in hand"))
            {
                player.EquipItem(spy, false);
                yield return Frames(3);
                c.Check(player.GetRightItem() == spy && player.GetLeftItem() == null && !player.IsItemEquiped(tower),
                    "spyglass equipped: the tower shield is put away");
                yield return RaiseUp(c, player, "after the tower shield");
                yield return LowerDown();
                player.EquipItem(tower, false);
                yield return Frames(3);
                c.Check(player.IsItemEquiped(tower) && !player.IsItemEquiped(spy), "tower shield equipped: the spyglass is put away");
            }
            var logged = _tap != null ? _tap.ErrorsSince(errors) : new List<string>();
            c.Check(_tap != null && logged.Count == 0, "no error logged meanwhile" + (logged.Count == 0 ? "" : ": " + First(logged)));
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    private static IEnumerator RunSort()
    {
        var c = new Checks(SortTest);
        var db = ObjectDB.instance;
        if (db == null || !SpyglassContent.Built)
        {
            SelfTest.Fail(SortTest, "no item database or spyglass item");
            yield break;
        }
        ItemDrop.ItemData.SharedData Shared(string prefab)
        {
            var go = db.GetItemPrefab(prefab);
            var drop = go != null ? go.GetComponent<ItemDrop>() : null;
            return drop != null ? drop.m_itemData.m_shared : null;
        }
        var spy = ItemKinds.Classify(Shared(SpyglassContent.ItemName));
        c.Check(Shared("Hammer") != null && Shared("SwordIron") != null, "Hammer and SwordIron exist");
        c.Check(spy == ItemKind.Tool, $"the spyglass is of the kind the sort mods call a tool ({spy})");
        c.Check(spy == ItemKinds.Classify(Shared("Hammer")), "same kind as the hammer");
        if (Shared("Hoe") != null)
        {
            c.Check(spy == ItemKinds.Classify(Shared("Hoe")), "same kind as the hoe");
        }
        else
        {
            c.Note("no item called Hoe in this game: compared with the hammer only");
        }
        c.Check(spy != ItemKinds.Classify(Shared("SwordIron")), "not the kind of a sword");
        c.Note($"Sort Chest {(OtherModActive("MC.UX.Container.Sort") ? "active" : "not active")}, Crafting Search and Sort "
               + $"{(OtherModActive("MC.UX.Crafting.SearchSort") ? "active" : "not active")}: both group items by this kind");
        c.Report();
    }

    // ---------- spyglass.bug.missing-knife-error ----------

    // The one error line with own test: "Flint Knife is missing from the item database". (The same error about the
    // network prefab list is not this one: spyglass.log keep counting it.)
    private static bool IsMenuKnifeError(string line) =>
        line.Contains($"({SpyglassContent.VanillaKnifeName}) is missing from the item database");

    // Alone here so spyglass.item and spyglass.log pass. Main menu with a mod that fill its own item database there
    // (not empty, no vanilla items; Jotunn do): SpyglassContent.RegisterInObjectDB only skip an EMPTY database, so it
    // look for the knife, find none and log the error, although the item is built fine once the real database is here.
    private static IEnumerator RunKnifeBug()
    {
        var c = new Checks(KnifeBugTest);
        if (_tap == null || ObjectDB.instance == null)
        {
            SelfTest.Fail(KnifeBugTest, "the log was not listened to, or no item database");
            yield break;
        }
        var lines = _tap.Problems().FindAll(IsMenuKnifeError);
        var knifeHere = ObjectDB.instance.GetItemPrefab(SpyglassContent.VanillaKnifeName) != null;
        c.Check(!SpyglassContent.MissingReported && lines.Count == 0,
            $"no \"Flint Knife is missing\" error (knife in the item database now {knifeHere}, spyglass built {SpyglassContent.Built}, "
            + $"error reported {SpyglassContent.MissingReported}, {lines.Count} such line(s) logged{(lines.Count > 0 ? ": " + lines[0] : "")})");
        c.Report();
    }

    // ---------- spyglass.log ----------

    // Own test, last of the spyglass ones: no warning or error from this mod since it start (the false missing-knife
    // error of the main menu left out: spyglass.bug.missing-knife-error has it).
    private static IEnumerator RunLog()
    {
        var c = new Checks(LogTest);
        if (_tap == null)
        {
            SelfTest.Fail(LogTest, "the log was not listened to");
            yield break;
        }
        var problems = _tap.Problems();
        var knife = problems.RemoveAll(IsMenuKnifeError);
        if (knife > 0)
        {
            c.Note($"{knife} \"Flint Knife is missing from the item database\" error line(s) left out (own test: {KnifeBugTest})");
        }
        c.Check(problems.Count == 0, problems.Count == 0
            ? "no warning or error from the spyglass since it started"
            : $"{problems.Count} warning or error line(s) from the spyglass, first: {First(problems)}");
        c.Report();
    }
}
#endif
