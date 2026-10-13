#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Bootstrap;
using HarmonyLib;
using MC.Shared;
using UnityEngine;
using PlayerPatchClass = MC.Farming.CultivatorReplantMod.Patches.PlayerPatches;

namespace MC.Farming.CultivatorReplantMod;

// Debug build only. Self tests with the mod really turned off and on again (Plugin.TestTurnOff / TestTurnOn: the two
// steps of the framework toggle, OnDeactivated + feature patches away, then back; no config write), the log watch, and
// the checks next to other MC mods:
//   replant.toggle      T30, T34, T36 (off part), T31 (same session part), T39 and T40 (mod off = young transplants on
//                       the player's own grow time, never the last rules in force; own setting changed while off)
//   replant.log         T32: no warning or error of this mod in the whole run but the cost warnings tests ask for
//   replant.x.idol      X01: with Forge Idol Upgrades loaded
//   replant.x.spyglass  X05: with Spyglass loaded
// RegisterMore / UnregisterMore: every newer test of the mod (SelfTests.Register calls them).
internal static partial class SelfTests
{
    private const string ToggleName = "replant.toggle";
    private const string LogName = "replant.log";
    private const string IdolName = "replant.x.idol";
    private const string SpyglassName = "replant.x.spyglass";

    private const string IdolGuid = "MC.Crafting.Forge.IdolUpgrades";
    private const string SpyglassItem = "MC_Spyglass";

    private static readonly KeyValuePair<string, Func<IEnumerator>>[] MoreTests =
    {
        new KeyValuePair<string, Func<IEnumerator>>(DigName, RunDig),
        new KeyValuePair<string, Func<IEnumerator>>(SwingName, RunSwing),
        new KeyValuePair<string, Func<IEnumerator>>(TierName, RunTier),
        new KeyValuePair<string, Func<IEnumerator>>(SaplingName, RunSapling),
        new KeyValuePair<string, Func<IEnumerator>>(PickedName, RunPicked),
        new KeyValuePair<string, Func<IEnumerator>>(CostsName, RunCosts),
        new KeyValuePair<string, Func<IEnumerator>>(FullName, RunFull),
        new KeyValuePair<string, Func<IEnumerator>>(HelmName, RunHelm),
        new KeyValuePair<string, Func<IEnumerator>>(PadName, RunPad),
        new KeyValuePair<string, Func<IEnumerator>>(WardName, RunWard),
        new KeyValuePair<string, Func<IEnumerator>>(BronzeName, RunBronze),
        new KeyValuePair<string, Func<IEnumerator>>(LevelsName, RunLevels),
        new KeyValuePair<string, Func<IEnumerator>>(PlantName, RunPlant),
        new KeyValuePair<string, Func<IEnumerator>>(CloudberryName, RunCloudberry),
        new KeyValuePair<string, Func<IEnumerator>>(RootPlaceName, RunRootPlace),
        new KeyValuePair<string, Func<IEnumerator>>(NoBuildName, RunNoBuild),
        new KeyValuePair<string, Func<IEnumerator>>(ClockName, RunClock),
        new KeyValuePair<string, Func<IEnumerator>>(SapName, RunSap),
        new KeyValuePair<string, Func<IEnumerator>>(RangeName, RunRange),
        new KeyValuePair<string, Func<IEnumerator>>(VanillaName, RunVanilla),
        new KeyValuePair<string, Func<IEnumerator>>(ForgeLowName, RunForgeLow),
        new KeyValuePair<string, Func<IEnumerator>>(ForgeHighName, RunForgeHigh),
        new KeyValuePair<string, Func<IEnumerator>>(SettingsName, RunSettings),
        new KeyValuePair<string, Func<IEnumerator>>(PotentialName, RunPotential),
        new KeyValuePair<string, Func<IEnumerator>>(TooltipName, RunTooltip),
        new KeyValuePair<string, Func<IEnumerator>>(SlotsName, RunSlots),
        new KeyValuePair<string, Func<IEnumerator>>(GemsName, RunGems),
        new KeyValuePair<string, Func<IEnumerator>>(ToggleName, RunToggle),
        new KeyValuePair<string, Func<IEnumerator>>(IdolName, RunIdol),
        new KeyValuePair<string, Func<IEnumerator>>(SpyglassName, RunSpyglass),
        new KeyValuePair<string, Func<IEnumerator>>(WrongBiomeName, RunWrongBiome),
        new KeyValuePair<string, Func<IEnumerator>>(ScytheName, RunScythe),
        new KeyValuePair<string, Func<IEnumerator>>(MountainName, RunMountain),
        new KeyValuePair<string, Func<IEnumerator>>(AshlandsName, RunAshlands),
        new KeyValuePair<string, Func<IEnumerator>>(NorthName, RunNorth),
        // Last: it reads what the whole run logged.
        new KeyValuePair<string, Func<IEnumerator>>(LogName, RunLog),
    };

