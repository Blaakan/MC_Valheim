using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MC.Shared;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;
#endif

namespace MC.Crafting.ForgeIdolUpgradesMod;

// Debug build only (calls vanish in Release). In-world self tests, run by world probe (tools/Test-InWorld.ps1):
//   forge.catalog  idol table: 16 idols max quality 4, every tier metal + 3 trophy pools resolve, level mapping
//   forge.icons    starred sprites (size, cache, gold pixel, PNG for eyes), GetIcon of a 3 star idol, hotbar slot
//                  (only GetIcon feed it: proves the patch reach an early compiled caller)
//   forge.tooltip  idol tooltip: level line replace vanilla quality line, chance, next cost
//   forge.idols    Forge of Potential in front of player, Idols tab: rows, cost slots, upgrade 0->1 (stack of 3),
//                  1->2 and 2->3 in place (stack of 1), 3 stars not listed, back to Upgrade tab
//   forge.refine   refinement at the Forge with forced rolls: failure lose 1 level (default), destroy rule break
//                  the item and give materials back, idol spent always,
//                  success raise item level in place (custom data kept), idol level pick (default + click cycle)
// Me take back every item me give, forget recipes me teach, close the window, destroy the Forge, reset forced roll.
internal static class SelfTests
{
    private const string CatalogName = "forge.catalog";
    private const string IconsName = "forge.icons";
    private const string TooltipName = "forge.tooltip";
    private const string IdolsName = "forge.idols";
    private const string RefineName = "forge.refine";
    private const string PopupName = "forge.popup";

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        SelfTest.Register(CatalogName, RunCatalog);
        SelfTest.Register(IconsName, RunIcons);
        SelfTest.Register(TooltipName, RunTooltip);
        SelfTest.Register(IdolsName, RunIdols);
        SelfTest.Register(RefineName, RunRefine);
        SelfTest.Register(PopupName, RunPopup);
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        SelfTest.Unregister(CatalogName);
        SelfTest.Unregister(IconsName);
        SelfTest.Unregister(TooltipName);
        SelfTest.Unregister(IdolsName);
        SelfTest.Unregister(RefineName);
        SelfTest.Unregister(PopupName);
        ForgeRefine.TestRoll = null;
#endif
    }

