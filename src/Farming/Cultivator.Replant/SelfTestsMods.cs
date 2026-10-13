#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Farming.CultivatorReplantMod;

// Debug build only. Self tests next to other mods loaded in the same game (same class as SelfTests.cs). Other mod found
// by GUID in the shared registry; never through its types: its settings read through BepInEx (never written), its
// work seen through the game (inventory, crafting list, console command, camera, log). Other mod missing = FAIL
// (nothing checked must never read as a pass). One test per TESTING.md item:
//   replant.x.idol-refine   X01: Forge of Potential, level 2 Stone axe: button shows Forge Idol Upgrades' chance, one
//                           attempt follows its rules (same item kept, one level up or down, one idol used); at the
//                           Forge a cultivator still goes from level 3 to 4
//   replant.x.search        X02: Crafting Search and Sort's search field, Forge Upgrade tab: "black metal" lists a
//                           level 3 cultivator and "bronze" does not; a level 1 cultivator the other way round
//   replant.x.encyclopedia  X03: Encyclopedia pages (its console command compendium_debug, real knowledge): transplant
//                           under Materials with name and description, young transplant among the cultivator's
//                           pieces, Cultivator page levels 4-7 with materials and Forge level, no idol row, Black
//                           metal "Used in" the Cultivator
//   replant.x.lootfilter    X04: Loot Pickup Filter on "Only selected" with an empty list (its console command): dug
//                           bush gives a transplant to the bag and its berries are still auto picked up, while a
//                           plain item lying there is not
//   replant.x.spyglass-view X05: spyglass raised: zoomed view and its round overlay shown, no Replant hint meanwhile;
//                           lowered, cultivator back: hint back, E digs
//   replant.e.planteasily.grid  E02 a, d, e: PlantEasily on (the tester's own settings): one press plants a grid of
//                           raspberry transplants, one transplant used per plant, all grow; last transplant planted
//                           while selected (NOTE whether PlantEasily logs its own error); no other error line
//   replant.e.planteasily.root  E04 b, E02 b: Yggdrasil transplant aimed 3 to 5 m from an Ancient Root with
//                           PlantEasily on: green ghost, planted; grid copies out of the root's reach say so and
//                           wait; E gives one back
internal static partial class SelfTests
{
    private const string IdolRefineName = "replant.x.idol-refine";
    private const string SearchName = "replant.x.search";
    private const string EncyclopediaName = "replant.x.encyclopedia";
    private const string LootFilterName = "replant.x.lootfilter";
    private const string SpyglassViewName = "replant.x.spyglass-view";
    private const string EasyGridName = "replant.e.planteasily.grid";
    private const string EasyRootName = "replant.e.planteasily.root";

    private const string SearchGuid = "MC.UX.Crafting.SearchSort";
    private const string SearchFieldObject = "MC_CraftSearchField";
    private const string EncyclopediaGuid = "MC.Exploration.Compendium.Encyclopedia";
    private const string EncyclopediaCommand = "compendium_debug";
    private const string LootGuid = "MC.UX.AutoPickup.Filter";
    private const string LootCommand = "lootfilter";
    private const string SpyglassGuid = "MC.Exploration.View.Spyglass";
    private const string IdolItem = "Upgrader0Weapon";

    // ---------- tools ----------

    private static bool OtherActive(string guid)
    {
        var view = FeatureRegistry.Find(guid);
        return view != null && view.Value.IsActive;
    }

    // Other MC mod there and active? No = FAIL line (item not checked), false.
    private static bool NeedOther(string test, string guid, string name, string item)
    {
        if (OtherActive(guid))
        {
            return true;
        }
        var view = FeatureRegistry.Find(guid);
        SelfTest.Fail(test, $"{name} is not loaded and active ({(view != null ? view.Value.State + ": " + view.Value.Status : "not loaded")}): nothing of TESTING.md {item} was checked");
        return false;
    }

    // Setting of another plugin as BepInEx holds it. Read only. Null = plugin or setting missing.
    private static object OtherSetting(string guid, string section, string key)
    {
        if (!Chainloader.PluginInfos.TryGetValue(guid, out var info) || info.Instance == null)
        {
            return null;
        }
        var config = info.Instance.Config;
        foreach (var def in config.Keys)
        {
            if (def.Section == section && def.Key == key)
            {
                return config[def].BoxedValue;
            }
        }
        return null;
    }

    private static string Text(object value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

    private static void RunConsole(string command)
    {
        if (Console.instance != null)
        {
            Console.instance.TryRunCommand(command);
        }
    }

    // Me = ears on every log source for a short time (other mod's command answer, error lines while a test plants).
    private sealed class LineCatcher : ILogListener
    {
        internal readonly List<string> All = new List<string>();
        internal readonly List<string> Errors = new List<string>();

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            if (eventArgs == null || eventArgs.Data == null)
            {
                return;
            }
            var text = eventArgs.Data.ToString();
            if (text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal))
            {
                return;
            }
            lock (All)
            {
                if (All.Count < 2000)
                {
                    All.Add(text);
                }
                if ((eventArgs.Level & (LogLevel.Error | LogLevel.Fatal)) != 0 && Errors.Count < 200)
                {
                    Errors.Add((eventArgs.Source != null ? eventArgs.Source.SourceName : "?") + ": " + text);
                }
            }
        }