    private static void RegisterMore()
    {
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

    private static bool FeaturePatched()
    {
        var method = AccessTools.Method(typeof(Player), nameof(Player.UpdatePlacement));
        return HasPatch(Harmony.GetPatchInfo(method)?.Prefixes, typeof(PlayerPatchClass), ModInfo.Guid);
    }

    // ---------- replant.toggle (T30, T34, T36 off part, T31 same-session part, T39, T40) ----------

    private static IEnumerator RunToggle()
    {
        if (!WorldReady(ToggleName))
        {
            yield break;
        }
        var c = new Checks(ToggleName);
        var player = Player.m_localPlayer;
        var gui = InventoryGui.instance;
        var recipe = CultivatorTiers.CultivatorRecipe;
        var rig = new Rig(player, ToggleName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig);
            yield return GrowReady(rig, c);
            // Whatever happens: on again before the rig puts the rest back.
            rig.Undo(Plugin.TestTurnOn);
            var inv = rig.Inv;
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var ygg = PlantCatalog.ByKey("YggaShoot");
            var name = rasp.ItemDisplayName;
            var vanilla = VanillaCultivatorIcon();
            var shared = CultivatorTiers.CultivatorDrop != null ? CultivatorTiers.CultivatorDrop.m_itemData.m_shared : null;
            if (!c.Check(recipe != null && shared != null && vanilla != null && Plugin.FeatureActive && FeaturePatched() && !player.m_noPlacementCost,
                    "mod on, its patches applied, no build cheat"))
            {
                c.Report();
                yield break;
            }
            var tool4 = rig.Give(PlantCatalog.CultivatorPrefab, 1, 4);
            rig.Give(rasp.ItemName, 3);
            rig.Give("Bronze", 5);
            rig.Give("RoundLog", 5);
            KnowRecipe(rig, recipe);

            // World: Forge level 4, Forge of Potential, chest with a level 3 cultivator, two young transplants.
            var forge = new ForgeRig();
            var potential = SpawnForge(rig, taken, forge) ? SpawnPotential(rig, taken) : null;
            if (!c.Check(forge.Station != null && potential != null, "Forge and Forge of Potential spawned"))
            {
                c.Report();
                yield break;
            }
            yield return ForgeLevel(rig, forge, 4, c);
            var chestSpot = Vector3.zero;
            var yggSpot = Vector3.zero;
            if (!c.Check(FindPlantSpot(rig, taken, 1.4f, out var raspSpot) && FindPlantSpot(rig, taken, 2.3f, out yggSpot)
                         && FindPlantSpot(rig, taken, 1.5f, out chestSpot), "three free spots"))
            {
                c.Report();
                yield break;
            }
            var raspSapling = rig.Spawn(rasp.SaplingName, raspSpot, Quaternion.identity);
            var yggSapling = rig.Spawn(ygg.SaplingName, yggSpot, Quaternion.identity);
            var chestGo = rig.Spawn("piece_chest_wood", chestSpot, Quaternion.identity);
            yield return Settle();
            var raspView = raspSapling.GetComponent<ZNetView>();
            var yggView = yggSapling.GetComponent<ZNetView>();
            var chest = chestGo != null ? chestGo.GetComponent<Container>() : null;
            var stored = chest != null ? chest.GetInventory().AddItem(PlantCatalog.CultivatorPrefab, 1, 3, 0, 0L, "", false) : null;
            var storedPlants = chest != null ? chest.GetInventory().AddItem(rasp.ItemName, 2, 1, 0, 0L, "", false) : null;
            c.Check(stored != null && storedPlants != null && stored.m_shared.m_maxQuality == 7, "chest holds a level 3 cultivator made while the mod was on, and two transplants");
            c.Check(RootGate.FindRoot(yggSpot, 30f) == null, "no Ancient Root near the Yggdrasil transplant");

            yield return Hold(rig, tool4);
            var bush = new Aimed();
            yield return SpawnInReach(rig, taken, "RaspberryBush", 1.2f, bush);
            if (!c.Check(bush.Seen && HintText().Contains("Replant") && SaplingPiecesOffered(player) >= 1,
                    "mod on: hint over a bush, transplants in the build menu" + bush.Why))
            {
                c.Report();
                yield break;
            }

            // T39, T40: the rules in force before the off use another grow time than this player's own setting (a
            // server's rules, or the setting as it was with the mod on). Own setting read from the config, never written.
            var own = CultivatorRules.Own().GrowTimeMultiplier;
            var other = Near(own, 0.1f, 0.01f) ? 0.5f : 0.1f;
            bool HasTimes(Plant pl, float multiplier)
            {
                TransplantContent.GrowTimes(rasp, multiplier, out var low, out var high);
                return pl != null && Near(pl.m_growTime, low, 0.5f) && Near(pl.m_growTimeMax, high, 0.5f);
            }
            string Times(Plant pl) => pl != null ? $"{F(pl.m_growTime)}-{F(pl.m_growTimeMax)} s" : "none";
            string TimesOf(float multiplier)
            {
                TransplantContent.GrowTimes(rasp, multiplier, out var low, out var high);
                return $"x{F(multiplier)} = {F(low)}-{F(high)} s";
            }
            var planted = raspSapling.GetComponent<Plant>();
            ServerRules.TestRules = new CultivatorRules { GrowTimeMultiplier = other };
            TransplantContent.Rebuild();
            c.Check(HasTimes(planted, other), $"mod on, rules with grow time {TimesOf(other)}: the planted transplant carries it ({Times(planted)})");

            // ----- off -----
            Plugin.TestTurnOff();
            yield return Frames(3);
            c.Check(!Plugin.FeatureActive && !FeaturePatched() && !CultivatorTiers.InForce, "turned off: feature patches gone, tiers not in force");
            // T40: the transplant planted before is on this player's own grow time from the moment the mod is off.
            c.Check(HasTimes(planted, own),
                $"off: the transplant planted before is on this player's own grow time ({TimesOf(own)}), not on the rules in force before the off (x{F(other)}); it has {Times(planted)}");
            yield return rig.AimAt(bush.AimAt);
            yield return Frames(4);
            c.Check(HintText().Length == 0 && !CrosshairYellow() && !Replant.Target.IsFresh, $"off: no hint over the bush ('{OneLine(HintText())}')");
            // The two checks around this one only mean something while the crosshair really is on the bush (the game's
            // own hover ray, the one Replant uses when on).
            player.FindHoverObject(out var offHover, out _);
            var offView = offHover != null ? offHover.GetComponentInParent<ZNetView>() : null;
            c.Check(offView != null && ReferenceEquals(offView, bush.View), $"off: the crosshair is still on the bush ({RayNote(player)})");
            yield return ReadyForKey(player);
            yield return Tap(UseAndSnap);
            yield return Frames(3);
            c.Check(Alive(bush.View) && CountByName(inv, name) == 3, "off: E on the bush does nothing");
            c.Check(SaplingPiecesOffered(player) == 0, $"off: no transplant in the build menu ({SaplingPiecesOffered(player)})");
            c.Check(CountByName(inv, name) == 3 && ObjectDB.instance.GetItemPrefab(rasp.ItemName) != null && ZNetScene.instance.GetPrefab(rasp.SaplingHash) != null,
                "off: transplant items stay, items and young plants still known to the game");
            c.Check(tool4.m_quality == 4 && inv.ContainsItem(tool4) && shared.m_maxQuality == 3, "off: the level 4 cultivator keeps level 4 (cultivators stop at 3 again)");
            c.Check(ReferenceEquals(tool4.GetIcon(), vanilla) && !tool4.GetTooltip().Contains("Tier:") && !tool4.GetTooltip().Contains("Replants:"),
                "off: no gem, no tier lines on the level 4 cultivator");

            // T31, same session part: a save and load of the inventory and of the chest keeps them while off.
            var pkg = new ZPackage();
            inv.Save(pkg);
            pkg.SetPos(0);
            var loaded = new Inventory("MC_SelfTest", null, inv.GetWidth(), inv.GetHeight());
            loaded.Load(pkg);
            c.Check(CountByName(loaded, name) == 3 && loaded.GetAllItems().Any(i => CultivatorTiers.IsCultivator(i) && i.m_quality == 4),
                "off: inventory saved and loaded again still has the transplants and the level 4 cultivator");
            if (chest != null)
            {
                var boxPkg = new ZPackage();
                chest.GetInventory().Save(boxPkg);
                boxPkg.SetPos(0);
                var box = new Inventory("MC_SelfTest", null, chest.GetInventory().GetWidth(), chest.GetInventory().GetHeight());
                box.Load(boxPkg);
                c.Check(CountByName(box, name) == 2, "off: chest saved and loaded again still has its transplants");
            }
            var raspPlant = raspSapling.GetComponent<Plant>();
            c.Check(Alive(raspView) && raspPlant.GetHoverText().Contains(name), $"off: the young transplant is still there with its name ('{OneLine(raspPlant.GetHoverText())}')");

            // T36 off part: level number back in the inventory slot.
            player.UnequipItem(tool4, false);
            gui.Show(null);
            yield return new WaitForSecondsRealtime(1f);
            var slot = SlotOf(gui.m_playerGrid, inv, tool4);
            c.Check(NumberSlot(slot, 4), $"off: level 4 slot shows its number again, no gem ({SlotText(slot)})");
            gui.Hide();
            yield return new WaitForSecondsRealtime(0.5f);

            // T34: level 3 cultivator taken from the chest after turning off, at a Forge of level 4 with Bronze and
            // Corewood: never offered for level 4.
            if (stored != null)
            {
                inv.MoveItemToThis(chest.GetInventory(), stored);
                c.Check(inv.ContainsItem(stored) && stored.m_shared.m_maxQuality == 7, "off: level 3 cultivator taken out of the chest (its own data still says 7 levels)");
                yield return OpenStation(rig, forge.Station, true);
                c.Check(!AnyCultivatorRow(), $"off: Forge level {forge.Station.GetLevel()} Upgrade tab offers no cultivator upgrade ({gui.m_availableRecipes.Count} rows)");
                c.Check(stored.m_shared.m_maxQuality == 3, "off: the cultivator from the chest is back to 3 levels");
                yield return CloseStation(rig);
            }
            yield return OpenStation(rig, potential, true);
            c.Check(AnyCultivatorRow(), "off: the Forge of Potential lists the cultivator again");
            yield return CloseStation(rig);

            // T39 (the check of the former replant.bug.offgrow): a young transplant that loads while the mod is off
            // carries this player's own grow time, not the last rules in force before it was turned off.
            GameObject freshGo = null;
            if (c.Check(FindPlantSpot(rig, taken, 1.4f, out var freshSpot), "free spot for a transplant that loads while the mod is off"))
            {
                freshGo = rig.Spawn(rasp.SaplingName, freshSpot, Quaternion.identity);
                yield return Settle();
            }
            var fresh = freshGo != null ? freshGo.GetComponent<Plant>() : null;
            if (c.Check(fresh != null && !Plugin.FeatureActive, "mod off, young raspberry transplant planted"))
            {
                c.Check(HasTimes(fresh, own),
                    $"mod off: a young transplant grows on this player's own grow time ({TimesOf(own)}), not on the last rules in force before it was turned off (x{F(other)}); it has {Times(fresh)}");
            }
            // T39, T40: the own setting changed while the mod is off (in memory, ServerRules.TestOwn: never the config
            // file) reaches the planted transplant, the one loaded while off and the young-plant prefab (what a
            // transplant loading later copies) at once: nobody runs the rebuild delay while the feature patches are off.
            var saplingPrefab = TransplantContent.SaplingPrefab(rasp);
            var prefabPlant = saplingPrefab != null ? saplingPrefab.GetComponent<Plant>() : null;
            var changed = Near(own, 0.5f, 0.01f) ? 2f : 0.5f;
            ServerRules.TestOwn = new CultivatorRules { GrowTimeMultiplier = changed };
            c.Check(!TransplantContent.RebuildPending && HasTimes(planted, changed) && HasTimes(fresh, changed) && HasTimes(prefabPlant, changed),
                $"off: own GrowTimeMultiplier changed to {TimesOf(changed)}: at once on the planted transplant ({Times(planted)}), the one loaded while off ({Times(fresh)}) "
                + $"and the young-plant prefab ({Times(prefabPlant)})");
            // Own setting = the default 1 from here (whatever the tester's config says): the growth below runs on it.
            ServerRules.TestOwn = new CultivatorRules();
            var due = Alive(raspView) ? planted.GetGrowTime() : 0f;
            c.Check(HasTimes(planted, 1f) && HasTimes(fresh, 1f) && HasTimes(prefabPlant, 1f) && due > 2000f && due < 20000f,
                $"off: own GrowTimeMultiplier back to 1: all three on {TimesOf(1f)} again; the planted transplant is due {F(due)} s after planting "
                + "(young 2000 s after, due 20000 s after)");
            rig.Destroy(freshGo);

            // Young transplants keep growing while off, like vanilla saplings (the Yggdrasil one with no root, no sap).
            Age(raspView, 20000.0);
            Age(yggView, 8000.0);
            var grownRasp = new Grown();
            yield return WaitGrown(rig, rasp, raspView, raspSpot, 16f, grownRasp);
            c.Check(grownRasp.Ok, $"off: the young raspberry transplant grows ({F(grownRasp.Waited)} s)");
            var grownYgg = new Grown();
            yield return WaitGrown(rig, ygg, yggView, yggSpot, 8f, grownYgg);
            c.Check(grownYgg.Ok, "off: a Yggdrasil transplant grows with no Ancient Root (vanilla sapling rules)");

            // ----- on again -----
            Plugin.TestTurnOn();
            UseDefaultRules();
            yield return Frames(3);
            c.Check(Plugin.FeatureActive && FeaturePatched() && CultivatorTiers.InForce && shared.m_maxQuality == 7, "on again: patches back, 7 levels");
            c.Check(ReferenceEquals(tool4.GetIcon(), TierIcons.CultivatorIcon(vanilla, 4)) && tool4.GetTooltip().Contains("Tier: <color=orange>Black metal cultivator</color>"),
                "on again: gem and tier lines back");
            gui.Show(null);
            yield return new WaitForSecondsRealtime(1f);
            slot = SlotOf(gui.m_playerGrid, inv, tool4);
            c.Check(GemSlot(slot, 4), $"on again: level 4 slot shows the gem, no number ({SlotText(slot)})");
            gui.Hide();
            yield return new WaitForSecondsRealtime(0.5f);
            if (stored != null)
            {
                yield return OpenStation(rig, forge.Station, true);
                var row = RecipeRow(recipe, stored);
                if (c.Check(row >= 0, "on again: the level 3 cultivator is listed for level 4"))
                {
                    gui.SetRecipe(row, false);
                    yield return Frames(3);
                    c.Check(SameRows(RequirementRows(), WantRows("BlackMetal", 5, "LinenThread", 10)), $"on again: with the black metal materials ('{RequirementRows()}')");
                }
                yield return CloseStation(rig);
            }
            yield return OpenStation(rig, potential, true);
            c.Check(!AnyCultivatorRow(), "on again: the Forge of Potential lists no cultivator");
            yield return CloseStation(rig);
            yield return Hold(rig, tool4);
            c.Check(SaplingPiecesOffered(player) >= 1, "on again: transplants back in the build menu");
            // The bush of the first look stood there for a quarter of a minute: by now a grown transplant, a creature
            // or Hugin may stand in the camera ray (run of 2026-10-08: no hint over it, the first run of the same code
            // had one). Still under the crosshair = that bush; else say what the ray hit and take a new bush where
            // Replant sees it, found like the first one.
            yield return Stand(rig, bush.Spot);
            yield return rig.AimAt(bush.AimAt);
            yield return Frames(4);
            var again = Replant.Target;
            if (!(again.IsFresh && ReferenceEquals(again.View, bush.View)))
            {
                c.Note($"on again: the first bush is not under the crosshair any more (bush still there: {Alive(bush.View)}; {RayNote(player)}); a new bush is used");
                bush = new Aimed();
                yield return SpawnInReach(rig, taken, "RaspberryBush", 1.2f, bush);
                yield return Frames(2);
            }
            c.Check(bush.Seen && HintText().Contains("Replant") && CrosshairYellow(), $"on again: hint back ('{OneLine(HintText())}')" + bush.Why);
            var carried = CountByName(inv, name);
            yield return ReadyForKey(player);
            yield return Tap(UseAndSnap);
            yield return WaitGone(bush.View, 1.5f);
            c.Check(!Alive(bush.View) && carried == 3 && CountByName(inv, name) == 4, $"on again: E digs the bush up ({carried} -> {CountByName(inv, name)} transplants)");
            c.Report();
        }
        finally
        {
            if (InventoryGui.instance != null && InventoryGui.IsVisible())
            {
                InventoryGui.instance.Hide();
            }
            rig.Restore();
        }
    }

