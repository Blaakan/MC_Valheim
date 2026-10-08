#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using MC.Farming.BreedingStarInheritanceMod.Patches;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Farming.BreedingStarInheritanceMod;

// Debug build only. Self tests with other MC mods (single player). Each needs the other mod loaded and active, and
// fails with a clear line when it is not (a skip would look like a checked item). One small test per mod: trouble
// with one never blocks the rest.
//   breeding.cross.sortchest   Sort Chest with MergeStacks: egg stacks of different quality never merge, dropped eggs
//                              keep their hover (Sort Chest driven through its own sort button code, by reflection)
//   breeding.cross.tamecounts  Creature Kill and Tame Counts: birth, hatching, growing add no tame; after a respawn
//                              both mods' spawn patches worked, counts same, a real tame still counts
//   breeding.cross.harpoon     Harpoon Hooks Tames: partner hooked after the conception (no hurt) and moved away, the
//                              baby still uses the recorded partner
internal static partial class SelfTests
{
    private const string SortChestName = "breeding.cross.sortchest";
    private const string TameCountsName = "breeding.cross.tamecounts";
    private const string HarpoonName = "breeding.cross.harpoon";

    private const string SortChestGuid = "MC.UX.Container.Sort";
    private const string StatsGuid = "MC.Exploration.Stats.PerCreature";
    private const string HarpoonGuid = "MC.Farming.Harpoon.HooksTames";

    // Where Creature Kill and Tame Counts keeps its data on the character (its CounterStore).
    private const string StatsTamesKey = StatsGuid + ".Tames";
    private const string StatsSinceKey = StatsGuid + ".CountingSince";

    private static void RegisterCross()
    {
        SelfTest.Register(SortChestName, RunSortChest);
        SelfTest.Register(TameCountsName, RunTameCounts);
        SelfTest.Register(HarpoonName, RunHarpoon);
    }

    private static void UnregisterCross()
    {
        SelfTest.Unregister(SortChestName);
        SelfTest.Unregister(TameCountsName);
        SelfTest.Unregister(HarpoonName);
    }

    private static Vector3 FlatForward(Player player)
    {
        var forward = player.transform.forward;
        forward.y = 0f;
        return forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward;
    }

    // ---------- breeding.cross.sortchest ----------

    private static IEnumerator RunSortChest()
    {
        var c = new Checks(SortChestName);
        var player = Player.m_localPlayer;
        var scene = ZNetScene.instance;
        var gui = InventoryGui.instance;
        if (player == null || scene == null || gui == null || ObjectDB.instance == null)
        {
            SelfTest.Fail(SortChestName, "no local player, world or inventory window");
            yield break;
        }
        if (!OtherActive(SortChestGuid, out var sortName))
        {
            SelfTest.Fail(SortChestName, $"{sortName} is not loaded or not active: this test needs it");
            yield break;
        }
        // Sort Chest keeps its sort internal: me call the method its Sort button calls.
        MethodInfo sort = null;
        ConfigEntry<bool> merge = null;
        if (Chainloader.PluginInfos.TryGetValue(SortChestGuid, out var info) && info != null && info.Instance != null)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var assembly = info.Instance.GetType().Assembly;
            var sorter = assembly.GetType("MC.UX.ContainerSortMod.ContainerSorter");
            sort = sorter != null ? sorter.GetMethod("TrySortOpenContainer", flags) : null;
            var plugin = assembly.GetType("MC.UX.ContainerSortMod.Plugin");
            var field = plugin != null ? plugin.GetField("MergeStacks", flags) : null;
            merge = field != null ? field.GetValue(null) as ConfigEntry<bool> : null;
        }
        if (sort == null || merge == null)
        {
            SelfTest.Fail(SortChestName, $"the sort method or the MergeStacks setting of {sortName} was not found (renamed?): this test "
                                         + "calls ContainerSorter.TrySortOpenContainer and reads Plugin.MergeStacks");
            yield break;
        }
        if (!merge.Value)
        {
            SelfTest.Fail(SortChestName, $"MergeStacks is off in this game's {sortName} settings: turn it on to check this item "
                                         + "(a test never changes another mod's settings)");
            yield break;
        }
        var chestPrefab = scene.GetPrefab("piece_chest_wood");
        var eggPrefab = ObjectDB.instance.GetItemPrefab("ChickenEgg");
        if (chestPrefab == null || chestPrefab.GetComponent<Container>() == null || eggPrefab == null || eggPrefab.GetComponent<ItemDrop>() == null)
        {
            SelfTest.Fail(SortChestName, "prefab piece_chest_wood (a container) or item ChickenEgg not found");
            yield break;
        }