        public void Dispose()
        {
        }
    }

    // "BlackMetal:5,LinenThread:10" -> pairs.
    private static List<KeyValuePair<string, int>> CostOf(string text)
    {
        var list = new List<KeyValuePair<string, int>>();
        foreach (var part in (text ?? "").Split(','))
        {
            var bits = part.Split(':');
            if (bits.Length == 2 && int.TryParse(bits[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
            {
                list.Add(new KeyValuePair<string, int>(bits[0].Trim(), amount));
            }
        }
        return list;
    }

    // ---------- replant.x.idol-refine (X01) ----------

    private static IEnumerator RunIdolRefine()
    {
        if (!WorldReady(IdolRefineName) || !NeedOther(IdolRefineName, IdolGuid, "Forge Idol Upgrades", "X01"))
        {
            yield break;
        }
        var c = new Checks(IdolRefineName);
        var player = Player.m_localPlayer;
        var gui = InventoryGui.instance;
        var recipe = CultivatorTiers.CultivatorRecipe;
        var rig = new Rig(player, IdolRefineName);
        var taken = new List<Vector3>();
        try
        {
            UseDefaultRules();
            rig.Noon();
            rig.TakeControls();
            // Its settings, read only: what the attempt must follow.
            var chance = Text(OtherSetting(IdolGuid, "Refinement", "ChanceLevel0"));
            var onFailure = Text(OtherSetting(IdolGuid, "Refinement", "OnFailure"));
            var lostText = Text(OtherSetting(IdolGuid, "Refinement", "LevelsLost"));
            var haveLost = int.TryParse(lostText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lost);
            if (!c.Check(chance.Length > 0 && onFailure.Length > 0 && haveLost,
                    $"Forge Idol Upgrades settings read: plain idol chance {chance}%, on failure {onFailure}, levels lost {lostText}"))
            {
                c.Report();
                yield break;
            }
            var station = SpawnPotential(rig, taken);
            yield return new WaitForSeconds(0.5f);
            var axe = rig.Give("AxeStone", 1, 2);
            rig.Give(IdolItem, 3);
            var axeRecipe = axe != null ? ObjectDB.instance.GetRecipe(axe) : null;
            var idolName = SharedName(IdolItem);
            if (!c.Check(recipe != null && station != null && station.m_upgrader && axe != null && axe.m_quality == 2 && axeRecipe != null && CountByName(rig.Inv, idolName) == 3,
                    "Forge of Potential, a level 2 Stone axe and three plain Wooden Battle Idols"))
            {
                c.Report();
                yield break;
            }
            KnowRecipe(rig, recipe);
            KnowRecipe(rig, axeRecipe);
            yield return OpenStation(rig, station, true);
            var row = RecipeRow(axeRecipe, axe);
            if (c.Check(row >= 0, $"the Stone axe is listed for refinement ({gui.m_availableRecipes.Count} rows)"))
            {
                gui.SetRecipe(row, false);
                yield return Frames(3);
                var label = gui.m_craftButton != null ? gui.m_craftButton.GetComponentInChildren<TMPro.TMP_Text>() : null;
                var shown = label != null ? label.text : "";
                var want = L("$inventory_upgraderbutton") + " (" + chance + "%)";
                c.Check(shown == want, $"the button shows Forge Idol Upgrades' chance of a plain idol ('{shown}', want '{want}')");
                ClearCenter();
                yield return CraftRow(row);
                yield return Frames(3);
                var kept = rig.Inv.ContainsItem(axe);
                var idols = CountByName(rig.Inv, idolName);
                c.Note($"after one attempt: same axe still carried {kept}, level {axe.m_quality}, {idols} idol(s) left; centre text '{CenterText()}'");
                c.Check(idols == 2, $"one idol used ({idols} of 3 left)");
                if (string.Equals(onFailure, "LoseLevels", StringComparison.Ordinal))
                {
                    var down = Mathf.Max(1, 2 - lost);
                    // Vanilla takes the item away and gives a new one (success, level 3), a lower one or nothing: the
                    // same item still there one level up or down is this mod's rule.
                    c.Check(kept && (axe.m_quality == 3 || axe.m_quality == down),
                        $"Forge Idol Upgrades' rule: the same axe stays in the inventory at level 3 (success) or level {down} (failure, never destroyed); it is level {axe.m_quality}, kept {kept}");
                }
                else
                {
                    var any = rig.Inv.GetAllItems().Any(i => i.m_shared.m_name == axe.m_shared.m_name);
                    c.Check((kept && axe.m_quality == 3) || !any,
                        $"Forge Idol Upgrades with OnFailure = {onFailure}: the same axe at level 3 (success) or no axe (failure); level {axe.m_quality}, kept {kept}, any axe {any}");
                }
            }
            yield return CloseStation(rig);

            // At the Forge a cultivator still goes from level 3 to 4, with Forge Idol Upgrades active.
            var forge = new ForgeRig();
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 3);
            foreach (var cost in CostOf(CultivatorRules.DefaultBlackMetalLevel))
            {
                rig.Give(cost.Key, cost.Value);
            }
            if (c.Check(tool != null && SpawnForge(rig, taken, forge), "Forge spawned, level 3 cultivator and the black metal materials given"))
            {
                yield return ForgeLevel(rig, forge, 4, c);
                yield return OpenStation(rig, forge.Station, true);
                var crafted = new Crafted();
                var wantRows = string.Join(", ", CostOf(CultivatorRules.DefaultBlackMetalLevel).Select(k => L(SharedName(k.Key)) + " x" + k.Value).ToArray());
                yield return CraftStep(rig, c, recipe, tool, 4, wantRows, crafted);
                c.Check(crafted.Item != null && OtherActive(IdolGuid), "with Forge Idol Upgrades active: the Forge upgrade from level 3 to 4 works");
                yield return CloseStation(rig);
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- replant.x.search (X02) ----------

    private static TMPro.TMP_InputField SearchField()
    {
        var gui = InventoryGui.instance;
        return gui != null ? gui.GetComponentsInChildren<TMPro.TMP_InputField>(true).FirstOrDefault(f => f != null && f.name == SearchFieldObject) : null;
    }

    // Text typed into the other mod's search field (its own change event), list rebuilt (it waits 0.1 s).
    private static IEnumerator TypeSearch(TMPro.TMP_InputField field, string text)
    {
        if (field.text != "")
        {
            field.text = "";
            yield return new WaitForSecondsRealtime(0.4f);
        }
        field.text = text;
        yield return new WaitForSecondsRealtime(0.5f);
        yield return Frames(3);
    }

    private static IEnumerator RunSearch()
    {
        if (!WorldReady(SearchName) || !NeedOther(SearchName, SearchGuid, "Crafting Search and Sort", "X02"))
        {
            yield break;
        }
        var c = new Checks(SearchName);
        var player = Player.m_localPlayer;
        var gui = InventoryGui.instance;
        var recipe = CultivatorTiers.CultivatorRecipe;
        var rig = new Rig(player, SearchName);
        var taken = new List<Vector3>();
        try
        {
            UseDefaultRules();
            rig.Noon();
            rig.TakeControls();
            var forge = new ForgeRig();
            var tool3 = rig.Give(PlantCatalog.CultivatorPrefab, 1, 3);
            // Materials in the bag: the game shows (and the search reads) only requirements the player knows.
            foreach (var material in new[] { "BlackMetal", "LinenThread", "Bronze", "RoundLog" })
            {
                rig.Give(material, 1);
            }
            // Control row: a second Forge upgrade that has nothing of black metal. With the cultivator alone in the list,
            // "the search filters" could never be seen on a search that keeps the cultivator.
            var other = rig.Give("AxeBronze", 1, 1);
            var otherRecipe = other != null ? ObjectDB.instance.GetRecipe(other) : null;
            if (!c.Check(recipe != null && tool3 != null && otherRecipe != null && SpawnForge(rig, taken, forge), "Forge spawned, level 3 cultivator and a Bronze axe (control row) given"))
            {
                c.Report();
                yield break;
            }
            KnowRecipe(rig, recipe);
            KnowRecipe(rig, otherRecipe);
            yield return ForgeLevel(rig, forge, 4, c);
            yield return OpenStation(rig, forge.Station, true);
            var field = SearchField();
            if (!c.Check(field != null && field.gameObject.activeInHierarchy, "the search field of Crafting Search and Sort is in the crafting window"))
            {
                c.Report();
                yield break;
            }
            var oldText = field.text;
            rig.Undo(() =>
            {
                if (field != null)
                {
                    field.text = oldText;
                }
            });
            yield return TypeSearch(field, "");
            var all = gui.m_availableRecipes.Count;
            c.Check(RecipeRow(recipe, tool3) >= 0 && RecipeRow(otherRecipe, other) >= 0,
                $"no search text: the level 3 cultivator and the Bronze axe are listed for their upgrade ({all} rows)");

            yield return TypeSearch(field, "black metal");
            var black = gui.m_availableRecipes.Count;
            c.Check(RecipeRow(recipe, tool3) >= 0, $"level 3 cultivator, search 'black metal': the Cultivator is listed ({black} of {all} rows)");
            c.Check(black < all && RecipeRow(otherRecipe, other) < 0, $"the search really filters the list: the Bronze axe is not listed ({black} of {all} rows)");
            yield return TypeSearch(field, "bronze");
            c.Check(RecipeRow(recipe, tool3) < 0, $"level 3 cultivator, search 'bronze': the Cultivator is not listed ({gui.m_availableRecipes.Count} rows)");

            // Level 1 cultivator instead.
            yield return TypeSearch(field, "");
            rig.Inv.RemoveItem(tool3);
            var tool1 = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            yield return Frames(3);
            yield return TypeSearch(field, "bronze");
            c.Check(tool1 != null && RecipeRow(recipe, tool1) >= 0, $"level 1 cultivator, search 'bronze': the Cultivator is listed ({gui.m_availableRecipes.Count} rows)");
            yield return TypeSearch(field, "black metal");
            c.Check(tool1 != null && RecipeRow(recipe, tool1) < 0, $"level 1 cultivator, search 'black metal': the Cultivator is not listed ({gui.m_availableRecipes.Count} rows)");
            field.text = oldText;
            yield return new WaitForSecondsRealtime(0.3f);
            yield return CloseStation(rig);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- replant.x.encyclopedia (X03) ----------

    // Page of one entry as the Encyclopedia writes it for the player's real knowledge: answer of its console command
    // (Debug builds), caught from the log. "" = it said nothing.
    private static string EncyclopediaPage(string key)
    {
        var catcher = new LineCatcher();
        BepInEx.Logging.Logger.Listeners.Add(catcher);
        try
        {
            RunConsole(EncyclopediaCommand + " " + key);
        }
        finally
        {
            BepInEx.Logging.Logger.Listeners.Remove(catcher);
        }
        lock (catcher.All)
        {
            // The answer is one log entry: "name | tab" first line of a page, or its "catalog is building" line. Other
            // entries of that moment (debug lines of any mod) are not the page: last such entry wins.
            for (var i = catcher.All.Count - 1; i >= 0; i--)
            {
                var first = catcher.All[i].Split('\n')[0];
                if (first.Contains(" | ") || first.Contains("catalog is building"))
                {
                    return catcher.All[i].Replace("\r", "");
                }
            }
            return string.Join("\n", catcher.All.ToArray()).Replace("\r", "");
        }
    }

    // Lines of the page from the line holding start up to the next line holding one of the stops.
    private static string PageBlock(string page, string start, params string[] stops)
    {
        var lines = (page ?? "").Split('\n');
        var from = Array.FindIndex(lines, l => l.Contains(start));
        if (from < 0)
        {
            return "";
        }
        var to = lines.Length;
        for (var i = from + 1; i < lines.Length; i++)
        {
            if (stops.Any(s => lines[i].Contains(s)))
            {
                to = i;
                break;
            }
        }
        return string.Join("\n", lines, from, to - from);
    }

    private static IEnumerator RunEncyclopedia()
    {
        if (!WorldReady(EncyclopediaName) || !NeedOther(EncyclopediaName, EncyclopediaGuid, "Encyclopedia", "X03"))
        {
            yield break;
        }
        var c = new Checks(EncyclopediaName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, EncyclopediaName);
        var knownMaterials = new HashSet<string>(player.m_knownMaterial);
        var knownRecipes = new HashSet<string>(player.m_knownRecipes);
        var knownStations = new HashSet<string>(player.m_knownStations.Keys);
        try
        {
            UseDefaultRules();
            rig.Noon();
            rig.TakeControls();
            if (!c.Check(Terminal.commands.ContainsKey(EncyclopediaCommand) && Console.instance != null, "the Encyclopedia's page command is there (Debug build)"))
            {
                c.Report();
                yield break;
            }
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var name = rasp.ItemDisplayName;
            var rules = new CultivatorRules();
            var costs = new[] { rules.BlackMetalLevel, rules.EitrLevel, rules.FlametalLevel, rules.BloodgoldLevel };
            // "After having a transplant": the things the pages name are known to this character.
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            rig.Give(rasp.ItemName, 1);
            foreach (var cost in costs.SelectMany(CostOf))
            {
                rig.Give(cost.Key, 1);
            }
            yield return Hold(rig, tool);
            player.UpdateKnownRecipesList();
            yield return Frames(3);

            // Catalog may still be building right after the first ask.
            var itemPage = "";
            var until = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < until)
            {
                itemPage = EncyclopediaPage(rasp.ItemName);
                if (itemPage.Length > 0 && !itemPage.Contains("catalog is building"))
                {
                    break;
                }
                yield return new WaitForSecondsRealtime(1f);
            }
            var itemPrefab = TransplantContent.ItemPrefab(rasp);
            var description = itemPrefab != null ? L(itemPrefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_description) : "";
            var firstSentence = description.Split('\n').Select(s => s.Trim()).FirstOrDefault(s => s.Length > 0) ?? "";
            var head = itemPage.Split('\n')[0];
            c.Note($"transplant page starts '{head}'");
            c.Check(head.StartsWith(name + " | " + L("$hud_materials"), StringComparison.Ordinal),
                $"the transplant is an entry of the Materials tab with its name ('{head}')");
            c.Check(!itemPage.Contains("Not discovered yet.") && firstSentence.Length > 0 && itemPage.Contains(firstSentence),
                $"its page shows the item's description ('{firstSentence}')");

            // Young transplant among the cultivator's pieces.
            var piecePage = EncyclopediaPage(rasp.SaplingName);
            var pieceHead = piecePage.Split('\n')[0];
            var cultivator = L(PlantCatalog.CultivatorItemName);
            c.Check(pieceHead.StartsWith(name + " | " + L("$hud_building"), StringComparison.Ordinal), $"the young transplant is an entry of the Building tab ('{pieceHead}')");
            c.Check(piecePage.Contains("Built with " + cultivator) && !piecePage.Contains("Not discovered yet."), $"and it is one of the {cultivator}'s pieces ('Built with {cultivator}')");

            // Cultivator page: levels 4 to 7, their materials, Forge level, no idol at an upgrade station.
            // The Encyclopedia names a station only once the character has seen it (else "???"): this character has
            // seen the Forge, like anyone who upgrades a cultivator (known stations put back at the end).
            var toolRecipe = CultivatorTiers.CultivatorRecipe;
            var forgeStation = toolRecipe != null ? toolRecipe.GetRequiredStation(PlantCatalog.FirstNewTier) : null;
            var forgeToken = forgeStation != null ? forgeStation.m_name : "";
            var forgeName = L(forgeToken);
            c.Check(forgeToken == "$piece_forge" && forgeName.Length > 0, $"the cultivator's upgrades are made at the Forge (station '{forgeToken}', shown as '{forgeName}')");
            if (forgeToken.Length > 0 && !player.m_knownStations.ContainsKey(forgeToken))
            {
                player.m_knownStations[forgeToken] = 1;
            }
            var toolPage = EncyclopediaPage(PlantCatalog.CultivatorPrefab);
            c.Check(toolPage.StartsWith(cultivator + " | ", StringComparison.Ordinal), $"Cultivator page found ('{toolPage.Split('\n')[0]}')");
            for (var q = PlantCatalog.FirstNewTier; q <= PlantCatalog.MaxTier; q++)
            {
                var block = PageBlock(toolPage, "Quality " + q, "Quality " + (q + 1), "Beyond quality", "[Header]");
                var station = Regex.Match(block, "Quality " + q + @" \((?<station>.+) level " + q + @"\)");
                // The Encyclopedia writes amounts as "name [times sign]5" (char 0xD7; this file stays plain ASCII).
                var times = " " + (char)0xD7;
                var wants = CostOf(costs[q - PlantCatalog.FirstNewTier]).Select(k => L(SharedName(k.Key)) + times + k.Value).ToArray();
                c.Check(station.Success && station.Groups["station"].Value == forgeName,
                    $"Cultivator page: level {q} at the {forgeName} of level {q} ('{OneLine(block)}')");
                c.Check(wants.Length > 0 && wants.All(w => block.Contains(w)), $"Cultivator page: level {q} costs {string.Join(", ", wants)}");
            }
            c.Check(!toolPage.Contains("At an upgrade station:"), "Cultivator page: no level shows an idol at an upgrade station");
            c.Note("Cultivator page " + (toolPage.Contains("Beyond quality") ? "still has" : "does not have") + " the known line 'Beyond quality 7: at an upgrade station only'");

            // Black metal: used in the Cultivator.
            var metalPage = EncyclopediaPage("BlackMetal");
            var usedIn = PageBlock(metalPage, "Used in", "[Header]");
            c.Check(usedIn.Split('\n').Skip(1).Any(l => l.Contains("] " + cultivator)),
                $"Black metal page: 'Used in' lists the {cultivator} ('{OneLine(usedIn)}')");
            c.Report();
        }
        finally
        {
            rig.Restore();
            // What the test taught the character goes again.
            player.m_knownMaterial.RemoveWhere(m => !knownMaterials.Contains(m));
            player.m_knownRecipes.RemoveWhere(r => !knownRecipes.Contains(r));
            foreach (var seen in player.m_knownStations.Keys.Where(s => !knownStations.Contains(s)).ToList())
            {
                player.m_knownStations.Remove(seen);
            }
        }
    }

    // ---------- replant.x.lootfilter (X04) ----------

    private static IEnumerator RunLootFilter()
    {
        if (!WorldReady(LootFilterName) || !NeedOther(LootFilterName, LootGuid, "Loot Pickup Filter", "X04"))
        {
            yield break;
        }
        var c = new Checks(LootFilterName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, LootFilterName);
        var taken = new List<Vector3>();
        var data = player.m_customData;
        var modeKey = LootGuid + ".Mode";
        var selectedKey = LootGuid + ".Selected";
        var lootKeys = new[] { LootGuid + ".Version", modeKey, LootGuid + ".Ignored", selectedKey };
        var saved = lootKeys.Where(k => data.ContainsKey(k)).ToDictionary(k => k, k => data[k]);
        try
        {
            if (!c.Check(Terminal.commands.ContainsKey(LootCommand) && Console.instance != null, "the loot filter console command is there")
                || !c.Check(Text(OtherSetting(LootGuid, "Filter", "ExemptHarvest")) == "True", "Loot Pickup Filter's ExemptHarvest setting is on (its default): hand harvests pass the filter"))
            {
                c.Report();
                yield break;
            }
            PrepareInput(rig);
            // Filter back as it was: mode and Selected list through its own command, then the saved character data
            // exactly as before (a character that never used the filter has none).
            rig.Undo(() =>
            {
                var mode = saved.TryGetValue(modeKey, out var m) ? m : "Everything";
                RunConsole(LootCommand + " mode " + (mode == "OnlySelected" ? "only" : mode == "SkipIgnored" ? "skip" : "everything"));
                if (saved.Count == 0)
                {
                    RunConsole(LootCommand + " defaults");
                    foreach (var key in lootKeys)
                    {
                        data.Remove(key);
                    }
                    return;
                }
                RunConsole(LootCommand + " clear selected");
                foreach (var item in (saved.TryGetValue(selectedKey, out var list) ? list : "").Split(','))
                {
                    if (item.Trim().Length > 0)
                    {
                        RunConsole(LootCommand + "_select " + item.Trim());
                    }
                }
            });
            RunConsole(LootCommand + " mode only");
            RunConsole(LootCommand + " clear selected");
            yield return Frames(2);
            if (!c.Check(data.TryGetValue(modeKey, out var modeNow) && modeNow == "OnlySelected" && data.TryGetValue(selectedKey, out var selectedNow) && selectedNow == "",
                    "Loot Pickup Filter is on 'Only selected' with an empty list"))
            {
                c.Report();
                yield break;
            }
            Player.m_enableAutoPickup = true;
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var name = rasp.ItemDisplayName;
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            yield return Hold(rig, tool);
            var bush = new Aimed();
            yield return SpawnInReach(rig, taken, "RaspberryBush", 1.2f, bush);
            var pick = bush.Go != null ? bush.Go.GetComponent<Pickable>() : null;
            if (!c.Check(tool != null && bush.Seen && pick != null && pick.CanBePicked(), "a ripe raspberry bush is under the crosshair within reach" + bush.Why))
            {
                c.Report();
                yield break;
            }
            var berry = pick.m_itemPrefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
            var want = PickAmount(pick);
            var berries = CountByName(rig.Inv, berry);
            var woodName = SharedName("Wood");
            var wood = CountByName(rig.Inv, woodName);

            // Control: a plain item lying behind the bush before the dig. The filter must leave it there.
            var away = bush.Spot - player.transform.position;
            away.y = 0f;
            var woodAt = bush.Spot + away.normalized * 0.8f;
            woodAt.y = Ground(woodAt) + 0.5f;
            var woodPrefab = ObjectDB.instance.GetItemPrefab("Wood");
            var woodGo = woodPrefab != null ? Object.Instantiate(woodPrefab, woodAt, Quaternion.identity) : null;
            var woodDrop = woodGo != null ? woodGo.GetComponent<ItemDrop>() : null;
            if (!c.Check(woodDrop != null, "a piece of Wood lies behind the bush (control)"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(1f);

            yield return rig.AimAt(bush.AimAt);
            yield return ReadyForKey(player);
            yield return Tap(UseAndSnap);
            yield return WaitGone(bush.View, 1.5f);
            c.Check(!Alive(bush.View) && CountByName(rig.Inv, name) == 1, $"E digs the bush up: the transplant goes to the inventory ({CountByName(rig.Inv, name)})");
            yield return new WaitForSeconds(0.6f);
            var lying = StackSum(DropsNear(bush.Spot, berry, 3f));
            c.Check(lying + CountByName(rig.Inv, berry) - berries == want, $"the dig picked the berries first ({lying} on the ground, {want} from one pick)");

            // Walk onto the spot: auto pickup takes the berries (a hand harvest), not the Wood.
            MoveTo(rig, bush.Spot + Vector3.up * 0.1f);
            yield return WaitFor(() => CountByName(rig.Inv, berry) - berries >= want, 6f);
            yield return new WaitForSeconds(1.5f);
            c.Check(CountByName(rig.Inv, berry) - berries == want, $"the raspberries were picked up by auto pickup ({CountByName(rig.Inv, berry) - berries} of {want})");
            var woodNear = woodGo != null ? Vector3.Distance(woodGo.transform.position, player.transform.position + Vector3.up) : -1f;
            c.Check(woodGo != null && CountByName(rig.Inv, woodName) == wood && woodNear >= 0f && woodNear < player.m_autoPickupRange,
                $"control: the Wood within auto pickup reach ({F(woodNear)} m of {F(player.m_autoPickupRange)} m) was left on the ground by the filter");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- replant.x.spyglass-view (X05) ----------

    private static float CameraFov()
    {
        var cam = GameCamera.instance;
        return cam != null && cam.m_camera != null ? cam.m_camera.fieldOfView : 0f;
    }

    private static float Zoom(float baseFov, float fov) =>
        fov > 0f ? Mathf.Tan(baseFov * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) : 0f;

    private static IEnumerator RunSpyglassView()
    {
        if (!WorldReady(SpyglassViewName) || !NeedOther(SpyglassViewName, SpyglassGuid, "Spyglass", "X05"))
        {
            yield break;
        }
        var c = new Checks(SpyglassViewName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, SpyglassViewName);
        var taken = new List<Vector3>();
        var raisedKey = (SpyglassGuid + ".Raised").GetStableHashCode();
        var overlayName = SpyglassGuid + ".Overlay";
        bool RaisedFlag() => player.m_nview != null && player.m_nview.IsValid() && player.m_nview.GetZDO().GetBool(raisedKey);
        bool Overlay() => GameObject.Find(overlayName) != null;
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
            // Control (player controller still off: no stray mouse moves the view): the bush still is under the game's
            // own hover ray (the one Replant reads), so "no hint" is about the spyglass in hand, not about looking
            // somewhere else.
            c.Check(ReferenceEquals(player.GetRightItem(), glass) && ReferenceEquals(UnderCrosshair(player), bush.View)
                    && !Replant.Target.IsFresh && !HintText().Contains("Replant"),
                $"spyglass in hand, the bush under the crosshair ({RayNote(player)}): no Replant target, no hint ('{OneLine(HintText())}')");
            // The spyglass reads the Attack click from the player controller (off since PrepareInput): on again.
            ControllerOn(rig);
            yield return new WaitForSeconds(0.3f);
            yield return ReadyForKey(player);
            yield return new WaitForEndOfFrame();
            var baseFov = CameraFov();
            c.Check(ReferenceEquals(player.GetRightItem(), glass) && !RaisedFlag() && !Overlay() && baseFov > 1f,
                $"spyglass in hand and down: plain view ({F(baseFov)} degrees), no overlay");

            // Raise it: Attack held (works with HoldToLook on or off).
            PressKey("Attack");
            yield return WaitFor(() => RaisedFlag() && Overlay() && Zoom(baseFov, CameraFov()) >= 1.4f, 3f);
            yield return new WaitForSeconds(0.8f);
            var hinted = false;
            for (var i = 0; i < 12; i++)
            {
                yield return new WaitForEndOfFrame();
                hinted |= Replant.Target.IsFresh || HintText().Contains("Replant");
            }
            var zoom = Zoom(baseFov, CameraFov());
            c.Check(RaisedFlag(), "spyglass raised (its flag on the player)");
            c.Check(zoom >= 1.4f && Overlay(), $"the spyglass view as usual: zoomed {F(zoom)} times ({F(CameraFov())} of {F(baseFov)} degrees) with its round overlay shown ({Overlay()})");
            c.Check(!hinted && Alive(bush.View), $"spyglass raised at the bush: no Replant target, no hint ('{OneLine(HintText())}')");

            // Lower it: Attack let go, Block pressed.
            LetGo("Attack");
            yield return Frames(3);
            yield return Tap("Block");
            yield return WaitFor(() => !RaisedFlag() && !Overlay(), 3f);
            yield return new WaitForSeconds(0.3f);
            yield return new WaitForEndOfFrame();
            c.Check(!RaisedFlag() && !Overlay() && Mathf.Abs(CameraFov() - baseFov) < 1f, $"lowered: plain view again ({F(CameraFov())} degrees)");

            // Back to the cultivator: hint back, E digs.
            var count = CountByName(rig.Inv, rasp.ItemDisplayName);
            yield return Hold(rig, tool);
            yield return rig.AimAt(bush.AimAt);
            yield return Frames(4);
            c.Check(Replant.Target.IsFresh && HintText().Contains("Replant"), $"back to the cultivator: the hint is back ('{OneLine(HintText())}')");
            yield return ReadyForKey(player);
            yield return Tap(UseAndSnap);
            yield return WaitGone(bush.View, 1.5f);
            c.Check(!Alive(bush.View) && CountByName(rig.Inv, rasp.ItemDisplayName) == count + 1, "and E digs the bush up");
            c.Report();
        }
        finally
        {
            LetGo("Attack");
            rig.Restore();
        }
    }

    // ---------- PlantEasily (other author's mod, the tester's own settings) ----------

    // PlantEasily there, turned on, with a grid of more than one plant? No = FAIL, false.
    private static bool NeedPlantEasily(string test, string item, out int rows, out int columns)
    {
        rows = 0;
        columns = 0;
        if (!PlantEasilyLoaded)
        {
            SelfTest.Fail(test, $"PlantEasily is not installed: nothing of TESTING.md {item} was checked");
            return false;
        }
        var active = Text(OtherSetting(PlantEasilyId, "General", "ModActive"));
        int.TryParse(Text(OtherSetting(PlantEasilyId, "General", "Rows")), NumberStyles.Integer, CultureInfo.InvariantCulture, out rows);
        int.TryParse(Text(OtherSetting(PlantEasilyId, "General", "Columns")), NumberStyles.Integer, CultureInfo.InvariantCulture, out columns);
        if (active != "True" || rows * columns < 2)
        {
            SelfTest.Fail(test, $"PlantEasily is turned off or its grid is one plant in its settings (ModActive {active}, {rows} x {columns}): its grid cannot be checked ({item})");
            return false;
        }
        return true;
    }

    // New young plants of this kind since before, once PlantEasily's planting (a few per frame) has stood still for a
    // second. All tracked by the Rig.
    private static IEnumerator NewSaplings(Rig rig, PlantKind kind, HashSet<ZNetView> before, List<ZNetView> made, float seconds)
    {
        var hashes = new[] { kind.SaplingHash };
        var until = Time.time + seconds;
        var last = -1;
        var stillSince = Time.time;
        while (Time.time < until)
        {
            var now = LoadedOf(hashes).Count(v => !before.Contains(v));
            if (now != last)
            {
                last = now;
                stillSince = Time.time;
            }
            else if (Time.time - stillSince > 1f)
            {
                break;
            }
            yield return null;
        }
        made.Clear();
        foreach (var view in LoadedOf(hashes))
        {
            if (!before.Contains(view))
            {
                made.Add(view);
                rig.Track(view.gameObject);
            }
        }
    }

    private static List<string> ErrorsAbout(LineCatcher log, params string[] words)
    {
        lock (log.All)
        {
            return log.Errors.Where(e => words.Any(w => e.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
        }
    }

    private static IEnumerator RunEasyGrid()
    {
        if (!WorldReady(EasyGridName) || !NeedPlantEasily(EasyGridName, "E02", out var rows, out var columns))
        {
            yield break;
        }
        var c = new Checks(EasyGridName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, EasyGridName);
        var taken = new List<Vector3>();
        var log = new LineCatcher();
        BepInEx.Logging.Logger.Listeners.Add(log);
        try
        {
            PrepareInput(rig);
            yield return GrowReady(rig, c);
            // PrepareInput took PlantEasily's placement patches off: this test wants them on.
            var back = PutPlantEasilyBack(rig.Name);
            // No stamina use (the probe world's own state; PrepareInput's undo puts the rate back): PlantEasily stops
            // a grid when the stamina runs out, and a whole grid is wanted here.
            Game.m_staminaRate = 0f;
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var name = rasp.ItemDisplayName;
            var piece = SaplingPiece(rasp);
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            var need = Mathf.Min(rows * columns + 5, 120);
            for (var i = 0; i < 8 && CountByName(rig.Inv, name) < need; i++)
            {
                rig.Give(rasp.ItemName, 20);
            }
            var carried = CountByName(rig.Inv, name);
            if (!c.Check(tool != null && piece != null && carried >= need, $"cultivator and {carried} raspberry transplants given (grid {rows} x {columns}, {back} PlantEasily patches on)")
                || !c.Check(FindSpot(rig.Origin, Vector3.forward, new[] { 14f, 18f, 22f, 26f, 30f, 34f, 38f }, 3.5f, taken, out var spot, p => StandOk(rig.Origin, p) && Buildable(p, 10f)),
                    "open ground for a grid, clear of no-build zones"))
            {
                c.Report();
                yield break;
            }
            yield return Hold(rig, tool);
            var placing = new Placing();
            yield return AimGhost(rig, piece, spot, placing);
            yield return Frames(10);
            c.Check(player.m_placementStatus == Player.PlacementStatus.Valid, $"PlantEasily on: the aimed raspberry transplant ghost can be placed (status {player.m_placementStatus})");
            var before = LoadedOf(new[] { rasp.SaplingHash });
            yield return ReadyForKey(player);
            yield return Tap("Attack");
            var made = new List<ZNetView>();
            yield return NewSaplings(rig, rasp, before, made, 12f);
            var used = carried - CountByName(rig.Inv, name);
            c.Note($"one press: {made.Count} young transplant(s) planted, {used} transplant(s) used");
            if (!c.Check(made.Count >= 2, $"one press plants a grid ({made.Count} young transplants; one only = PlantEasily's grid did not plant)"))
            {
                c.Report();
                yield break;
            }
            c.Check(used == made.Count, $"a grid costs one transplant per plant ({used} used for {made.Count} planted)");

            // They all grow: their clock put past the grow time, vanilla does the rest.
            var places = made.Select(v => v.transform.position).ToList();
            TransplantContent.GrowTimes(rasp, ServerRules.Current.GrowTimeMultiplier, out _, out var high);
            foreach (var view in made)
            {
                Age(view, high + 200.0);
            }
            var grownHashes = GrownHashes(rasp).ToArray();
            var until = Time.time + 28f;
            while (Time.time < until && made.Any(Alive))
            {
                yield return new WaitForSeconds(0.5f);
            }
            yield return Frames(2);
            var grown = 0;
            foreach (var place in places)
            {
                var bush = FindNear(grownHashes, place, 0.8f);
                if (bush != null)
                {
                    grown++;
                    rig.Track(bush.gameObject);
                }
            }
            c.Check(grown == places.Count && !made.Any(Alive), $"they all grow into raspberry bushes ({grown} of {places.Count}, {made.Count(Alive)} still young)");

            // d: the last transplant of the kind planted while it is selected.
            TakeBack(rig.Inv, name, CountByName(rig.Inv, name) - 1);
            var lastSpot = Vector3.zero;
            if (c.Check(CountByName(rig.Inv, name) == 1 && FindSpot(rig.Origin, Vector3.back, new[] { 12f, 16f, 20f, 24f, 28f }, 3f, taken, out lastSpot, p => StandOk(rig.Origin, p) && Buildable(p, 6f)),
                    "one transplant left and a second open spot"))
            {
                yield return AimGhost(rig, piece, lastSpot, placing);
                yield return Frames(10);
                before = LoadedOf(new[] { rasp.SaplingHash });
                yield return ReadyForKey(player);
                yield return Tap("Attack");
                yield return NewSaplings(rig, rasp, before, made, 6f);
                yield return Frames(10);
                c.Check(made.Count == 1 && CountByName(rig.Inv, name) == 0, $"the last transplant plants one young plant ({made.Count} planted, {CountByName(rig.Inv, name)} left)");
            }
            var known = ErrorsAbout(log, "GhostGrid.GetGhostObject");
            c.Note(known.Count > 0
                ? $"PlantEasily logged its own known error {known.Count} time(s) (ArgumentOutOfRangeException ... GhostGrid.GetGhostObject)"
                : "PlantEasily did not log its 'GhostGrid.GetGhostObject' error this time");
            // e: no other error naming PlantEasily or this mod.
            var others = ErrorsAbout(log, "PlantEasily", typeof(SelfTests).Namespace, ModInfo.Guid).Where(e => e.IndexOf("GhostGrid.GetGhostObject", StringComparison.Ordinal) < 0).ToList();
            // Control: ears that hear nothing would say "no error" too. Planting writes log lines (the game's own
            // "Placed ..." for one): none heard = nothing was checked.
            int heard;
            lock (log.All)
            {
                heard = log.All.Count;
            }
            c.Check(heard > 0, $"the log was listened to while planting ({heard} line(s) of any source heard; none = the error check below checked nothing)");
            c.Check(others.Count == 0, others.Count == 0 ? "no other error line naming PlantEasily or this mod while planting" : $"{others.Count} error line(s): " + others[0].Substring(0, Math.Min(260, others[0].Length)));
            c.Report();
        }
        finally
        {
            BepInEx.Logging.Logger.Listeners.Remove(log);
            rig.Restore();
        }
    }

    private static IEnumerator RunEasyRoot()
    {
        if (!WorldReady(EasyRootName) || !NeedPlantEasily(EasyRootName, "E04 b and E02 b", out var rows, out var columns))
        {
            yield break;
        }
        var c = new Checks(EasyRootName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, EasyRootName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig);
            yield return GrowReady(rig, c);
            RootGate.ClearLedger();
            var back = PutPlantEasilyBack(rig.Name);
            // No stamina use, so the whole grid is planted (see replant.e.planteasily.grid).
            Game.m_staminaRate = 0f;
            var ygg = PlantCatalog.ByKey("YggaShoot");
            var name = ygg.ItemDisplayName;
            var piece = SaplingPiece(ygg);
            var origin = rig.Origin;
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 5);
            var need = Mathf.Min(rows * columns + 5, 120);
            for (var i = 0; i < 8 && CountByName(rig.Inv, name) < need; i++)
            {
                rig.Give(ygg.ItemName, 20);
            }
            var carried = CountByName(rig.Inv, name);
            if (!c.Check(tool != null && piece != null && carried >= need, $"level 5 cultivator and {carried} Yggdrasil transplants given (grid {rows} x {columns}, {back} PlantEasily patches on)")
                || !SpawnRoot(rig, c, taken, out _, out var root, out var rootSpot, p => Buildable(p, 10f)))
            {
                c.Report();
                yield break;
            }
            yield return Settle();
            var range = ServerRules.Current.RootRange;
            if (!c.Check(FindSpot(rootSpot, origin - rootSpot, new[] { 4.5f, 5f, 5.5f, 6f, 7f, 8f, 9f, 10f }, ygg.GrowRadius + 0.3f, taken, out var good,
                    p => RootGate.FindRoot(p, 5f) == root && RootGate.FindRoot(p, 3f) == null && StandOk(origin, p) && Buildable(p)), "ground spot 3 to 5 m from the root"))
            {
                c.Report();
                yield break;
            }
            yield return Hold(rig, tool);
            var placing = new Placing();
            yield return AimGhost(rig, piece, good, placing);
            yield return Frames(10);
            c.Check(player.m_placementStatus == Player.PlacementStatus.Valid, $"PlantEasily on, aimed 3 to 5 m from the root: the aimed ghost is green (status {player.m_placementStatus})");
            var ghostAt = player.m_placementGhost != null ? player.m_placementGhost.transform.position : good;
            var before = LoadedOf(new[] { ygg.SaplingHash });
            yield return ReadyForKey(player);
            yield return Tap("Attack");
            var plantedAt = Time.time;
            var made = new List<ZNetView>();
            yield return NewSaplings(rig, ygg, before, made, 12f);
            var used = carried - CountByName(rig.Inv, name);
            var aimed = made.FirstOrDefault(v => Utils.DistanceXZ(v.transform.position, ghostAt) < 1.2f);
            c.Note($"one press: {made.Count} young Yggdrasil transplant(s), {used} transplant(s) used");
            if (!c.Check(aimed != null, $"the aimed transplant is planted ({made.Count} planted in all)"))
            {
                c.Report();
                yield break;
            }
            c.Check(used == made.Count, $"one transplant per plant ({used} used for {made.Count} planted)");
            var inReach = made.Where(v => RootGate.FindRoot(v.transform.position, range) == root).ToList();
            var outside = made.Where(v => RootGate.FindRoot(v.transform.position, range) == null).ToList();
            // Hover text as the game shows it (Plant.GetHoverText, what the crosshair reads), not this mod's own line maker.
            string HoverOf(ZNetView v)
            {
                var plant = Alive(v) ? v.GetComponent<Plant>() : null;
                return plant != null ? plant.GetHoverText() ?? "" : "";
            }
            c.Check(inReach.Contains(aimed) && HoverOf(aimed).Contains("Draws "), $"the aimed one found the root (its hover says what it draws: '{OneLine(HoverOf(aimed))}')");
            if (c.Check(outside.Count > 0, $"grid copies farther than {F(range)} m from the root were planted ({outside.Count} of {made.Count}; none = the grid stayed inside the root's reach and E02 b is not checked)"))
            {
                var wantLine = $"Needs an Ancient Root within {F1(range)} m";
                var said = outside.Count(v => HoverOf(v).Contains(wantLine));
                c.Check(said == outside.Count, $"each of them says '{wantLine}' on hover ({said} of {outside.Count}; first: '{OneLine(HoverOf(outside[0]))}')");
                // They wait: clock put past the grow time once their first 10 s are over, one more growth pass.
                var level = root.GetLevel();
                yield return WaitFor(() => Time.time - plantedAt > 12f, 14f);
                TransplantContent.GrowTimes(ygg, ServerRules.Current.GrowTimeMultiplier, out _, out var high);
                foreach (var view in outside)
                {
                    Age(view, high + 200.0);
                }
                // Each young plant is looked at once per 10 s, each on its own beat: 11 s covers them all.
                yield return new WaitForSeconds(11f);
                c.Check(outside.All(Alive) && Near(root.GetLevel(), level, 0.5f), $"past their grow time they wait: {outside.Count(Alive)} of {outside.Count} still young, no sap taken");

                // Replant gives one back: the others go first so the crosshair finds it.
                var one = outside.OrderBy(v => (v.transform.position - origin).sqrMagnitude).First();
                foreach (var view in made.Where(v => !ReferenceEquals(v, one) && Alive(v)).ToList())
                {
                    rig.Destroy(view.gameObject);
                }
                yield return Settle();
                yield return Stand(rig, one.transform.position);
                yield return rig.AimAt(AimPoint(one.gameObject));
                yield return Frames(4);
                var count = CountByName(rig.Inv, name);
                var target = Replant.Target;
                if (c.Check(target.IsFresh && ReferenceEquals(target.View, one) && target.IsSapling, $"a waiting copy is under the crosshair ({RayNote(player)})"))
                {
                    yield return ReadyForKey(player);
                    yield return Tap(UseAndSnap);
                    yield return WaitGone(one, 1.5f);
                    c.Check(!Alive(one) && CountByName(rig.Inv, name) == count + 1, $"E gives it back ({count} -> {CountByName(rig.Inv, name)} transplants)");
                }
            }
            c.Report();
        }
        finally
        {
            RootGate.ClearLedger();
            rig.Restore();
        }
    }
}
#endif