    // ---------- replant.log (T32) ----------

    // Warnings the cost settings tests ask for on purpose (T26, replant.tiers, replant.settings).
    private static readonly string[] AskedWarnings =
    {
        "has no valid material", "is not an item in this game", "has no valid amount", "is listed twice in",
    };

    private static IEnumerator RunLog()
    {
        var c = new Checks(LogName);
        if (!c.Check(SelfTestLog.Installed, "log watch installed at plugin load"))
        {
            c.Report();
            yield break;
        }
        var problems = SelfTestLog.Problems();
        var asked = problems.Where(p => AskedWarnings.Any(a => p.Text.Contains(a))).ToList();
        var other = problems.Where(p => !AskedWarnings.Any(a => p.Text.Contains(a))).ToList();
        c.Note($"{SelfTestLog.Mark} line(s) logged by {ModInfo.Name} this session, {asked.Count} cost warning(s) asked for by the settings tests");
        c.Check(SelfTestLog.Dropped == 0, $"every line kept ({SelfTestLog.Dropped} dropped)");
        c.Check(other.Count == 0, other.Count == 0
            ? "no other warning or error of this mod in the whole run"
            : $"{other.Count} warning(s) or error(s) of this mod: " + string.Join(" | ", other.Take(4).Select(p => p.Level + ": " + p.Text.Substring(0, Math.Min(220, p.Text.Length))).ToArray()));
        c.Report();
    }

