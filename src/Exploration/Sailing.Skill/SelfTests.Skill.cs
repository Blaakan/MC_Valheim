#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SailingSkillMod;

// Debug build only. Me = sailing.panel: the skill as the player meet it, no ship needed.
//   console     "raiseskill Sailing 10" typed in the real console (Terminal.TryRunCommand): level, console line, HUD
//               message with the icon; lower-case name; "raiseskill all 5" / "resetskill all"; "resetskill Sailing";
//               Tab on "raiseskill Sa" / "resetskill Sa"
//   panel       Skills panel opened through the inventory: Sailing row (name, level, icon, tooltip), row gone after
//               reset
//   save data   Player.Save hold the Sailing record; a fresh Skills object (as at login or respawn) load it with
//               level, progress and definition
//   death       vanilla Skills.OnDeath: Sailing lowered by the same share as another skill, progress emptied
//   language    vanilla language change (Localization.Clear + SetupLanguage, no pref written): name and description
//               stay, both ways; every word put back exactly after
// Character skills put back from a copy; console cheat marks put back (RunConsole).
internal static partial class SelfTests
{
    private const string PanelName = "sailing.panel";

    private static readonly FieldInfo TranslationsField =
        typeof(Localization).GetField("m_translations", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    private static readonly MethodInfo LocalizationClear =
        typeof(Localization).GetMethod("Clear", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null,
            Type.EmptyTypes, null);

    private static IEnumerator RunPanel()
    {
        var c = new Checks(PanelName);
        var player = Player.m_localPlayer;
        if (player == null || player.GetSkills() == null || Localization.instance == null)
        {
            SelfTest.Fail(PanelName, "no local player");
            yield break;
        }
        var skills = player.GetSkills();
        var type = SailingSkill.Type;
        var loc = Localization.instance;
        var icon = SailingSkill.Def.m_icon;
        var watch = LogMark();
        ZPackage backup = null;
        Skills fresh = null;
        try
        {
            backup = new ZPackage();
            skills.Save(backup);
            c.Check(MessageHud.instance != null && !Hud.IsUserHidden() && MessageSpy.CanRead, "the HUD is hidden (or its message queue was not found): its messages cannot be read");

            // ----- console: raiseskill Sailing 10 -----
            skills.ResetSkill(type);
            HelmSkill.Invalidate();
            var spy = new MessageSpy();
            var printed = RunConsole("raiseskill Sailing 10");
            if (c.Check(printed != null, "no console in this world: console commands not checked"))
            {
                spy.Poll();
                c.Check(skills.m_skillData.TryGetValue(type, out var raised) && Near(raised.m_level, 10f),
                    "'raiseskill Sailing 10' in the console did not give Sailing 10");
                c.Check(printed.Contains("Skill Sailing = 10"),
                    $"console printed '{Join(printed)}' after 'raiseskill Sailing 10', expected 'Skill Sailing = 10'");
                c.Check(icon != null && spy.SawTopLeft("Skill increased Sailing: 10", icon),
                    "no top-left message 'Skill increased Sailing: 10' with the Sailing icon (messages: "
                    + Join(spy.TopLeft.Select(p => p.Key + (p.Value != null ? " [" + p.Value.name + "]" : " [no icon]")).ToList()) + ")");
                // Typed by hand without Tab: any case.
                printed = RunConsole("raiseskill sailing 2");
                c.Check(skills.m_skillData.TryGetValue(type, out raised) && Near(raised.m_level, 12f)
                        && printed != null && printed.Contains("Skill Sailing = 12"),
                    $"'raiseskill sailing 2' did not add 2 levels (console: '{Join(printed)}')");
            }

            // ----- Skills panel row -----
            SetSailing(player, 10f, 0f);
            yield return ShowInventory(c);
            var rows = OpenPanel();
            if (c.Check(rows != null, "no inventory UI: the Skills panel was not read"))
            {
                var row = FindRow(rows, SailingSkill.DisplayName);
                if (c.Check(row != null, "the Skills panel has no (single) row named Sailing; rows: "
                                         + string.Join(", ", rows.Select(r => r.Name).ToArray())))
                {
                    c.Check(row.Level == "10", $"the Sailing row shows level '{row.Level}', expected 10");
                    c.Check(icon != null && row.Icon == icon,
                        $"the Sailing row shows icon '{(row.Icon != null ? row.Icon.name : "none")}', expected the mod's own "
                        + $"'{(icon != null ? icon.name : "none")}'");
                    c.Check(row.Tooltip == "$" + SailingSkill.DescriptionKey && loc.Localize(row.Tooltip) == SailingSkill.Description,
                        $"the Sailing row's tooltip is '{loc.Localize(row.Tooltip ?? "")}', expected the skill's description");
                    // The picture itself: the mod's own drawing (cream sail in the middle, hull below, empty corners).
                    var drawn = IconDrawn(row.Icon, out var picture);
                    c.Check(drawn, "the Sailing row's icon is not the drawn sail: " + picture);
                }
                // Inventory slides in: wait, then one picture for the human who checks the look of the icon.
                yield return new WaitForSeconds(0.7f);
                SelfTest.Screenshot(PanelName, "skills");
                yield return null;
                yield return null;
                ClosePanel();
            }

            // ----- Tab completes the name -----
            foreach (var key in new[] { "raiseskill", "resetskill" })
            {
                var done = TabComplete(key, "Sa", out var why);
                if (done != null)
                {
                    c.Check(done == key + " Sailing", $"Tab on '{key} Sa' gave '{done}', expected '{key} Sailing'");
                    continue;
                }
                // Input box did not take the text (console never opened): vanilla tabCycle's own filter and order on
                // the command's Tab list.
                var options = Terminal.commands.TryGetValue(key, out var command) && command != null ? command.GetTabOptions() : null;
                var matches = options != null
                    ? options.Where(o => o != null && o.Length > 2 && o.Substring(0, 2).ToLower() == "sa").ToList()
                    : new List<string>();
                matches.Sort();
                c.Note($"{key}: the console input box did not take text ({why}); Tab list read instead ({Join(matches)})");
                c.Check(matches.Count > 0 && matches[0] == "Sailing", $"Tab on '{key} Sa' would not give Sailing first ({Join(matches)})");
            }

            // ----- item texts read skill levels (vanilla tooltip, the Encyclopedia's item pages): Sailing untouched -----
            CheckItemTexts(c, player);

            // ----- console: all -----
            if (printed != null)
            {
                SetSailing(player, 10f, 0f);
                var swim = skills.GetSkill(Skills.SkillType.Swim).m_level;
                printed = RunConsole("raiseskill all 5");
                c.Check(Near(skills.GetSkill(type).m_level, 15f)
                        && Near(skills.GetSkill(Skills.SkillType.Swim).m_level, Mathf.Min(100f, swim + 5f)),
                    $"'raiseskill all 5': Sailing {F(skills.GetSkill(type).m_level)} (expected 15), Swim "
                    + $"{F(skills.GetSkill(Skills.SkillType.Swim).m_level)} (expected {F(Mathf.Min(100f, swim + 5f))})");
                c.Check(printed != null && printed.Contains("All skills increased by 5"),
                    $"console printed '{Join(printed)}' after 'raiseskill all 5'");
                printed = RunConsole("resetskill all");
                c.Check(!skills.m_skillData.ContainsKey(type) && !skills.m_skillData.ContainsKey(Skills.SkillType.Swim),
                    "'resetskill all' left Sailing (or Swim) on the character");
                c.Check(printed != null && printed.Contains("All skills reset"), $"console printed '{Join(printed)}' after 'resetskill all'");

                // ----- console: resetskill Sailing removes the row -----
                SetSailing(player, 23f, 3.3f);
                yield return ShowInventory(c);
                rows = OpenPanel();
                var before = FindRow(rows, SailingSkill.DisplayName);
                // Bar value must be readable (GuiBar.m_value): a bar me cannot read prove nothing.
                c.Check(before != null && before.Level == "23" && before.Progress > 0f,
                    $"the Skills panel does not show Sailing 23 with progress before the reset (level '{(before != null ? before.Level : "no row")}', "
                    + $"bar {(before != null ? F(before.Progress) : "?")}; -1 = the bar could not be read)");
                printed = RunConsole("resetskill Sailing");
                c.Check(!skills.m_skillData.ContainsKey(type) && printed != null && printed.Contains("Skill Sailing reset"),
                    $"'resetskill Sailing' did not remove the skill (console: '{Join(printed)}')");
                rows = OpenPanel();
                c.Check(rows != null && rows.All(r => r.Name != SailingSkill.DisplayName),
                    "the Skills panel still has a Sailing row after 'resetskill Sailing'");
                ClosePanel();
            }

            // ----- level and progress in the character's save data, and back into a newly loaded character -----
            SetSailing(player, 23f, 3.3f);
            var saved = new ZPackage();
            player.Save(saved);
            var record = new ZPackage();
            record.Write((int)type);
            record.Write(23f);
            record.Write(3.3f);
            var found = CountBytes(saved.GetArray(), record.GetArray());
            c.Check(found == 1, $"the character's save data holds the record 'Sailing, level 23, progress 3.3' {found} times, expected once");
            fresh = FreshSkills(player);
            c.Check(fresh.m_skills.Count(d => d != null && d.m_skill == type) == 1 && ReferenceEquals(fresh.GetSkillDef(type), SailingSkill.Def),
                "a newly made Skills object does not know the Sailing definition exactly once");
            var pkg = new ZPackage();
            skills.Save(pkg);
            pkg.SetPos(0);
            fresh.Load(pkg);
            c.Check(fresh.m_skillData.TryGetValue(type, out var loaded) && loaded.m_level == 23f && loaded.m_accumulator == 3.3f
                    && ReferenceEquals(loaded.m_info, SailingSkill.Def),
                "a newly loaded character lost the Sailing level, its progress or its definition");
            var again = new ZPackage();
            fresh.Save(again);
            c.Check(again.Size() == pkg.Size(), "saving the newly loaded skills gives other data than what was loaded");

            // ----- death penalty (vanilla call of Player.OnDeath on a hard death) -----
            SetSailing(player, 40f, 2f);
            var other = skills.GetSkill(Skills.SkillType.Swim);
            other.m_level = 20f;
            other.m_accumulator = 1f;
            var share = skills.m_DeathLowerFactor * Game.m_skillReductionRate;
            skills.OnDeath();
            HelmSkill.Invalidate();
            var sailing = skills.GetSkill(type);
            c.Check(Near(sailing.m_level, 40f * (1f - share), 1e-3f) && Near(other.m_level, 20f * (1f - share), 1e-3f),
                $"death penalty: Sailing 40 -> {F(sailing.m_level)}, Swim 20 -> {F(other.m_level)}, expected both lowered by "
                + $"{F(share * 100f)}%");
            if (Near(share, 0.05f, 1e-5f))
            {
                c.Check(Near(sailing.m_level, 38f, 1e-3f), $"death penalty: Sailing 40 -> {F(sailing.m_level)}, expected 38");
            }
            else
            {
                c.Note($"this world lowers skills by {F(share * 100f)}% on death (not the usual 5%): 40 -> 38 not checked as a number");
            }
            c.Check(sailing.m_accumulator == 0f && sailing.GetLevelPercentage() == 0f, "death penalty: Sailing progress not emptied");
            yield return ShowInventory(c);
            rows = OpenPanel();
            var dead = FindRow(rows, SailingSkill.DisplayName);
            // Bar read back as exactly 0 (-1 = bar not readable = no proof).
            c.Check(dead != null && dead.Level == ((int)sailing.m_level).ToString() && dead.Progress == 0f,
                $"after the death penalty the Skills panel shows Sailing '{(dead != null ? dead.Level : "no row")}' with progress "
                + $"{(dead != null ? F(dead.Progress) : "?")}, expected {(int)sailing.m_level} and an empty bar (-1 = the bar could not be read)");
            if (Near(share, 0.05f, 1e-5f))
            {
                c.Check(dead != null && dead.Level == "38", $"after the death penalty the Skills panel shows Sailing '{(dead != null ? dead.Level : "no row")}', expected 38");
            }
            ClosePanel();
            // Respawn = new character object loaded from the data saved at death.
            pkg = new ZPackage();
            skills.Save(pkg);
            pkg.SetPos(0);
            fresh.Load(pkg);
            c.Check(fresh.m_skillData.TryGetValue(type, out loaded) && loaded.m_level == sailing.m_level && loaded.m_accumulator == 0f,
                "the lowered Sailing level did not survive a reload of the character");

            // ----- language change and back (vanilla Localization.SetLanguage without its pref write) -----
            var translations = TranslationsField != null ? TranslationsField.GetValue(loc) as Dictionary<string, string> : null;
            if (c.Check(translations != null && LocalizationClear != null, "Localization.m_translations or Clear not found: language change not checked"))
            {
                var selected = loc.GetSelectedLanguage();
                var change = selected == "German" ? "French" : "German";
                var swimWord = loc.Localize("$skill_swim");
                var keep = new Dictionary<string, string>(translations);
                try
                {
                    LocalizationClear.Invoke(loc, null);
                    loc.SetupLanguage(change);
                    var changed = loc.Localize("$skill_swim");
                    c.Check(changed != swimWord && !changed.StartsWith("[", StringComparison.Ordinal),
                        $"the language did not change to {change} ('$skill_swim' reads '{changed}', before '{swimWord}')");
                    c.Check(loc.Localize("$" + SailingSkill.NameKey) == SailingSkill.DisplayName
                            && loc.Localize("$" + SailingSkill.DescriptionKey) == SailingSkill.Description,
                        $"after a change to {change} the skill reads '{loc.Localize("$" + SailingSkill.NameKey)}' / "
                        + $"'{loc.Localize("$" + SailingSkill.DescriptionKey)}'");
                    LocalizationClear.Invoke(loc, null);
                    loc.SetupLanguage(selected);
                    c.Check(loc.Localize("$" + SailingSkill.NameKey) == SailingSkill.DisplayName
                            && loc.Localize("$" + SailingSkill.DescriptionKey) == SailingSkill.Description,
                        $"after the change back to {selected} the skill reads '{loc.Localize("$" + SailingSkill.NameKey)}'");
                }
                finally
                {
                    // Every word as before the test, whoever added it (other mods add theirs in their own ways).
                    translations.Clear();
                    foreach (var pair in keep)
                    {
                        translations[pair.Key] = pair.Value;
                    }
                    SailingSkill.AddWords(loc);   // also empty the translate cache
                }
                c.Check(loc.Localize("$skill_swim") == swimWord && translations.Count == keep.Count,
                    "the game's words were not put back after the language check");
            }

            CheckQuiet(c, watch);
            c.Report();
        }
        finally
        {
            if (fresh != null)
            {
                DestroyNow(fresh.gameObject);
            }
            if (InventoryGui.instance != null && InventoryGui.IsVisible())
            {
                ClosePanel();
            }
            if (backup != null)
            {
                backup.SetPos(0);
                skills.Load(backup);
                HelmSkill.Invalidate();
            }
        }
    }

    // Me read icon pixels back through the graphics card (texture itself not readable): sail and hull painted, corners
    // empty. Colours not compared close (colour space of the read-back), only light or dark and see-through.
    private static bool IconDrawn(Sprite icon, out string detail)
    {
        detail = "no icon";
        if (icon == null || icon.texture == null)
        {
            return false;
        }
        var texture = icon.texture;
        var target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
        var before = RenderTexture.active;
        Texture2D copy = null;
        try
        {
            Graphics.Blit(texture, target);
            RenderTexture.active = target;
            copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0f, 0f, texture.width, texture.height), 0, 0);
            copy.Apply();
            // Places in the 64 x 64 drawing (SkillIcon: sail 11-53 x 22-53, hull 4-60 x 6-17), scaled if it ever grows.
            Color At(float x, float y) => copy.GetPixel(Mathf.RoundToInt(x / 64f * texture.width), Mathf.RoundToInt(y / 64f * texture.height));
            var sail = At(20f, 40f);
            var hull = At(32f, 10f);
            var corner = At(2f, 62f);
            var gap = At(2f, 30f);
            detail = $"{icon.name} {texture.width}x{texture.height}: sail alpha {F(sail.a)} light {F(sail.r)}, hull alpha {F(hull.a)}, "
                     + $"corner alpha {F(corner.a)}, beside the sail alpha {F(gap.a)}";
            return icon.name == "MC_Sailing_Skill" && sail.a > 0.9f && sail.r > 0.5f && hull.a > 0.9f && corner.a < 0.1f && gap.a < 0.1f;
        }
        catch (Exception e)
        {
            detail = "could not read the icon back: " + e.Message;
            return false;
        }
        finally
        {
            RenderTexture.active = before;
            RenderTexture.ReleaseTemporary(target);
            if (copy != null)
            {
                UnityEngine.Object.Destroy(copy);
            }
        }
    }

    // First item of each kind the Encyclopedia show with skill numbers: a weapon, a tool, a food.
    private static List<ItemDrop.ItemData> SkillReadingItems()
    {
        var result = new List<ItemDrop.ItemData>();
        var db = ObjectDB.instance;
        if (db == null)
        {
            return result;
        }
        ItemDrop.ItemData weapon = null;
        ItemDrop.ItemData tool = null;
        ItemDrop.ItemData food = null;
        foreach (var go in db.m_items)
        {
            var drop = go != null ? go.GetComponent<ItemDrop>() : null;
            var data = drop != null ? drop.m_itemData : null;
            if (data == null || data.m_shared == null || data.m_shared.m_icons == null || data.m_shared.m_icons.Length == 0)
            {
                continue;
            }
            var shared = data.m_shared;
            if (weapon == null && shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon && shared.m_skillType == Skills.SkillType.Swords)
            {
                weapon = data;
            }
            else if (tool == null && shared.m_itemType == ItemDrop.ItemData.ItemType.Tool)
            {
                tool = data;
            }
            else if (food == null && shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable && shared.m_food > 0f)
            {
                food = data;
            }
        }
        foreach (var item in new[] { weapon, tool, food })
        {
            if (item != null)
            {
                result.Add(item);
            }
        }
        return result;
    }

    // Vanilla item tooltip read skill levels and add missing skills at level 0 (weapon skills). Encyclopedia item pages
    // call it through own wrapper that remove what the call added. Neither may add Sailing to a character without it,
    // nor remove a Sailing 0 entry that was there.
    private static void CheckItemTexts(Checks c, Player player)
    {
        var skills = player.GetSkills();
        var type = SailingSkill.Type;
        var items = SkillReadingItems();
        if (!c.Check(items.Count == 3, $"no sword, tool and food found in the game's items ({items.Count} of 3): item texts not checked"))
        {
            return;
        }
        var names = string.Join(", ", items.Select(i => i.m_shared.m_name).ToArray());
        // Plugin dlls are not found by name through Type.GetType: me look at the loaded assemblies.
        Type builder = null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name == "MC.Exploration.Compendium.Encyclopedia")
            {
                builder = assembly.GetType("MC.Exploration.CompendiumEncyclopediaMod.DetailBuilder", false);
                break;
            }
        }
        var wrapper = builder != null
            ? builder.GetMethod("TooltipKeepingSkills", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, null,
                new[] { typeof(ItemDrop.ItemData), typeof(Player) }, null)
            : null;
        var encyclopedia = FeatureRegistry.Find("MC.Exploration.Compendium.Encyclopedia");
        if (wrapper == null)
        {
            c.Note("Encyclopedia not loaded (or its item text reader was renamed): only the game's own tooltip is checked");
        }
        else
        {
            c.Note($"Encyclopedia item text reader found (mod {(encyclopedia != null ? encyclopedia.Value.State : "not registered")}); items: {names}");
        }
        foreach (var present in new[] { false, true })
        {
            skills.ResetSkill(type);
            if (present)
            {
                SetSailing(player, 0f, 0f);
            }
            HelmSkill.Invalidate();
            var texts = 0;
            foreach (var item in items)
            {
                var text = ItemDrop.ItemData.GetTooltip(item, 1, true, Game.m_worldLevel);
                texts += string.IsNullOrEmpty(text) ? 0 : 1;
                if (wrapper == null)
                {
                    continue;
                }
                try
                {
                    wrapper.Invoke(null, new object[] { item, player });
                }
                catch (Exception e)
                {
                    c.Check(false, $"the Encyclopedia's item text reader threw for {item.m_shared.m_name}: {(e.InnerException ?? e).Message}");
                }
            }
            c.Check(texts == items.Count && skills.m_skillData.ContainsKey(type) == present,
                present
                    ? "reading item texts removed a Sailing 0 entry from the character"
                    : "reading item texts added a Sailing entry to a character without the skill");
        }
        skills.ResetSkill(type);
        HelmSkill.Invalidate();
    }

    // How many times the byte run is in the data.
    private static int CountBytes(byte[] data, byte[] run)
    {
        if (data == null || run == null || run.Length == 0)
        {
            return 0;
        }
        var count = 0;
        for (var i = 0; i + run.Length <= data.Length; i++)
        {
            var same = true;
            for (var j = 0; j < run.Length && same; j++)
            {
                same = data[i + j] == run[j];
            }
            if (same)
            {
                count++;
            }
        }
        return count;
    }
}
#endif