#if DEBUG
    private sealed class Checks
    {
        private readonly string _name;
        private readonly List<string> _failures = new List<string>();
        private int _count;

        internal Checks(string name) => _name = name;

        internal void Check(bool ok, string what)
        {
            _count++;
            if (!ok)
            {
                _failures.Add(what);
            }
        }

        internal void Report(string extra = "")
        {
            if (_failures.Count == 0)
            {
                SelfTest.Pass(_name, $"{_count} checks OK{extra}");
            }
            else
            {
                SelfTest.Fail(_name, $"{_failures.Count} of {_count} checks failed: {string.Join("; ", _failures.ToArray())}");
            }
        }
    }

    // Items me gave, taken back in finally (by name + quality, whatever stack they joined).
    private sealed class Given
    {
        private readonly List<KeyValuePair<string, int>> _items = new List<KeyValuePair<string, int>>();

        internal ItemDrop.ItemData Add(string prefab, int amount, int quality = 1)
        {
            var inventory = Player.m_localPlayer.GetInventory();
            var item = inventory.AddItem(prefab, amount, quality, 0, 0L, "", false);
            var drop = ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>();
            if (drop != null)
            {
                _items.Add(new KeyValuePair<string, int>(drop.m_itemData.m_shared.m_name, -1));
            }
            return item;
        }

        internal void TakeBack()
        {
            var inventory = Player.m_localPlayer.GetInventory();
            foreach (var pair in _items)
            {
                var n = inventory.CountItems(pair.Key, -1, false);
                if (n > 0)
                {
                    inventory.RemoveItem(pair.Key, n, -1, false);
                }
            }
            _items.Clear();
        }
    }

    private static ItemDrop Prefab(string name) => ObjectDB.instance.GetItemPrefab(name)?.GetComponent<ItemDrop>();

    // ---------- forge.catalog ----------

    private static IEnumerator RunCatalog()
    {
        var c = new Checks(CatalogName);
        var idols = IdolCatalog.AllIdols().ToList();
        c.Check(idols.Count == 16, $"16 idols expected, found {idols.Count}");
        foreach (var idol in idols)
        {
            c.Check(idol.Prefab.m_itemData.m_shared.m_maxQuality == 4, $"{idol.PrefabName} max quality must be 4, is {idol.Prefab.m_itemData.m_shared.m_maxQuality}");
            var tier = IdolCatalog.TierOf(idol);
            c.Check(tier != null && tier.Material != null, $"{idol.PrefabName} tier {idol.Tier} must have a metal");
            for (var level = 1; level <= IdolLevels.Max && tier != null; level++)
            {
                c.Check(tier.Pools[level] != null && tier.Pools[level].Names.Count > 0, $"tier {idol.Tier} level {level} trophy pool empty");
            }
        }
        // Default lists: every name resolved (the config holds the defaults in a fresh test run).
        foreach (var d in IdolTierDefaults.All)
        {
            foreach (var list in new[] { d.Base, d.Elite, d.Boss })
            {
                foreach (var name in list.Split(','))
                {
                    c.Check(Prefab(name.Trim()) != null, $"default trophy '{name.Trim()}' (tier {d.Tier}) not in game");
                }
            }
            c.Check(Prefab(d.Material) != null, $"default metal '{d.Material}' (tier {d.Tier}) not in game");
        }
        // Forest and Frost troll trophies share one item name: counted as one kind.
        var bronze = IdolCatalog.TierOf(idols.FirstOrDefault(i => i.Tier == 1));
        c.Check(bronze != null && bronze.Pools[2].Names.Contains("$item_trophy_troll"), "bronze elite pool must hold the troll trophy");
        c.Check(IdolLevels.Of(new ItemDrop.ItemData { m_quality = 1 }) == 0 && IdolLevels.Of(new ItemDrop.ItemData { m_quality = 4 }) == 3
                && IdolLevels.Of(new ItemDrop.ItemData { m_quality = 7 }) == 3, "quality 1 -> level 0, 4 -> 3, above clamped");

        // Server rules on the wire: round trip, clamping, unknown layout refused.
        var own = ForgeRules.Own();
        var pkg = new ZPackage();
        own.Write(pkg);
        pkg.SetPos(0);
        c.Check(ForgeRules.TryRead(pkg, out var back, out var clamped) && !clamped && back.Describe() == own.Describe()
                && back.SameTiers(own) && back.LevelsLost == own.LevelsLost && back.Failure == own.Failure,
            "rules survive the wire unchanged");
        var wild = ForgeRules.Own();
        wild.Chance[2] = 150;
        wild.LevelsLost = 0;
        pkg = new ZPackage();
        wild.Write(pkg);
        pkg.SetPos(0);
        c.Check(ForgeRules.TryRead(pkg, out back, out clamped) && clamped && back.Chance[2] == 100 && back.LevelsLost == 1,
            "out-of-range rules from the wire are clamped");
        pkg = new ZPackage();
        pkg.Write(ForgeRules.Layout + 1);
        pkg.SetPos(0);
        c.Check(!ForgeRules.TryRead(pkg, out _, out _), "unknown rules layout refused");
        c.Check(ServerRules.Receive(new ZPackage()) == false, "single player never takes rules from a peer");
        c.Check(PlayerCheck.Decide(true, true, true, false, false, false) == JoinVerdict.Refuse
                && PlayerCheck.Decide(true, true, true, false, false, true) == JoinVerdict.Allowed
                && PlayerCheck.Decide(true, true, true, false, true, false) == JoinVerdict.HasMod
                && PlayerCheck.Decide(false, true, true, false, false, false) == JoinVerdict.Skip,
            "join check: no mod refused (or allowed by setting), mod fine, only the server acts");
        c.Report($"; chances {IdolLevels.ChancePercent(0)}/{IdolLevels.ChancePercent(1)}/{IdolLevels.ChancePercent(2)}/{IdolLevels.ChancePercent(3)}%");
        yield break;
    }

    // ---------- forge.icons ----------

    private static IEnumerator RunIcons()
    {
        var c = new Checks(IconsName);
        var given = new Given();
        var keep = StarIcons.KeepReadable;
        try
        {
            StarIcons.Clear();
            StarIcons.KeepReadable = true;
            var prefab = Prefab("Upgrader3Weapon");
            var icon = prefab.m_itemData.m_shared.m_icons[0];
            SelfTest.Note(IconsName, $"color space {QualitySettings.activeColorSpace}, atlas sRGB {icon.texture.isDataSRGB}, "
                                     + $"atlas {icon.texture.width}x{icon.texture.height} {icon.texture.graphicsFormat}");
            for (var stars = 1; stars <= 3; stars++)
            {
                var made = StarIcons.Get(icon, stars);
                c.Check(made != icon && StarIcons.IsStarSprite(made), $"{stars} star sprite must be a new sprite");
                c.Check(StarIcons.Get(icon, stars) == made, $"{stars} star sprite must come from cache the 2nd time");
                c.Check(Mathf.Approximately(made.rect.width, icon.rect.width) && Mathf.Approximately(made.rect.height, icon.rect.height)
                        && Mathf.Approximately(made.pixelsPerUnit, icon.pixelsPerUnit), $"{stars} star sprite must keep size and ppu");
                var tex = made.texture;
                if (tex.isReadable)
                {
                    // Rightmost star centre, near top right (see StarIcons.DrawStars): gold, opaque.
                    var w = tex.width;
                    var h = tex.height;
                    var outer = Mathf.Max(4f, w * 0.12f);
                    var rim = Mathf.Max(1f, outer * 0.22f);
                    var px = tex.GetPixel(Mathf.RoundToInt(w - 1f - rim - outer * 0.951f), Mathf.RoundToInt(h - 1f - rim - outer));
                    c.Check(px.a > 0.95f && px.r > 0.7f && px.g > 0.35f && px.b < 0.5f, $"{stars} star: star centre must be gold, is {px}");
                    // Middle of icon: copied from the atlas, not empty.
                    var mid = tex.GetPixel(w / 2, h / 3);
                    c.Check(mid.a > 0.1f, $"{stars} star: icon middle must not be empty (atlas copy), is {mid}");
                    var dir = Path.GetDirectoryName(SelfTest.ShotPath(IconsName, "x") ?? Path.Combine(Path.GetTempPath(), "x"));
                    File.WriteAllBytes(Path.Combine(dir, $"forge.icons__upgrader3weapon_{stars}stars.png"), ImageConversion.EncodeToPNG(tex));
                }
                else
                {
                    c.Check(false, "made texture must stay readable while KeepReadable");
                }
            }
            c.Check(StarIcons.Get(icon, 0) == icon, "0 stars = plain icon");

            // Real item: GetIcon patched. Hotbar slot 3 (x=2, y=0) only use GetIcon (no patch of me there).
            var inventory = Player.m_localPlayer.GetInventory();
            var idol = given.Add("Upgrader3Weapon", 1, 4);
            c.Check(idol != null, "could not add a 3 star idol");
            if (idol != null)
            {
                var own = inventory.GetAllItems().FirstOrDefault(i => i.m_shared.m_name == idol.m_shared.m_name && i.m_quality == 4);
                c.Check(own != null && StarIcons.IsStarSprite(own.GetIcon()), "GetIcon of a 3 star idol must return the starred sprite");
                c.Check(own != null && own.m_shared.m_maxQuality == 4, "idol in inventory must have max quality 4 (its own copy)");
                if (own != null && inventory.GetItemAt(2, 0) == null)
                {
                    inventory.MoveItemToThis(inventory, own, own.m_stack, 2, 0);
                }
                yield return new WaitForSeconds(0.5f);
                var bar = Object.FindObjectsByType<HotkeyBar>(FindObjectsSortMode.None).FirstOrDefault();
                var element = bar != null && bar.m_elements.Count > 2 ? bar.m_elements[2] : null;
                c.Check(element != null && StarIcons.IsStarSprite(element.m_icon.sprite), "hotbar slot 3 must show the starred icon (GetIcon patch reach it)");
            }
            c.Report();
        }
        finally
        {
            given.TakeBack();
            StarIcons.KeepReadable = keep;
            StarIcons.Clear();
        }
    }

    // ---------- forge.popup ----------

    // Real pickup of a 2-star idol never seen before, fresh star cache (like a new session): the "added" message and
    // the "New material" unlock message must show the starred icon, not an empty square. Me check the message read is
    // the idol's own, not one other tests left in the HUD queue.
    private static IEnumerator RunPopup()
    {
        var c = new Checks(PopupName);
        var player = Player.m_localPlayer;
        var inventory = player.GetInventory();
        var prefab = Prefab("Upgrader3Weapon");
        var idolName = prefab.m_itemData.m_shared.m_name;
        var knew = player.m_knownMaterial.Contains(idolName);
        ItemDrop dropped = null;
        try
        {
            StarIcons.Clear();
            player.m_knownMaterial.Remove(idolName);
            var item = prefab.m_itemData.Clone();
            item.m_quality = 3;
            item.m_dropPrefab = prefab.gameObject;
            dropped = ItemDrop.DropItem(item, 1, player.transform.position + Vector3.up, Quaternion.identity);
            yield return null;

            // Tests before me leave HUD work waiting: top-left messages ("new item", "added"...; HUD show one per
            // second, so mine wait behind them and old icon stay on screen), unlock popups, text fades. Me throw it all
            // away in same frame as pickup and blank the screen: next top-left message and next unlock popup are mine.
            // Me put nothing back (old news of other tests).
            var hud = MessageHud.instance;
            hud.ClearUnlockQueue();
            hud.m_msgQeue.Clear();
            hud._crossFadeTextBuffer.Clear();
            hud.HideAll();
            hud.currentMsg = new MessageHud.MsgData(); // Nothing on screen: no merge with old message ("x2").
            hud.m_msgQueueTimer = 1f; // Screen idle long enough: next queued message show on next Update.
            c.Check(player.Pickup(dropped.gameObject, false, false), "pickup of the dropped 2-star idol");
            dropped = null;

            // Mine = the 'added' message this pickup queued. Me wait till HUD show that very message, then read icon.
            var addedText = Localization.instance.Localize("$msg_added " + idolName);
            var mine = hud.m_msgQeue.FirstOrDefault(m => m != null && m.m_text == addedText);
            var shownBy = Time.realtimeSinceStartup + 3f;
            while (mine != null && !ReferenceEquals(hud.currentMsg, mine) && Time.realtimeSinceStartup < shownBy)
            {
                yield return null;
            }
            var ownShown = mine != null && ReferenceEquals(hud.currentMsg, mine);
            c.Check(ownShown, mine == null
                ? $"the pickup queues the idol's own 'added' message '{addedText}'"
                : $"the 'added' message on screen is the idol's own, shows '{hud.m_messageText.text}'");
            var shownIcon = hud.m_messageIcon.sprite;
            SelfTest.Note(PopupName, $"message: '{hud.m_messageText.text}' (idol's own: {ownShown}); icon: {Describe(shownIcon)}; "
                                     + $"image type {hud.m_messageIcon.type}, mesh {hud.m_messageIcon.useSpriteMesh}, "
                                     + $"material {(hud.m_messageIcon.material != null ? hud.m_messageIcon.material.name : "none")}");
            c.Check(shownIcon != null && StarIcons.IsStarSprite(shownIcon), "the 'added' message shows the starred icon");
            SelfTest.Screenshot(PopupName, "added");
            yield return null;
            yield return null;

            // Unlock message comes a moment later (its own slot and animation).
            UnityEngine.UI.Image unlockIcon = null;
            var wanted = Localization.instance.Localize(idolName);
            var until = Time.realtimeSinceStartup + 6f;
            while (unlockIcon == null && Time.realtimeSinceStartup < until)
            {
                foreach (var msg in hud.m_unlockMessages)
                {
                    if (msg != null
                        && msg.transform.Find("UnlockMessage/UnlockDescription")?.GetComponent<TMPro.TMP_Text>() is { } text
                        && text.text == wanted)
                    {
                        unlockIcon = msg.transform.Find("UnlockMessage/icon_bkg/UnlockIcon")?.GetComponent<UnityEngine.UI.Image>();
                    }
                }
                yield return null;
            }
            c.Check(unlockIcon != null, $"a 'New material' unlock message for {wanted} appears");
            if (unlockIcon != null)
            {
                yield return new WaitForSecondsRealtime(0.8f);
                SelfTest.Note(PopupName, $"unlock icon: {Describe(unlockIcon.sprite)}; image type {unlockIcon.type}, "
                                         + $"mesh {unlockIcon.useSpriteMesh}, material {(unlockIcon.material != null ? unlockIcon.material.name : "none")}");
                c.Check(unlockIcon.sprite != null && StarIcons.IsStarSprite(unlockIcon.sprite), "the unlock message shows the starred icon");
                SelfTest.Screenshot(PopupName, "unlock");
                yield return null;
                yield return null;
            }
            c.Report();
        }
        finally
        {
            if (dropped != null)
            {
                ZNetScene.instance.Destroy(dropped.gameObject);
            }
            var n = inventory.CountItems(idolName, -1, false);
            if (n > 0)
            {
                inventory.RemoveItem(idolName, n, -1, false);
            }
            if (knew)
            {
                player.m_knownMaterial.Add(idolName);
            }
            else
            {
                player.m_knownMaterial.Remove(idolName);
            }
        }
    }

    private static string Describe(Sprite sprite)
    {
        if (ReferenceEquals(sprite, null))
        {
            return "none";
        }
        if (sprite == null)
        {
            return "destroyed sprite";
        }
        var tex = sprite.texture;
        return $"{sprite.name} ({(StarIcons.IsStarSprite(sprite) ? "starred" : "game")}), texture "
               + (tex == null ? "missing" : $"{tex.name} {tex.width}x{tex.height} {tex.graphicsFormat}");
    }

    // ---------- forge.tooltip ----------

    private static IEnumerator RunTooltip()
    {
        var c = new Checks(TooltipName);
        var prefab = Prefab("Upgrader3Weapon");
        var item = prefab.m_itemData.Clone();
        for (var q = 1; q <= 4; q++)
        {
            item.m_quality = q;
            var text = Localization.instance.Localize(item.GetTooltip());
            var level = q - 1;
            c.Check(text.Contains("Level: ") && text.Contains($"({level} of 3)"), $"quality {q}: tooltip must say level {level} of 3: '{text}'");
            c.Check(text.Contains(IdolLevels.ChancePercent(level) + "%"), $"quality {q}: tooltip must show {IdolLevels.ChancePercent(level)}%");
            c.Check(!text.Contains(Localization.instance.Localize("$item_quality") + ":"), $"quality {q}: vanilla quality line must be gone");
            c.Check(level < 3 ? text.Contains("Idols tab") : text.Contains("Maximum level"), $"quality {q}: next upgrade or max line");
        }
        item.m_quality = 1;
        var plain = Localization.instance.Localize(item.GetTooltip());
        c.Check(plain.Contains(Localization.instance.Localize("$item_silver")), $"plain silver idol tooltip must name its metal: '{plain}'");

        // Idol on the ground: hover says the stars, not the raw quality. (Prefab data clone has no drop prefab.)
        item.m_quality = 3;
        item.m_dropPrefab = prefab.gameObject;
        var player = Player.m_localPlayer;
        ItemDrop dropped = null;
        try
        {
            dropped = ItemDrop.DropItem(item, 1, player.transform.position + player.transform.forward * 2f + Vector3.up, Quaternion.identity);
            yield return null;
            var hover = dropped != null ? dropped.GetHoverText() : "";
            c.Check(hover.Contains("(2 stars)") && !hover.Contains("[3]"), $"ground hover of a 2-star idol must say (2 stars): '{hover}'");
        }
        finally
        {
            if (dropped != null)
            {
                ZNetScene.instance.Destroy(dropped.gameObject);
            }
        }
        c.Report();
    }

    // ---------- shared Forge setup ----------

    private sealed class Forge
    {
        internal GameObject Go;
        internal CraftingStation Station;

        internal static IEnumerator Open(Forge forge)
        {
            var player = Player.m_localPlayer;
            var prefab = ZNetScene.instance.GetPrefab("UpgradeStation");
            var forward = player.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            // 3 m away: the Forge is big, closer its collider push the player, and out of use range the game close
            // the window (Player.UpdateStations). Use range of this test copy made large for the same reason.
            var pos = player.transform.position + forward * 3f;
            pos.y = ZoneSystem.instance.GetGroundHeight(pos);
            forge.Go = Object.Instantiate(prefab, pos, Quaternion.LookRotation(-forward));
            yield return new WaitForSeconds(0.5f);
            forge.Station = forge.Go.GetComponentInChildren<CraftingStation>();
            forge.Station.m_useDistance = 50f;
            player.SetCraftingStation(forge.Station);
            InventoryGui.instance.Show(null, 3);
            yield return new WaitForSecondsRealtime(1f);
        }

        internal void Close()
        {
            var gui = InventoryGui.instance;
            if (gui != null && InventoryGui.IsVisible())
            {
                gui.Hide();
            }
            var player = Player.m_localPlayer;
            if (player != null)
            {
                player.SetCraftingStation(null);
            }
            if (Go != null)
            {
                ZNetScene.instance.Destroy(Go);
            }
        }
    }

    private static int RowOf(InventoryGui gui, ItemDrop.ItemData item)
    {
        for (var i = 0; i < gui.m_availableRecipes.Count; i++)
        {
            if (ReferenceEquals(gui.m_availableRecipes[i].ItemData, item))
            {
                return i;
            }
        }
        return -1;
    }

    // Press craft, then skip the 8-12 s timer: next UpdateRecipe run DoCrafting.
    private static IEnumerator PressAndFinish(InventoryGui gui)
    {
        gui.OnCraftPressed();
        yield return null;
        if (gui.m_craftTimer >= 0f)
        {
            gui.m_craftTimer = 1000f;
        }
        yield return null;
        yield return null;
    }

    private static string Slot(InventoryGui gui, int i, string child) =>
        gui.m_recipeRequirementList[i].transform.Find(child)?.GetComponent<TMP_Text>()?.text ?? "";

    private static bool SlotShown(InventoryGui gui, int i) =>
        gui.m_recipeRequirementList[i].transform.Find("res_icon")?.gameObject.activeSelf ?? false;

    // ---------- forge.idols ----------

    private static IEnumerator RunIdols()
    {
        var c = new Checks(IdolsName);
        var given = new Given();
        var forge = new Forge();
        var player = Player.m_localPlayer;
        var inventory = player.GetInventory();
        var gui = InventoryGui.instance;
        try
        {
            var idolName = Prefab("Upgrader3Weapon").m_itemData.m_shared.m_name;
            given.Add("Upgrader3Weapon", 3, 1);
            given.Add("Silver", 5);
            given.Add("TrophyWolf", 3);
            given.Add("TrophyUlv", 2);
            yield return Forge.Open(forge);

            var tab = gui.m_tabCraft.transform.parent.Find("MC_IdolsTab");
            c.Check(tab != null && tab.gameObject.activeSelf, "Idols tab must show at the Forge of Potential");
            c.Check(!gui.m_tabCraft.gameObject.activeSelf && gui.m_tabUpgrade.gameObject.activeSelf, "vanilla: Craft hidden, Upgrade shown at the Forge");
            c.Check(!IdolsTab.Mode && !gui.m_tabUpgrade.interactable, "Forge must open on the vanilla Upgrade tab");
            if (tab == null)
            {
                c.Report();
                yield break;
            }
            tab.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return null;
            c.Check(IdolsTab.Mode && gui.m_tabUpgrade.interactable && !tab.GetComponent<UnityEngine.UI.Button>().interactable,
                "click on Idols: Idols selected, Upgrade clickable");
            var stack = inventory.GetAllItems().FirstOrDefault(i => i.m_shared.m_name == idolName && i.m_quality == 1);
            var row = RowOf(gui, stack);
            c.Check(row >= 0, "Idols tab must list the plain silver idol stack");
            c.Check(row >= 0 && gui.m_availableRecipes[row].CanCraft, "silver idol row must be craftable (5 silver, 5 wolf/ulv trophies)");
            gui.SetRecipe(row, false);
            yield return null;
            yield return null;
            c.Check(SlotShown(gui, 0) && Slot(gui, 0, "res_amount") == "5", $"slot 1 = 5 silver, is '{Slot(gui, 0, "res_amount")}'");
            c.Check(SlotShown(gui, 1) && Slot(gui, 1, "res_amount") == "5" && Slot(gui, 1, "res_name").Contains("Common"),
                $"slot 2 = 5 common trophies, is '{Slot(gui, 1, "res_name")}' x '{Slot(gui, 1, "res_amount")}'");
            c.Check(SlotShown(gui, 2) && Slot(gui, 2, "res_amount") == "1", "slot 3 = the idol itself");
            c.Check(!SlotShown(gui, 3), "slot 4 empty");
            c.Check(gui.m_craftButton.interactable, "Upgrade idol button must be usable");
            c.Check(gui.m_itemCraftType.text.Contains("1 star"), $"craft type must say upgrade to 1 star, is '{gui.m_itemCraftType.text}'");
            SelfTest.Screenshot(IdolsName, "idols-tab");
            yield return null;
            yield return null;

            yield return PressAndFinish(gui);
            c.Check(inventory.CountItems(idolName, 1) == 2 && inventory.CountItems(idolName, 2) == 1, "0->1: two plain + one 1-star idol");
            c.Check(gui.m_craftRecipe == null && gui.m_craftUpgradeItem == null,
                "after an idol upgrade the craft snapshot is empty (another Forge mod's prefix finds nothing to remove)");
            c.Check(inventory.CountItems("$item_silver") == 0, "0->1: 5 silver used");
            c.Check(inventory.CountItems("$item_trophy_wolf") + inventory.CountItems("$item_trophy_ulv") == 0, "0->1: 5 trophies used, any mix");

            // 1->2 in place: stack of 1 keep its object and slot.
            given.Add("Silver", 10);
            given.Add("TrophyFenring", 3);
            gui.UpdateCraftingPanel();
            yield return null;
            var one = inventory.GetAllItems().FirstOrDefault(i => i.m_shared.m_name == idolName && i.m_quality == 2);
            var pos = one != null ? one.m_gridPos : new Vector2i(-1, -1);
            row = RowOf(gui, one);
            c.Check(row >= 0 && gui.m_availableRecipes[row].CanCraft, "1-star row craftable with 10 silver + 3 fenring");
            gui.SetRecipe(row, false);
            yield return null;
            yield return PressAndFinish(gui);
            c.Check(one != null && one.m_quality == 3 && one.m_gridPos.x == pos.x && one.m_gridPos.y == pos.y && inventory.ContainsItem(one),
                "1->2: same idol object upgraded in place");
            c.Check(inventory.CountItems("$item_silver") == 0 && inventory.CountItems("$item_trophy_fenring") == 0, "1->2: 10 silver + 3 elite used");

            // 2->3, boss trophy.
            given.Add("Silver", 15);
            given.Add("TrophyDragonQueen", 1);
            gui.UpdateCraftingPanel();
            yield return null;
            row = RowOf(gui, one);
            gui.SetRecipe(row, false);
            yield return null;
            yield return PressAndFinish(gui);
            c.Check(one != null && one.m_quality == 4, "2->3: idol has 3 stars");
            c.Check(RowOf(gui, one) < 0, "3 star idol no longer listed");
            c.Check(inventory.CountItems("$item_trophy_dragonqueen") == 0, "2->3: boss trophy used");

            // Missing cost: plain stack row not craftable, button off.
            var plainStack = inventory.GetAllItems().FirstOrDefault(i => i.m_shared.m_name == idolName && i.m_quality == 1);
            row = RowOf(gui, plainStack);
            c.Check(row >= 0 && !gui.m_availableRecipes[row].CanCraft, "plain idol row without metal/trophies not craftable");

            // Turning the mod off with an idol upgrade running (what OnDeactivated does first): timer cancelled,
            // vanilla rows, nothing of ours left for vanilla to take for a refinement.
            given.Add("Silver", 5);
            given.Add("TrophyWolf", 5);
            gui.UpdateCraftingPanel();
            yield return null;
            gui.SetRecipe(RowOf(gui, plainStack), false);
            yield return null;
            gui.OnCraftPressed();
            yield return null;
            c.Check(gui.m_craftTimer >= 0f && IdolUpgrade.IsOurs(gui.m_craftRecipe), "idol upgrade timer running before the off check");
            IdolsTab.BeforeOff();
            yield return null;
            c.Check(gui.m_craftTimer < 0f && gui.m_craftRecipe == null, "turning off cancels the running idol upgrade");
            c.Check(!IdolsTab.Mode && !gui.m_tabUpgrade.interactable, "turning off leaves the Idols tab (Upgrade selected)");
            c.Check(gui.m_availableRecipes.All(r => !IdolUpgrade.IsOurs(r.Recipe)) && !IdolUpgrade.IsOurs(gui.m_selectedRecipe.Recipe),
                "turning off leaves no idol row and no idol selection");
            c.Check(inventory.CountItems(idolName, 1) == 2 && inventory.CountItems("$item_silver") == 5, "turning off used nothing");
            tab.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return null;

            // Back to vanilla Upgrade tab.
            gui.OnTabUpgradePressed();
            yield return null;
            c.Check(!IdolsTab.Mode && !gui.m_tabUpgrade.interactable, "Upgrade tab click leave the Idols tab");
            c.Check(gui.m_availableRecipes.All(r => !IdolUpgrade.IsOurs(r.Recipe)), "Upgrade tab list no idol rows");
            c.Report();
        }
        finally
        {
            forge.Close();
            given.TakeBack();
        }
    }

    // ---------- forge.refine ----------

    private static IEnumerator RunRefine()
    {
        var c = new Checks(RefineName);
        var given = new Given();
        var forge = new Forge();
        var player = Player.m_localPlayer;
        var inventory = player.GetInventory();
        var gui = InventoryGui.instance;
        string taught = null;
        var refund = new Dictionary<string, int>();
        try
        {
            // A one-handed weapon with an idol recipe (vanilla data), and its idol.
            var recipe = ObjectDB.instance.m_recipes.FirstOrDefault(r => r != null && r.m_item != null && r.m_enabled
                && r.m_item.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon
                && r.m_resources.Any(q => q.m_upgraderResource && q.m_resItem != null));
            if (recipe == null)
            {
                c.Check(false, "no weapon recipe with an idol in ObjectDB");
                c.Report();
                yield break;
            }
            var idolReq = recipe.m_resources.First(q => q.m_upgraderResource && q.m_resItem != null);
            var idolPrefab = idolReq.m_resItem.gameObject.name;
            var idolName = idolReq.m_resItem.m_itemData.m_shared.m_name;
            var itemName = recipe.m_item.m_itemData.m_shared.m_name;
            SelfTest.Note(RefineName, $"weapon {recipe.m_item.name}, idol {idolPrefab}");
            if (player.m_knownRecipes.Add(itemName))
            {
                taught = itemName;
            }
            given.Add(recipe.m_item.gameObject.name, 1, 3);
            var weapon = inventory.GetAllItems().FirstOrDefault(i => i.m_shared.m_name == itemName);
            weapon.m_customData["MC.Test.Marker"] = "kept";
            given.Add(idolPrefab, 1, 1);
            given.Add(idolPrefab, 1, 4);
            yield return Forge.Open(forge);

            var row = RowOf(gui, weapon);
            c.Check(row >= 0, "Upgrade tab at the Forge must list the weapon");
            gui.SetRecipe(row, false);
            yield return null;
            yield return null;
            var high = IdolLevels.ChancePercent(3);
            var buttonText = gui.m_craftButton.GetComponentInChildren<TMP_Text>().text;
            c.Check(buttonText.Contains(high + "%"), $"default pick = 3 stars: button must show {high}%, is '{buttonText}'");
            c.Check(gui.m_itemCraftType.text.Contains(ForgePanel.FailureText(weapon.m_quality)),
                $"warning replaced by the failure rule, is '{gui.m_itemCraftType.text}'");
            c.Check(ForgePanel.FailureText(3) == "A failure costs 1 level." && ForgePanel.FailureText(1) == "A failure costs only the idol.",
                "default failure text: 1 level, nothing at level 1");
            c.Check(StarIcons.IsStarSprite(gui.m_recipeRequirementList[0].transform.Find("res_icon").GetComponent<UnityEngine.UI.Image>().sprite),
                "idol slot must show the 3 star icon");
            SelfTest.Screenshot(RefineName, "forge-upgrade-tab");
            yield return null;
            yield return null;

            // Click cycle: 3 stars -> plain.
            ForgePanel.OnSlotClicked(0);
            yield return null;
            c.Check(IdolChoice.LevelToSpend(inventory, idolName, 1) == 0, "click on the idol slot must switch to the plain idol");
            ForgePanel.OnSlotClicked(0);
            yield return null;
            c.Check(IdolChoice.LevelToSpend(inventory, idolName, 1) == 3, "second click back to 3 stars");

            // Failure with the 3 star idol (default rule: lose 1 level): roll above its chance.
            var chance3 = IdolLevels.Chance(3);
            if (chance3 < 1f)
            {
                ForgeRefine.TestRoll = Mathf.Min(1f, chance3 + 0.02f);
                weapon.m_durability = weapon.GetMaxDurability() * 0.5f;
                yield return PressAndFinish(gui);
                c.Check(inventory.ContainsItem(weapon) && weapon.m_quality == 2, $"failure: same weapon object down to level 2, is {weapon.m_quality}");
                c.Check(Mathf.Approximately(weapon.m_durability, weapon.GetMaxDurability()), "failure: re-made like vanilla (full durability)");
                c.Check(weapon.m_crafterName == player.GetPlayerName(), "failure: refiner is the crafter (vanilla re-make)");
                c.Check(inventory.CountItems(idolName, 4) == 0 && inventory.CountItems(idolName, 1) == 1, "failure: only the 3 star idol used");
            }
            else
            {
                SelfTest.Note(RefineName, "3 star chance is 100%: failure case skipped");
                inventory.RemoveItem(idolName, 1, 4);
                weapon.m_quality = 2;
            }

            // Success with the plain idol: roll under its chance.
            ForgeRefine.TestRoll = Mathf.Max(0f, IdolLevels.Chance(0) - 0.02f);
            gui.UpdateCraftingPanel();
            yield return null;
            row = RowOf(gui, weapon);
            gui.SetRecipe(row, false);
            yield return null;
            yield return PressAndFinish(gui);
            c.Check(InventoryGui.IsVisible() && player.GetCurrentCraftingStation() == forge.Station, "Forge window must still be open");
            c.Check(inventory.ContainsItem(weapon) && weapon.m_quality == 3, $"success: same weapon object at level 3, is {weapon.m_quality}");
            c.Check(Mathf.Approximately(weapon.m_durability, weapon.GetMaxDurability()), "success: full durability (vanilla gives a new item)");
            c.Check(weapon.m_customData.TryGetValue("MC.Test.Marker", out var marker) && marker == "kept", "success: custom data kept");
            c.Check(weapon.m_crafterName == player.GetPlayerName(), "success: refiner is the crafter (vanilla re-make)");
            c.Check(inventory.CountItems(idolName) == 0, "success: plain idol used");

            // No idol left: button off.
            gui.UpdateCraftingPanel();
            yield return null;
            gui.SetRecipe(RowOf(gui, weapon), false);
            yield return null;
            yield return null;
            c.Check(!gui.m_craftButton.interactable, "no idol: refine button off");

            // OnFailure = Destroy (test rules, not the config): vanilla break, part of the materials back.
            var destroy = ForgeRules.Own();
            destroy.Failure = FailureMode.Destroy;
            ServerRules.TestRules = destroy;
            given.Add(idolPrefab, 1, 1);
            foreach (var req in recipe.m_resources.Where(q => q.m_recover && q.m_resItem != null))
            {
                refund[req.m_resItem.m_itemData.m_shared.m_name] = inventory.CountItems(req.m_resItem.m_itemData.m_shared.m_name, -1, false);
            }
            ForgeRefine.TestRoll = Mathf.Min(1f, IdolLevels.Chance(0) + 0.02f);
            gui.UpdateCraftingPanel();
            yield return null;
            gui.SetRecipe(RowOf(gui, weapon), false);
            yield return null;
            yield return null;
            c.Check(gui.m_itemCraftType.text.Contains("A failure destroys the item."), $"destroy rule shown, is '{gui.m_itemCraftType.text}'");
            yield return PressAndFinish(gui);
            c.Check(!inventory.ContainsItem(weapon), "destroy: weapon gone");
            c.Check(inventory.CountItems(idolName) == 0, "destroy: idol used");
            c.Check(refund.Any(pair => inventory.CountItems(pair.Key, -1, false) > pair.Value), "destroy: some materials came back");
            c.Report();
        }
        finally
        {
            ServerRules.TestRules = null;
            foreach (var pair in refund)
            {
                var extra = inventory.CountItems(pair.Key, -1, false) - pair.Value;
                if (extra > 0)
                {
                    inventory.RemoveItem(pair.Key, extra, -1, false);
                }
            }
            ForgeRefine.TestRoll = null;
            IdolChoice.Clear();
            forge.Close();
            given.TakeBack();
            if (taught != null)
            {
                player.m_knownRecipes.Remove(taught);
            }
        }
    }
#endif
}