        // Test before may have respawned the character: while it stand up the game shut every inventory window.
        var free = new Box();
        yield return WaitPlayerFree(free, 30f);
        if (!free.Ok || !ReferenceEquals(Player.m_localPlayer, player))
        {
            SelfTest.Fail(SortChestName, free.Ok ? "the character changed while the test waited for it" : $"{free.Detail}: a chest cannot be opened");
            yield break;
        }

        GameObject chest = null;
        var drops = new List<GameObject>();
        try
        {
            var forward = FlatForward(player);
            chest = Object.Instantiate(chestPrefab, Ground(player.transform.position + forward * 2f), Quaternion.LookRotation(-forward));
            var wear = chest.GetComponent<WearNTear>();
            if (wear != null)
            {
                wear.enabled = false; // no support check: never break and spill
            }
            yield return null;
            yield return null;
            var container = chest.GetComponent<Container>();
            var inventory = container != null ? container.GetInventory() : null;
            if (inventory == null || inventory.GetWidth() < 4)
            {
                SelfTest.Fail(SortChestName, "the test chest has no inventory of at least 4 slots in a row");
                yield break;
            }
            // Part stacks in separate slots (as after splitting by hand): quality 1 x3, quality 2 x3, quality 1 x2,
            // quality 2 x2. Put straight in the list: the game's own add would already join same-quality stacks.
            var template = eggPrefab.GetComponent<ItemDrop>().m_itemData;
            var eggName = template.m_shared.m_name;
            var layout = new[] { new Vector2i(1, 3), new Vector2i(2, 3), new Vector2i(1, 2), new Vector2i(2, 2) };
            for (var i = 0; i < layout.Length; i++)
            {
                var item = template.Clone();
                item.m_dropPrefab = eggPrefab;
                item.m_quality = layout[i].x;
                item.m_stack = layout[i].y;
                item.m_gridPos = new Vector2i(i, 0);
                inventory.m_inventory.Add(item);
            }
            inventory.Changed();
            gui.Show(container);
            var until = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
            if (!c.Check(ReferenceEquals(gui.m_currentContainer, container) && container.IsOwner() && InventoryGui.IsVisible(),
                    "the test chest must be the open container, owned by this game"))
            {
                c.Report("");
                yield break;
            }

            sort.Invoke(null, null);
            yield return null;
            var stacks = new List<ItemDrop.ItemData>();
            foreach (var item in inventory.GetAllItems())
            {
                if (item.m_shared.m_name == eggName)
                {
                    stacks.Add(item);
                }
            }
            var plain = 0;
            var star = 0;
            foreach (var item in stacks)
            {
                if (item.m_quality == 1)
                {
                    plain += item.m_stack;
                }
                else if (item.m_quality == 2)
                {
                    star += item.m_stack;
                }
            }
            c.Check(stacks.Count == 2 && plain == 5 && star == 5,
                $"after the sort with MergeStacks: expected two stacks (5 x quality 1, 5 x quality 2), found {DescribeStacks(stacks)}");

            // Dropped on the ground (the game's own drop): the hover still tells the quality.
            var at = Ground(player.transform.position + forward * 3.5f) + Vector3.up * 0.5f;
            var hovers = new List<string>();
            for (var i = 0; i < stacks.Count; i++)
            {
                var drop = ItemDrop.DropItem(stacks[i], stacks[i].m_stack, at + Vector3.Cross(Vector3.up, forward) * (i - 0.5f), Quaternion.identity);
                drop.m_autoPickup = false;
                drops.Add(drop.gameObject);
            }
            yield return null;
            yield return null;
            for (var i = 0; i < stacks.Count && i < drops.Count; i++)
            {
                if (!c.Check(drops[i] != null, "a dropped egg stack vanished"))
                {
                    continue;
                }
                var hover = FirstLine(drops[i].GetComponent<ItemDrop>().GetHoverText());
                hovers.Add(hover);
                var wantNumber = stacks[i].m_quality > 1;
                c.Check(hover.IndexOf($"[{stacks[i].m_quality}]", StringComparison.Ordinal) >= 0 == wantNumber && (wantNumber || hover.IndexOf('[') < 0),
                    $"dropped quality-{stacks[i].m_quality} eggs hover '{hover}', expected {(wantNumber ? "the number [" + stacks[i].m_quality + "]" : "no number")}");
            }
            c.Report($"{sortName} (MergeStacks on): four egg part stacks (quality 1 x3 and x2, quality 2 x3 and x2) sorted into "
                     + $"{DescribeStacks(stacks)}; dropped on the ground they hover as {string.Join(" / ", hovers.ToArray())}");
        }
        finally
        {
            if (InventoryGui.instance != null && InventoryGui.IsVisible())
            {
                InventoryGui.instance.Hide();
            }
            foreach (var go in drops)
            {
                if (go != null && ZNetScene.instance != null)
                {
                    ZNetScene.instance.Destroy(go);
                }
            }
            if (chest != null)
            {
                var container = chest.GetComponent<Container>();
                if (container != null && container.GetInventory() != null)
                {
                    container.GetInventory().RemoveAll(); // empty chest: nothing spills when it goes
                }
                if (ZNetScene.instance != null)
                {
                    ZNetScene.instance.Destroy(chest);
                }
            }
        }
    }

    // ---------- breeding.cross.tamecounts ----------

    private static string CustomData(Player player, string key)
    {
        return player != null && player.m_customData != null && player.m_customData.TryGetValue(key, out var value) ? value : null;
    }

    // Count of one creature name in the stored tames ("1\n<count>\t<name>\n..."). Nothing stored = 0.
    private static int StoredTames(string raw, string creature)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return 0;
        }
        var total = 0;
        var lines = raw.Split('\n');
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var tab = line.IndexOf('\t');
            if (tab <= 0 || line.Substring(tab + 1) != creature)
            {
                continue;
            }
            if (int.TryParse(line.Substring(0, tab), NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            {
                total += count;
            }
        }
        return total;
    }

    private static int ErrorLines(List<LogLine> lines)
    {
        var errors = 0;
        foreach (var line in lines)
        {
            if ((line.Level & (LogLevel.Error | LogLevel.Fatal)) != 0 && !line.Text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal))
            {
                errors++;
            }
        }
        return errors;
    }

    private static IEnumerator RunTameCounts()
    {
        var c = new Checks(TameCountsName);
        var player = Player.m_localPlayer;
        if (player == null || Game.instance == null)
        {
            SelfTest.Fail(TameCountsName, "no local player (the test needs a world)");
            yield break;
        }
        if (!OtherActive(StatsGuid, out var statsName))
        {
            SelfTest.Fail(TameCountsName, $"{statsName} is not loaded or not active: this test needs it");
            yield break;
        }
        if (PenBlocked(TameCountsName))
        {
            yield break;
        }
        var position = player.transform.position;
        var rotation = player.transform.rotation;
        var tamesBefore = CustomData(player, StatsTamesKey);
        var boars = new Pen(TameCountsName, egg: false);
        var hens = new Pen(TameCountsName, egg: true);
        var extra = new List<GameObject>();
        var statsLog = new LogTap(statsName);
        var myMark = LogMark();
        var tamesTouched = false;
        var hadSoftDeath = HasSoftDeath(player);
        BepInEx.Logging.Logger.Listeners.Add(statsLog);
        try
        {
            // 1. A birth, a hatching and two growings: not one tame counted.
            if (!SetupPen(boars, "Boar", 1, 1))
            {
                yield break;
            }
            yield return null;
            Override = Always;
            var w = Begin(c, boars, boars.B, boars.A, "piglet");
            End(c, boars, boars.B, w, "piglet", 2, PartnerSource.Recorded, 1, out var piglet, true, null);
            yield return null;
            yield return null;
            if (piglet != null)
            {
                var grown = GrowUp(c, piglet.GetComponent<Character>(), "piglet growing up", out _);
                if (grown != null)
                {
                    extra.Add(grown.gameObject);
                    c.Check(grown.IsTamed() && grown.GetLevel() == 2, $"the grown boar: tamed {grown.IsTamed()}, level {grown.GetLevel()}");
                }
            }
            Cleanup(boars);
            DestroyAll(extra);
            yield return null;
            if (!SetupPen(hens, "Hen", 1, 1))
            {
                yield break;
            }
            yield return null;
            Override = Always;
            w = Begin(c, hens, hens.B, hens.A, "egg");
            End(c, hens, hens.B, w, "egg", 2, PartnerSource.Recorded, 1, out var egg, true, null);
            yield return null;
            yield return null;
            if (egg != null)
            {
                var chick = HatchNow(c, egg, "hatching", out _, out _, out _);
                if (chick != null)
                {
                    extra.Add(chick.gameObject);
                    yield return null;
                    yield return null;
                    var hen = chick != null ? GrowUp(c, chick, "chick growing up", out _) : null;
                    if (hen != null)
                    {
                        extra.Add(hen.gameObject);
                        c.Check(hen.IsTamed() && hen.GetLevel() == 2, $"the grown hen: tamed {hen.IsTamed()}, level {hen.GetLevel()}");
                    }
                }
            }
            Override = null;
            c.Check(CustomData(Player.m_localPlayer, StatsTamesKey) == tamesBefore,
                $"a birth, a hatching or growing up changed the tame counts of {statsName}: '{Show(tamesBefore)}' -> "
                + $"'{Show(CustomData(Player.m_localPlayer, StatsTamesKey))}'");
            Cleanup(hens);
            DestroyAll(extra);
            yield return null;

            // 2. Respawn: both mods patch the same game method (the character appearing).
            var mark = LogMark();
            var box = new Box();
            yield return Respawn(box);
            if (!c.Check(box.Ok, $"respawn: {box.Detail}"))
            {
                c.Report("");
                yield break;
            }
            var now = Player.m_localPlayer;
            var own = FarmerSkill.OwnLevel(now);
            c.Check(FarmerSkill.ReadPublished(now) == own, $"after the respawn the published Farming is {Inv(FarmerSkill.ReadPublished(now))}, own {Inv(own)}");
            c.Check(CountLines(mark, PublishedLineStart) == 1, "one Debug publish line expected when the character appeared after the respawn");
            c.Check(MyActive() && OtherActive(StatsGuid, out _), "both mods must still be active after the respawn");
            c.Check(CustomData(now, StatsSinceKey) != null, $"{statsName} has no counting start day on the new character");
            c.Check(CustomData(now, StatsTamesKey) == tamesBefore,
                $"the respawn changed the tame counts of {statsName}: '{Show(tamesBefore)}' -> '{Show(CustomData(now, StatsTamesKey))}'");
            yield return AfterRespawn(position, rotation);

            // 3. It keeps working: a real tame (the game's own Tame) adds exactly one.
            now = Player.m_localPlayer;
            var wild = now != null ? SpawnWild(hens, "Boar", Ground(now.transform.position + FlatForward(now) * 4f), 1) : null;
            yield return null;
            yield return null;
            var tameable = wild != null ? wild.GetComponent<Tameable>() : null;
            if (c.Check(tameable != null && now != null && Player.m_localPlayer == now, "could not spawn the untamed boar to tame"))
            {
                var counted = StoredTames(CustomData(now, StatsTamesKey), wild.m_name);
                tamesTouched = true;
                tameable.Tame();
                var after = StoredTames(CustomData(now, StatsTamesKey), wild.m_name);
                c.Check(wild.IsTamed(), "the game's Tame did not tame the boar");
                c.Check(after == counted + 1, $"after the respawn a real tame must add one to the counts of {statsName} ({counted} -> {after})");
            }
            // Next test (other mod's too) get a character that stand and can open its inventory.
            var free = new Box();
            yield return WaitPlayerFree(free, 30f);
            if (!free.Ok)
            {
                c.Note($"after the respawn {free.Detail}: a test that opens the inventory right after this one may fail");
            }
            c.Check(ErrorLines(statsLog.Since(0)) == 0, $"{statsName} logged an error during the test");
            c.Check(ErrorLines(LogSince(myMark)) == 0, $"{ModInfo.Name} logged an error during the test");
            c.Report($"with {statsName}: a birth, a hatching and two growings added no tame (counts '{Show(tamesBefore)}' kept); after a real "
                     + "respawn this mod published the Farming level at once, the counts and the counting start day were still there, "
                     + "and a real tame still added one (taken back after); no error line from either mod");
        }
        finally
        {
            BepInEx.Logging.Logger.Listeners.Remove(statsLog);
            Override = null;
            var now = Player.m_localPlayer;
            if (now != null)
            {
                if (tamesTouched && now.m_customData != null)
                {
                    // The test tame must not stay in the character's counts.
                    if (tamesBefore == null)
                    {
                        now.m_customData.Remove(StatsTamesKey);
                    }
                    else
                    {
                        now.m_customData[StatsTamesKey] = tamesBefore;
                    }
                }
                now.SetGodMode(true);
                DropSoftDeath(now, hadSoftDeath);
            }
            FarmerSkill.Reset();
            FarmerSkill.PublishNow();
            DestroyAll(extra);
            Cleanup(boars);
            Cleanup(hens);
        }
    }

    private static string Show(string raw)
    {
        return raw == null ? "nothing stored" : raw.Replace("\n", "\\n").Replace("\t", "\\t");
    }

    private static void DestroyAll(List<GameObject> objects)
    {
        foreach (var go in objects)
        {
            if (go != null && ZNetScene.instance != null)
            {
                ZNetScene.instance.Destroy(go);
            }
        }
        objects.Clear();
    }

    // ---------- breeding.cross.harpoon ----------

    private static IEnumerator RunHarpoon()
    {
        var pen = new Pen(HarpoonName, egg: false);
        var c = new Checks(HarpoonName);
        var player = Player.m_localPlayer;
        var hash = 0;
        try
        {
            if (player == null || ObjectDB.instance == null)
            {
                SelfTest.Fail(HarpoonName, "no local player (the test needs a world)");
                yield break;
            }
            if (!OtherActive(HarpoonGuid, out var harpoonName))
            {
                SelfTest.Fail(HarpoonName, $"{harpoonName} is not loaded or not active: this test needs it");
                yield break;
            }
            foreach (var effect in ObjectDB.instance.m_StatusEffects)
            {
                if (effect is SE_Harpooned)
                {
                    hash = effect.NameHash();
                    break;
                }
            }
            if (hash == 0)
            {
                SelfTest.Fail(HarpoonName, "the game's harpoon hook effect (SE_Harpooned) was not found");
                yield break;
            }
            if (PenBlocked(HarpoonName) || !SetupPen(pen, "Boar", 1, 3))
            {
                yield break;
            }
            yield return null;
            Override = Never;
            const string label = "partner hooked and moved away after the conception";
            var w = Begin(c, pen, pen.B, pen.A, label);
            if (w != null)
            {
                // The hit a thrown harpoon makes: pierce damage, the hook effect, thrown by this player.
                var health = pen.A.GetHealth();
                var hit = new HitData();
                hit.m_damage.m_pierce = 15f;
                hit.m_point = pen.A.GetCenterPoint();
                hit.m_dir = pen.Forward;
                hit.m_skill = Skills.SkillType.Spears;
                hit.m_statusEffectHash = hash;
                hit.SetAttacker(player);
                pen.A.Damage(hit);
                c.Check(pen.A.GetSEMan().HaveStatusEffect(hash), $"{label}: the tamed partner must carry the hook effect after the harpoon hit");
                c.Check(pen.A.GetHealth() >= health, $"{label}: the hook hurt the tame (health {Inv(health)} -> {Inv(pen.A.GetHealth())})");
                c.Check(pen.A.IsTamed(), $"{label}: the partner is no longer tamed");
                // Stand-in for the rope pull: 25 m, outside the pen range.
                Place(pen.A, pen.Center + pen.Side * 25f, pen.Forward);
                End(c, pen, pen.B, w, label, 1, PartnerSource.Recorded, 1, "self-test settings.", "own 3, partner 1 (recorded)");
            }
            Override = null;
            c.Report($"with {harpoonName}: the level-1 partner took a harpoon hit after the conception (hook effect on, no health lost, still "
                     + "tamed) and was moved 25 m away; the level-3 parent's piglet is level 1 with the recorded partner");
        }
        finally
        {
            Override = null;
            if (hash != 0 && pen.A != null && pen.A.GetSEMan() != null)
            {
                pen.A.GetSEMan().RemoveStatusEffect(hash, true);
            }
            Cleanup(pen);
        }
    }
}
#endif