    // ---------- replant.x.idol (X01) ----------

    private static IEnumerator RunIdol()
    {
        if (!WorldReady(IdolName))
        {
            yield break;
        }
        var c = new Checks(IdolName);
        if (!Chainloader.PluginInfos.ContainsKey(IdolGuid))
        {
            SelfTest.Fail(IdolName, "Forge Idol Upgrades is not loaded: nothing checked");
            yield break;
        }
        var player = Player.m_localPlayer;
        var gui = InventoryGui.instance;
        var recipe = CultivatorTiers.CultivatorRecipe;
        var rig = new Rig(player, IdolName);
        var taken = new List<Vector3>();
        try
        {
            UseDefaultRules();
            rig.Noon();
            rig.TakeControls();
            var idolMod = FeatureRegistry.Find(IdolGuid);
            c.Check(idolMod != null && idolMod.Value.IsActive, "Forge Idol Upgrades active");
            var station = SpawnPotential(rig, taken);
            yield return new WaitForSeconds(0.5f);
            var cultivators = new[] { 1, 3, 4 }.Select(q => rig.Give(PlantCatalog.CultivatorPrefab, 1, q)).ToArray();
            var axe = rig.Give("AxeStone", 1);
            rig.Give("Upgrader0Weapon", 3);
            var axeRecipe = axe != null ? ObjectDB.instance.GetRecipe(axe) : null;
            if (!c.Check(recipe != null && station != null && cultivators.All(i => i != null) && axeRecipe != null, "Forge of Potential, cultivators, Stone axe and idols"))
            {
                c.Report();
                yield break;
            }
            KnowRecipe(rig, recipe);
            KnowRecipe(rig, axeRecipe);
            yield return OpenStation(rig, station, true);
            c.Check(!AnyCultivatorRow(), $"with Forge Idol Upgrades: no cultivator in the Forge of Potential list ({gui.m_availableRecipes.Count} rows)");
            c.Check(gui.m_availableRecipes.Any(r => ReferenceEquals(r.ItemData, axe)), "the Stone axe is listed");
            yield return CloseStation(rig);
            // What its guard looks at: nobody of this mod in front of or inside the crafting method.
            var crafting = Harmony.GetPatchInfo(AccessTools.Method(typeof(InventoryGui), nameof(InventoryGui.DoCrafting)));
            bool Ours(Patch p) => p.owner != null && p.owner.StartsWith(ModInfo.Guid, StringComparison.Ordinal);
            c.Check(crafting == null || (!crafting.Prefixes.Any(Ours) && !crafting.Transpilers.Any(Ours)), "this mod has no prefix or transpiler on the crafting method");
            var warned = SelfTestLog.Mentions().Where(m => m.Contains("Another mod may take over refinement") || m.Contains("Another mod changes crafting or the Forge of Potential")).ToList();
            c.Check(warned.Count == 0, warned.Count == 0 ? "no takeover warning of Forge Idol Upgrades names this mod" : "Forge Idol Upgrades warned: " + warned[0]);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- replant.x.spyglass (X05) ----------

    private static IEnumerator RunSpyglass()
    {
        if (!WorldReady(SpyglassName))
        {
            yield break;
        }
        var c = new Checks(SpyglassName);
        if (ObjectDB.instance.GetItemPrefab(SpyglassItem) == null)
        {
            SelfTest.Fail(SpyglassName, "Spyglass is not loaded (no MC_Spyglass item): nothing checked");
            yield break;
        }
        var player = Player.m_localPlayer;
        var rig = new Rig(player, SpyglassName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig);
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            var glass = rig.Give(SpyglassItem, 1);
            if (!c.Check(tool != null && glass != null, "cultivator and spyglass given"))
            {
                c.Report();
                yield break;
            }
            yield return Hold(rig, tool);
            var bush = new Aimed();
            yield return SpawnInReach(rig, taken, "RaspberryBush", 1.2f, bush);
            if (!c.Check(bush.Seen && HintText().Contains("Replant"), "cultivator in hand: Replant hint over the bush" + bush.Why))
            {
                c.Report();
                yield break;
            }
            yield return Hold(rig, glass);
            yield return rig.AimAt(bush.AimAt);
            yield return Frames(5);
            c.Check(ReferenceEquals(player.GetRightItem(), glass) || ReferenceEquals(player.GetLeftItem(), glass), "spyglass in hand");
            c.Check(!Replant.Target.IsFresh && !HintText().Contains("Replant"), $"spyglass in hand: no Replant hint ('{OneLine(HintText())}')");
            yield return ReadyForKey(player);
            var count = CountByName(rig.Inv, rasp.ItemDisplayName);
            yield return Hold(rig, tool);
            yield return rig.AimAt(bush.AimAt);
            yield return Frames(4);
            c.Check(Replant.Target.IsFresh && HintText().Contains("Replant"), "back to the cultivator: hint back");
            yield return ReadyForKey(player);
            yield return Tap(UseAndSnap);
            yield return WaitGone(bush.View, 1.5f);
            c.Check(!Alive(bush.View) && CountByName(rig.Inv, rasp.ItemDisplayName) == count + 1, "and E digs the bush up");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }
}
#endif
