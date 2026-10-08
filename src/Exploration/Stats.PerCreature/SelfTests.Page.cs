#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using BepInEx.Logging;
using HarmonyLib;
using MC.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace MC.Exploration.StatsPerCreatureMod;

// Page, settings, storage and on/off tests. Me write kill numbers straight into profile lifetime table (the
// rig put real ones back): page must show just what game has saved.
internal static partial class SelfTests
{
    private const string Neck = "Neck";

    // Same order the page use for names (as the player read them, then ordinal).
    private static int CompareShown(string a, string b)
    {
        var c = string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase);
        return c != 0 ? c : string.CompareOrdinal(a, b);
    }

    private static bool SameRows(List<string> rows, params string[] expected) => SameLines(rows, expected);

    private static int CountOf(string text, string part)
    {
        var count = 0;
        for (var i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    // ---------- percreature.page ----------

    private static IEnumerator RunPage()
    {
        var rig = Rig.Begin(PageName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(PageName);
        try
        {
            const string fake = "$enemy_mc_selftest_fake";
            const string playerName = "Bjorn";
            var neck = CreatureName(Neck);
            var greydwarf = CreatureName(Greydwarf);
            var deer = CreatureName(Deer);
            var boar = CreatureName(Boar);
            var wolf = CreatureName(Wolf);
            if (!c.Check(neck != null && greydwarf != null && deer != null && boar != null && wolf != null, "Neck, Greydwarf, Deer, Boar and Wolf prefabs exist"))
            {
                c.Report();
                yield break;
            }
            rig.ClearKills();
            // Kills from before game saved weapon types: total only.
            rig.SetKills(neck, 40f);
            rig.SetKills(greydwarf, 243f, melee: 180f, ranged: 50f);
            rig.SetKills(deer, 3f, melee: 3f);
            // Translation key game no know.
            rig.SetKills(fake, 2f);
            // No translation key: player name from old PvP kill.
            rig.SetKills(playerName, 1f);
            // What achievement reset leave: creature still in table, 0 everywhere.
            foreach (var bucket in rig.Kills)
            {
                bucket[boar] = 0f;
            }
            rig.SetTames(Stored("1\t" + deer, "2\t" + wolf));
            var since = rig.RawSince;
            c.Check(since != null && Regex.IsMatch(since, "^[0-9]{4}-[0-9]{2}-[0-9]{2}$"),
                $"the character has its tame-counting start day, written at spawn as yyyy-MM-dd (found '{since}')");

            var page = new Page();
            yield return OpenPage(page);
            if (!c.Check(page.Ok, $"Player Statistics could not be read: {page.Problem}"))
            {
                c.Report();
                yield break;
            }
            var s = ParseSection(page.Entry);
            c.Note($"entry {page.Index + 1} of {page.Topics.Count} in the Valheim Compendium list; section: {s.Describe()}");
            c.Check(s.Found, "the Player Statistics text starts with the 'Creatures' section");
            c.Check(s.Grey == GreyLine(since ?? "?"), $"grey line reads 'Kills: the game's own count ... Tames: counted while this mod is on (since {since}).' (found '{s.Grey}')");
            c.Check(SameRows(s.Creatures,
                    $"{Loc(greydwarf)}: 243 killed (melee 180, ranged 50, other 13)",
                    $"{Loc(neck)}: 40 killed",
                    $"{Loc(deer)}: 3 killed (melee 3), 1 tamed",
                    $"{Loc(fake)}: 2 killed",
                    $"{Loc(wolf)}: 2 tamed"),
                "creature rows, most killed first: 'Greydwarf: 243 killed (melee 180, ranged 50, other 13)' (parts add up), 'Neck: 40 killed' (old kills: no "
                + "parentheses), 'Deer: 3 killed (melee 3), 1 tamed', the untranslated name with 2 killed, 'Wolf: 2 tamed' "
                + $"(found {s.Describe()})");
            c.Check(s.Row(Loc(boar)) == null && s.Raw.IndexOf(Loc(boar) + ":", StringComparison.Ordinal) < 0,
                "a creature whose counts are all 0 has no row");
            c.Note($"a translation key the game does not know is shown as '{Loc(fake)}'");
            c.Check(s.Raw.IndexOf(fake, StringComparison.Ordinal) < 0 && Loc(fake) != fake, "the untranslated creature is shown the way the game shows a missing translation, not as the raw key");
            c.Check(s.OtherLine && SameRows(s.Others, $"{playerName}: 1 killed") && s.Row(playerName) == null,
                $"the player name is listed only under the grey 'Other names' line (found {s.Describe()})");
            c.Check(s.Odd.Count == 0, $"no unexpected line in the section ({string.Join(" | ", s.Odd.ToArray())})");
            c.Check(s.Raw.IndexOf("mixed", StringComparison.OrdinalIgnoreCase) < 0, "the section never says 'mixed'");
            c.Check(s.Found && s.Raw.EndsWith("\n\n\n", StringComparison.Ordinal) && VanillaStart(s.Rest),
                "after the last row come two empty lines, then the vanilla text (its first line)");
            c.Check(CountOf(s.Rest, "<color=orange>Difficulty Category: ") == rig.Profile.m_playerStats.Length,
                $"the vanilla text below has all its {rig.Profile.m_playerStats.Length} category blocks (found {CountOf(s.Rest, "<color=orange>Difficulty Category: ")})");

            // What player read once entry is picked.
            var shown = ParseSection(page.Shown);
            c.Check(page.Topic == Loc("$inventory_stats"), $"the picked entry is titled '{Loc("$inventory_stats")}' (found '{page.Topic}')");
            c.Check(shown.Found && SameRows(shown.Creatures, s.Creatures.ToArray()) && SameRows(shown.Others, s.Others.ToArray()) && shown.Grey == s.Grey,
                $"the text area starts with the same section (found {shown.Describe()})");
        }
        finally
        {
            rig.End();
        }
        c.Report();
    }

    // ---------- percreature.scroll ----------

    // Gamepad part that can be checked without a gamepad: the entry is picked the way the D-pad picks it (ShowText),
    // the text starts with the section, and the scroll bar the right stick moves (TextsDialog.UpdateGamepadInput) is
    // on and scrolls the longer text. Own small test: it hangs on how the game's dialog is built.
    private static IEnumerator RunScroll()
    {
        var rig = Rig.Begin(ScrollName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(ScrollName);
        try
        {
            var page = new Page();
            yield return OpenPage(page);
            if (!c.Check(page.Ok, $"Player Statistics could not be read: {page.Problem}"))
            {
                c.Report();
                yield break;
            }
            c.Check(page.Topic == Loc("$inventory_stats") && page.Shown != null && page.Shown.StartsWith(Header + "\n", StringComparison.Ordinal),
                $"with Player Statistics picked, the text starts with the Creatures section (title '{page.Topic}')");
            yield return null;
            yield return null;
            var bar = page.Dialog.m_rightScrollbar;
            // Scroll view of the text = the one the text area live in. How the game tie the bar to it (scroll view's
            // own bar, or bar's change event) is prefab data: me only say which, the scroll check below prove the tie.
            var scroll = page.Dialog.m_textArea != null ? page.Dialog.m_textArea.GetComponentInParent<ScrollRect>() : null;
            if (c.Check(bar != null && scroll != null, "the dialog has its right scroll bar (the one the gamepad's right stick moves) and a scroll view around the text"))
            {
                c.Note(scroll.verticalScrollbar == bar
                    ? "the right scroll bar is the text scroll view's own bar"
                    : "the right scroll bar is not set as the text scroll view's own bar (tied some other way, see the scroll check)");
                c.Check(bar.gameObject.activeSelf && bar.size < 0.999f,
                    $"the text is longer than the view, so the right scroll bar is on (on {bar.gameObject.activeSelf}, size {F(bar.size)})");
                var barBefore = bar.value;
                bar.value = 1f;
                yield return null;
                var one = scroll.verticalNormalizedPosition;
                bar.value = 0f;
                yield return null;
                var zero = scroll.verticalNormalizedPosition;
                bar.value = barBefore;
                c.Check(Mathf.Abs(one - zero) > 0.5f, $"moving that scroll bar scrolls the text from one end to the other (position {F(one)} -> {F(zero)})");
            }
        }
        finally
        {
            rig.End();
        }
        c.Report();
    }

    // ---------- percreature.settings ----------

    // Four creatures whose most-killed order is the reverse of their name order (in any game language), two of them tied.
    private sealed class SettingsRows
    {
        internal string[] MostKilledTypes;
        internal string[] MostKilledPlain;
        internal string[] NameTypes;
        internal string[] NamePlain;

        internal string[] For(SortOrder sort, bool types) =>
            sort == SortOrder.Name ? (types ? NameTypes : NamePlain) : (types ? MostKilledTypes : MostKilledPlain);
    }

    private static SettingsRows InjectSettingsData(Rig rig)
    {
        var tokens = new List<string>();
        foreach (var prefab in new[] { Boar, Greyling, Deer, Neck })
        {
            var name = CreatureName(prefab);
            if (name == null)
            {
                return null;
            }
            tokens.Add(name);
        }
        tokens.Sort((a, b) => CompareShown(Loc(a), Loc(b)));
        rig.ClearKills();
        rig.SetTames(null);
        rig.SetKills(tokens[0], 1f);
        rig.SetKills(tokens[1], 5f, melee: 5f);
        rig.SetKills(tokens[2], 5f, ranged: 5f);
        rig.SetKills(tokens[3], 9f, melee: 4f, ranged: 2f);
        var n = tokens.ConvertAll(Loc);
        return new SettingsRows
        {
            MostKilledTypes = new[] { $"{n[3]}: 9 killed (melee 4, ranged 2, other 3)", $"{n[1]}: 5 killed (melee 5)", $"{n[2]}: 5 killed (ranged 5)", $"{n[0]}: 1 killed" },
            MostKilledPlain = new[] { $"{n[3]}: 9 killed", $"{n[1]}: 5 killed", $"{n[2]}: 5 killed", $"{n[0]}: 1 killed" },
            NameTypes = new[] { $"{n[0]}: 1 killed", $"{n[1]}: 5 killed (melee 5)", $"{n[2]}: 5 killed (ranged 5)", $"{n[3]}: 9 killed (melee 4, ranged 2, other 3)" },
            NamePlain = new[] { $"{n[0]}: 1 killed", $"{n[1]}: 5 killed", $"{n[2]}: 5 killed", $"{n[3]}: 9 killed" },
        };
    }

    // Me open page again and compare its creature rows.
    private static IEnumerator CheckRows(Checks c, string when, string[] expected)
    {
        var page = new Page();
        yield return ReadPage(page);
        if (!c.Check(page.Ok, $"{when}: Player Statistics could not be read: {page.Problem}"))
        {
            yield break;
        }
        var s = ParseSection(page.Entry);
        c.Check(s.Found && SameRows(s.Creatures, expected), $"{when}: rows must be [{string.Join(" | ", expected)}] (found {s.Describe()})");
    }

    private static IEnumerator RunSettings()
    {
        var rig = Rig.Begin(SettingsName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(SettingsName);
        try
        {
            var rows = InjectSettingsData(rig);
            if (!c.Check(rows != null, "Boar, Greyling, Deer and Neck prefabs exist"))
            {
                c.Report();
                yield break;
            }
            Plugin.TestDisplay = (SortOrder.MostKilled, true);
            yield return CheckRows(c, "defaults (SortBy = MostKilled, ShowWeaponTypes = true): most killed first, ties by name, weapon types in parentheses", rows.MostKilledTypes);
            Plugin.TestDisplay = (SortOrder.Name, false);
            yield return CheckRows(c, "SortBy = Name and ShowWeaponTypes = false at the next open: alphabetical, no parentheses", rows.NamePlain);
            Plugin.TestDisplay = (SortOrder.MostKilled, true);
            yield return CheckRows(c, "set back at the next open: most killed first, parentheses back", rows.MostKilledTypes);

            // Each setting alone (me build entry, no show it).
            Plugin.TestDisplay = (SortOrder.Name, true);
            var nameTypes = ParseSection(BuildEntry());
            c.Check(nameTypes.Found && SameRows(nameTypes.Creatures, rows.NameTypes), $"SortBy = Name alone: alphabetical with parentheses (found {nameTypes.Describe()})");
            Plugin.TestDisplay = (SortOrder.MostKilled, false);
            var plain = ParseSection(BuildEntry());
            c.Check(plain.Found && SameRows(plain.Creatures, rows.MostKilledPlain), $"ShowWeaponTypes = false alone: most killed first, no parentheses (found {plain.Describe()})");

            // No override: two settings of config file decide (me only read them, never write).
            Plugin.TestDisplay = null;
            var sort = Plugin.SortBy.Value;
            var types = Plugin.ShowWeaponTypes.Value;
            var fromConfig = ParseSection(BuildEntry());
            c.Note($"settings in the config right now: SortBy = {sort}, ShowWeaponTypes = {types}");
            c.Check(fromConfig.Found && SameRows(fromConfig.Creatures, rows.For(sort, types)),
                $"with no test override the page follows the two settings as they are (SortBy = {sort}, ShowWeaponTypes = {types}; found {fromConfig.Describe()})");
        }
        finally
        {
            rig.End();
        }
        c.Report();
    }

    // ---------- percreature.store ----------

    private static byte[] SavedPair(string key, string value)
    {
        var pkg = new ZPackage();
        pkg.Write(key);
        pkg.Write(value);
        return pkg.GetArray();
    }

    private static bool HasBytes(byte[] all, byte[] part)
    {
        for (var i = 0; i + part.Length <= all.Length; i++)
        {
            var j = 0;
            while (j < part.Length && all[i + j] == part[j])
            {
                j++;
            }
            if (j == part.Length)
            {
                return true;
            }
        }
        return false;
    }

    private static IEnumerator RunStore()
    {
        var rig = Rig.Begin(StoreName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(StoreName);
        var culture = Thread.CurrentThread.CurrentCulture;
        try
        {
            var p = rig.P;
            var boarName = CreatureName(Boar);
            if (!c.Check(boarName == TameToken, $"Boar is named {TameToken} (found '{boarName}')"))
            {
                c.Report();
                yield break;
            }
            // Character this mod never saw: no kills, no tame data, no start day.
            rig.ClearKills();
            rig.SetTames(null);
            p.m_customData.Remove(CreatureCounts.CountingSinceDataKey);

            c.Check(CreatureCounts.ApiVersion == 1, $"ApiVersion is 1 (found {CreatureCounts.ApiVersion})");
            c.Check(CreatureCounts.TamesDataKey == "MC.Exploration.Stats.PerCreature.Tames"
                    && CreatureCounts.CountingSinceDataKey == "MC.Exploration.Stats.PerCreature.CountingSince",
                "the two documented data keys are unchanged");
            c.Check(CreatureCounts.IsAvailable, "IsAvailable is true in the world with the character spawned");
            c.Check(CreatureCounts.CountingSince == null && CreatureCounts.GetCreatureNames().Count == 0 && CreatureCounts.GetTames(boarName) == 0
                    && CreatureCounts.GetKills(boarName) == 0 && CreatureCounts.GetTames(null) == 0 && CreatureCounts.GetKills(null) == 0,
                "a character with nothing recorded: no start day, no names, 0 kills and tames");

            // First spawn with mod: start day written.
            var mark = _watch.Mark();
            var dayBefore = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            Patches.PlayerPatches.TestSpawned(p);
            var dayAfter = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var since = rig.RawSince;
            c.Check(since != null && Regex.IsMatch(since, "^[0-9]{4}-[0-9]{2}-[0-9]{2}$") && (since == dayBefore || since == dayAfter),
                $"at spawn the start day is stored as yyyy-MM-dd, today in plain digits ({dayAfter}; found '{since}')");
            c.Check(SameLines(_watch.Since(mark, LogLevel.Debug, "Counting started: "), $"Counting started: {since} (tames are counted from this date)."),
                $"log line 'Counting started: {since} (tames are counted from this date).' (found {Lines(_watch.Since(mark, LogLevel.Debug, "Counting started: "))})");
            var api = CreatureCounts.CountingSince;
            c.Check(api.HasValue && api.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) == since && api.Value.Year == DateTime.Now.Year,
                "CountingSince gives that day back (Gregorian year)");

            // Later spawns (death, login) never move it. Me check.
            p.m_customData[CreatureCounts.CountingSinceDataKey] = "2001-02-03";
            Patches.PlayerPatches.TestSpawned(p);
            c.Check(rig.RawSince == "2001-02-03", $"a later spawn keeps the stored start day (found '{rig.RawSince}')");

            var page = new Page();
            yield return ReadPage(page);
            if (c.Check(page.Ok, $"Player Statistics could not be read: {page.Problem}"))
            {
                var s = ParseSection(page.Entry);
                c.Check(s.Found && s.Grey == GreyLine("2001-02-03") && SameRows(s.Creatures, NothingRow) && !s.OtherLine,
                    $"with nothing recorded the page shows the section with the start day and 'Nothing killed or tamed yet.' (found '{s.Grey}', {s.Describe()})");
            }

            // One tame: exact stored text, API read it back, page show it.
            var holder = new Tameable[1];
            yield return SpawnTameable(rig, Boar, Ground(p.transform.position + OpenDirection(rig, false, 3f) * 3f), holder);
            if (!c.Check(holder[0] != null, "could not spawn a boar"))
            {
                c.Report();
                yield break;
            }
            mark = _watch.Mark();
            holder[0].Tame();
            yield return null;
            var raw = rig.RawTames;
            c.Check(raw == "1\n1\t" + TameToken, $"stored tame value is exactly '1\\n1\\t{TameToken}': version line 1, count in plain digits, a tab, the key (found '{Esc(raw)}')");
            c.Check(SameLines(TameLines(mark), $"Tame counted: {TameToken} (message). Stored MC.Exploration.Stats.PerCreature.Tames = 1\\n1\\t{TameToken}"),
                $"log line 'Tame counted: {TameToken} (message). Stored MC.Exploration.Stats.PerCreature.Tames = 1\\n1\\t{TameToken}' (found {Lines(TameLines(mark))})");
            var names = CreatureCounts.GetCreatureNames();
            c.Check(CreatureCounts.GetTames(TameToken) == 1 && names.Count == 1 && names[0] == TameToken && !ReferenceEquals(names, CreatureCounts.GetCreatureNames()),
                "the API reads it back: 1 Boar tamed, Boar listed (a new list each call)");
            yield return ReadPage(page);
            if (c.Check(page.Ok, $"Player Statistics could not be read: {page.Problem}"))
            {
                var s = ParseSection(page.Entry);
                c.Check(s.Found && SameRows(s.Creatures, $"{Loc(TameToken)}: 1 tamed"), $"the page shows 'Boar: 1 tamed' (found {s.Describe()})");
            }

            // Region format with other calendar (Thai): still Gregorian day in plain digits. No frame pass here.
            CultureInfo thai = null;
            try
            {
                thai = new CultureInfo("th-TH");
            }
            catch (Exception e)
            {
                c.Note($"Thai region format not available in this runtime ({e.GetType().Name}): calendar check skipped");
            }
            if (thai != null)
            {
                Thread.CurrentThread.CurrentCulture = thai;
                var gregorianYear = DateTime.Now.ToString("yyyy", CultureInfo.InvariantCulture);
                var thaiYear = "?";
                try
                {
                    thaiYear = DateTime.Now.ToString("yyyy");
                }
                catch (Exception e)
                {
                    c.Note($"this runtime cannot write a date in the Thai region format ({e.GetType().Name})");
                }
                c.Note($"Thai region format: this runtime writes the year as {thaiYear} (Gregorian {gregorianYear})"
                       + (thaiYear == gregorianYear ? "; same year, so this run proves nothing about another calendar" : ""));
                p.m_customData.Remove(CreatureCounts.CountingSinceDataKey);
                var thaiBefore = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                Patches.PlayerPatches.TestSpawned(p);
                var thaiAfter = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var thaiSince = rig.RawSince;
                c.Check(thaiSince != null && (thaiSince == thaiBefore || thaiSince == thaiAfter) && thaiSince.StartsWith(gregorianYear + "-", StringComparison.Ordinal),
                    $"with a Thai region format the start day is still the Gregorian day in plain digits ({thaiAfter}; found '{thaiSince}')");
                var thaiPage = ParseSection(BuildEntry());
                c.Check(thaiPage.Found && thaiPage.Grey == GreyLine(thaiSince ?? "?") && SameRows(thaiPage.Creatures, $"{Loc(TameToken)}: 1 tamed"),
                    $"with a Thai region format the page shows the same day and numbers in plain digits (found '{thaiPage.Grey}', {thaiPage.Describe()})");
                Thread.CurrentThread.CurrentCulture = culture;
            }
            since = rig.RawSince;

            // Saved with the character: the game's own character save data (written at death, logout and every save)
            // holds both values; after a load they are new text objects and are read again.
            var save = new ZPackage();
            p.Save(save);
            var bytes = save.GetArray();
            c.Check(HasBytes(bytes, SavedPair(CreatureCounts.TamesDataKey, raw ?? "")),
                "the character save data holds the tame counts under the documented key, exactly as stored");
            c.Check(since != null && HasBytes(bytes, SavedPair(CreatureCounts.CountingSinceDataKey, since)),
                "the character save data holds the start day under the documented key");
            rig.SetTames(raw);
            c.Check(!ReferenceEquals(rig.RawTames, raw) && CreatureCounts.GetTames(TameToken) == 1,
                "the same stored text loaded again (a new text object, nothing cached) reads 1 Boar tamed");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culture;
            rig.End();
        }
        c.Report();
    }

    // ---------- percreature.format ----------

    private static IEnumerator RunFormat()
    {
        var rig = Rig.Begin(FormatName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(FormatName);
        try
        {
            const string boar = TameToken;
            const string wolf = WolfToken;
            const string deer = "$enemy_deer";
            var since = rig.RawSince;
            var errorsBefore = _watch.Errors().Count;
            // Other mods' tests may have killed these three before me: names list and page row below must hang on
            // the stored tames only (saved kill numbers come back at End). Me credit no kill here.
            rig.ForgetKills(boar, wolf, deer);

            // Nothing stored.
            rig.SetTames(null);
            c.Check(CreatureCounts.GetTames(boar) == 0, "no stored value reads 0");
            CounterStore.AddTame(boar, "test");
            c.Check(rig.RawTames == "1\n1\t" + boar, $"first tame writes '1\\n1\\t{boar}' (found '{Esc(rig.RawTames)}')");

            // Windows line ends.
            rig.SetTames("1\r\n2\t" + boar + "\r\n");
            c.Check(CreatureCounts.GetTames(boar) == 2, $"Windows line ends are read: Boar 2 (found {CreatureCounts.GetTames(boar)})");
            CounterStore.AddTame(boar, "test");
            c.Check(rig.RawTames == "1\n3\t" + boar, $"and the next tame rewrites it clean: '1\\n3\\t{boar}' (found '{Esc(rig.RawTames)}')");

            // Empty line, text without a tab, signed number, padded number, zero, the same creature twice.
            rig.SetTames("1\n\njunk\n+3\t" + wolf + "\n 4\t" + wolf + "\n0\t" + deer + "\n2\t" + boar + "\n3\t" + boar);
            c.Check(CreatureCounts.GetTames(boar) == 5 && CreatureCounts.GetTames(wolf) == 0 && CreatureCounts.GetTames(deer) == 0,
                $"bad lines are skipped and the two lines of one creature are added up: Boar 5, Wolf 0, Deer 0 (found {CreatureCounts.GetTames(boar)}, "
                + $"{CreatureCounts.GetTames(wolf)}, {CreatureCounts.GetTames(deer)})");
            var names = CreatureCounts.GetCreatureNames();
            c.Check(names.Contains(boar) && !names.Contains(wolf) && !names.Contains(deer), "only the creature with a readable count is listed");
            CounterStore.AddTame(boar, "test");
            c.Check(rig.RawTames == "1\n6\t" + boar, $"and the next tame rewrites it clean: '1\\n6\\t{boar}' (found '{Esc(rig.RawTames)}')");

            // Rewrite must be sorted by name.
            rig.SetTames("1\n1\t" + wolf + "\n1\t" + boar);
            CounterStore.AddTame(deer, "test");
            c.Check(rig.RawTames == "1\n1\t" + boar + "\n1\t" + deer + "\n1\t" + wolf, $"a rewrite lists one creature per line, sorted by name (found '{Esc(rig.RawTames)}')");

            // Highest number.
            rig.SetTames("1\n2147483647\t" + boar);
            c.Check(CreatureCounts.GetTames(boar) == int.MaxValue, "2147483647 is read");
            CounterStore.AddTame(boar, "test");
            c.Check(rig.RawTames == "1\n2147483647\t" + boar && CreatureCounts.GetTames(boar) == int.MaxValue,
                $"a count at 2147483647 stays there (found '{Esc(rig.RawTames)}')");
            rig.SetTames("1\n2147483647\t" + boar + "\n5\t" + boar);
            c.Check(CreatureCounts.GetTames(boar) == int.MaxValue, "two lines that add up past 2147483647 read 2147483647");

            // Written by newer mod version: shown, never rewritten, one warning.
            CounterStore.TestResetWarnings();
            rig.SetTames("2\n5\t" + boar);
            var newer = rig.RawTames;
            var mark = _watch.Mark();
            c.Check(CreatureCounts.GetTames(boar) == 5, $"a value with version line 2 is still read: Boar 5 (found {CreatureCounts.GetTames(boar)})");
            // "Still shown" = on the page too (entry built like the Compendium build it, nothing opened).
            var newerPage = ParseSection(BuildEntry());
            c.Check(newerPage.Found && newerPage.Row(Loc(boar)) == $"{Loc(boar)}: 5 tamed",
                $"a value with version line 2 is still shown on the page: 'Boar: 5 tamed' (found {newerPage.Describe()})");
            CounterStore.AddTame(boar, "test");
            CounterStore.AddTame(wolf, "test");
            c.Check(ReferenceEquals(rig.RawTames, newer) && rig.RawTames == "2\n5\t" + boar && CreatureCounts.GetTames(boar) == 5 && CreatureCounts.GetTames(wolf) == 0,
                $"new tames are not counted and the value written by the newer version is left untouched (found '{Esc(rig.RawTames)}')");
            var warnings = _watch.Since(mark, LogLevel.Warning, "format version '2'");
            c.Check(warnings.Count == 1 && TameLines(mark).Count == 0, $"one warning for the two refused tames, no 'Tame counted' line ({warnings.Count} warning(s))");

            // Creature name with line break: refused, nothing written.
            rig.SetTames("1\n1\t" + boar);
            var clean = rig.RawTames;
            mark = _watch.Mark();
            CounterStore.AddTame("a\nb", "test");
            CounterStore.AddTame("a\rb", "test");
            CounterStore.AddTame("", "test");
            CounterStore.AddTame(null, "test");
            c.Check(ReferenceEquals(rig.RawTames, clean) && _watch.Since(mark, LogLevel.Warning, "contains a line break").Count == 2 && TameLines(mark).Count == 0,
                $"a name with a line break is refused with a warning each time; an empty name counts nothing (found '{Esc(rig.RawTames)}', "
                + $"{_watch.Since(mark, LogLevel.Warning, "contains a line break").Count} warning(s))");
            c.Check(ReferenceEquals(rig.RawSince, since), "none of this moved the start day");
            var errors = _watch.Errors();
            c.Check(errors.Count == errorsBefore,
                $"all of this was read without an error line of this mod ({errors.Count - errorsBefore} new: {(errors.Count > errorsBefore ? errors[errors.Count - 1] : "")})");
        }
        finally
        {
            CounterStore.TestResetWarnings();
            rig.End();
        }
        c.Report();
        yield return null;
    }

    // ---------- percreature.toggle ----------

    // Feature turned off live, then on again. turnOff / turnOn = how this run flips it: single player = the Debug block
    // hook (Enabled never written), multiplayer run = the real Enabled setting.
    private static IEnumerator ToggleSteps(Rig rig, Checks c, Plugin plugin, Action turnOff, Action turnOn)
    {
        var p = rig.P;
        var greyling = CreatureName(Greyling);
        var boarName = CreatureName(Boar);
        var wolfName = CreatureName(Wolf);
        if (!c.Check(greyling != null && boarName != null && wolfName != null, "Greyling, Boar and Wolf prefabs exist"))
        {
            yield break;
        }
        // As on a character with 2 Greyling kills (club), 1 boar tamed, no Boar or Wolf kill; the rest of the kill
        // table stays as it is.
        rig.ForgetKills(greyling, boarName, wolfName);
        rig.SetKills(greyling, 2f, melee: 2f);
        rig.SetTames(Stored("1\t" + boarName));
        var since = rig.RawSince;
        var rawTames = rig.RawTames;
        var page = new Page();
        if (!c.Check(plugin.IsActive && since != null, $"setup: the feature is active and the start day is stored ({plugin.State}: {plugin.StatusText})"))
        {
            yield break;
        }

        // On: entry = section + vanilla text. Off in the same frame: entry = that vanilla text, byte for byte.
        var onEntry = BuildEntry();
        var section = StatsSection.Build();
        var onOk = c.Check(onEntry != null && ParseSection(onEntry).Found && onEntry.StartsWith(section, StringComparison.Ordinal),
            "on: the Player Statistics entry starts with the Creatures section");
        var mark = _watch.Mark();
        turnOff();
        var offEntry = BuildEntry();
        c.Check(!plugin.IsActive, $"turned off: the feature is no longer active ({plugin.State}: {plugin.StatusText})");
        c.Check(offEntry != null && VanillaStart(offEntry) && offEntry.IndexOf(Header, StringComparison.Ordinal) < 0,
            "off: the entry is the vanilla text, no Creatures section");
        if (onOk && offEntry != null)
        {
            c.Check(onEntry.Substring(section.Length) == offEntry, "the text after the section (feature on) is exactly the vanilla text (feature off): vanilla statistics unchanged");
        }

        yield return ReadPage(page);
        c.Check(page.Ok && !ParseSection(page.Entry).Found && VanillaStart(page.Entry) && page.Shown != null
                && page.Shown.IndexOf(Header, StringComparison.Ordinal) < 0,
            $"off: the reopened page is the vanilla page ({page.Problem})");

        // While off: tame next to player, one far away, tame message from other game. None count.
        var dir = OpenDirection(rig, false, 3f);
        var holder = new Tameable[1];
        yield return SpawnTameable(rig, Boar, Ground(p.transform.position + dir * 3f), holder);
        var boar = holder[0];
        yield return SpawnTameable(rig, Wolf, Ground(p.transform.position + OpenDirection(rig, false, 40f) * 40f), holder);
        var wolf = holder[0];
        if (c.Check(boar != null && wolf != null, "could not spawn a boar and a wolf"))
        {
            rig.ArmMessage();
            boar.Tame();
            wolf.Tame();
            yield return null;
            c.Check(boar.IsTamed() && wolf.IsTamed() && rig.CenterText == Loc(boarName + TamedSuffix),
                $"off: the game still tames both and shows its own message for the near one (center text '{rig.CenterText}')");
            p.m_nview.InvokeRPC("Message", (int)MessageHud.MessageType.Center, boarName + TamedSuffix, 0);
            yield return null;
            c.Check(CreatureCounts.GetTames(boarName) == 1 && CreatureCounts.GetTames(wolfName) == 0 && ReferenceEquals(rig.RawTames, rawTames)
                    && TameLines(mark).Count == 0,
                $"off: tames are not counted (Boar {CreatureCounts.GetTames(boarName)}, expected 1 from before; Wolf {CreatureCounts.GetTames(wolfName)}; "
                + $"log {Lines(TameLines(mark))})");
        }

        // While off: a kill. Game count it, API still read it.
        var victims = new Character[1];
        yield return SpawnNear(rig, Greyling, Ground(p.transform.position + dir * 3f + Vector3.Cross(Vector3.up, dir) * 2f), victims);
        if (c.Check(victims[0] != null, "could not spawn a Greyling"))
        {
            Hit(victims[0], p, Skills.SkillType.Clubs, 1e7f);
            var w = new Waiter();
            yield return WaitGone(victims[0], 4f, w);
            var t = Tally.Of(greyling);
            c.Check(w.Met && t.Total == 3 && t.Melee == 3, $"off: the game counts the Greyling kill and the API reads it (found {t}, expected 3 killed, melee 3)");
        }
        var apiDay = CreatureCounts.CountingSince;
        c.Check(CreatureCounts.GetTames(boarName) == 1 && apiDay.HasValue && apiDay.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) == since,
            "off: the API still reads the stored tames and start day");

        // On again.
        turnOn();
        c.Check(plugin.IsActive, $"turned on again: the feature is active ({plugin.State}: {plugin.StatusText})");
        c.Check(ReferenceEquals(rig.RawSince, since), $"the start day is unchanged ('{rig.RawSince}', was '{since}')");
        yield return ReadPage(page);
        if (c.Check(page.Ok, $"on again: Player Statistics could not be read: {page.Problem}"))
        {
            var s = ParseSection(page.Entry);
            c.Check(s.Found && s.Grey == GreyLine(since), "on again: the section is back, with the same start day");
            c.Check(s.Row(Loc(greyling)) == $"{Loc(greyling)}: 3 killed (melee 3)" && s.Row(Loc(boarName)) == $"{Loc(boarName)}: 1 tamed"
                    && s.Row(Loc(wolfName)) == null,
                $"on again: the Greyling killed while off is counted (3 killed), the boar and wolf tamed while off are not (Boar still 1 tamed, no Wolf row) "
                + $"(found {s.Describe()})");
        }
    }

    private static IEnumerator RunToggle()
    {
        var rig = Rig.Begin(ToggleName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(ToggleName);
        var plugin = FindPlugin();
        if (plugin == null)
        {
            rig.End();
            SelfTest.Fail(ToggleName, "plugin object not found");
            yield break;
        }
        // Status line change while off: me keep it in memory only, this test never write the config file.
        var saveOnSet = plugin.Config.SaveOnConfigSet;
        try
        {
            plugin.Config.SaveOnConfigSet = false;
            yield return ToggleSteps(rig, c, plugin,
                () =>
                {
                    Plugin.TestForceOff = true;
                    FeatureRegistry.RefreshAll();
                },
                () =>
                {
                    Plugin.TestForceOff = false;
                    FeatureRegistry.RefreshAll();
                });
            c.Check(plugin.Enabled.Value, "the Enabled setting was never touched");
        }
        finally
        {
            if (Plugin.TestForceOff)
            {
                Plugin.TestForceOff = false;
                FeatureRegistry.RefreshAll();
            }
            plugin.Config.SaveOnConfigSet = saveOnSet;
            rig.End();
        }
        c.Report();
    }

    // ---------- percreature.compat ----------

    private const string OtherModTopic = "MC self test: entry of another mod";
    private const string MissingLine = "Player Statistics entry not found in the Compendium; creature section not added.";

    // Stand-in for a mod that edits the Compendium list after the game filled it: own entry first, Player Statistics
    // moved from the end to the second place.
    private static void OtherModEditsList(TextsDialog __instance)
    {
        var texts = __instance.m_texts;
        var topic = Loc("$inventory_stats");
        TextsDialog.TextInfo stats = null;
        for (var i = texts.Count - 1; i >= 0; i--)
        {
            if (texts[i] != null && texts[i].m_topic == topic)
            {
                stats = texts[i];
                texts.RemoveAt(i);
                break;
            }
        }
        texts.Insert(0, new TextsDialog.TextInfo(OtherModTopic, "Text of another mod."));
        if (stats != null)
        {
            texts.Insert(1, stats);
        }
    }

    // Stand-in for a mod that takes the Player Statistics entry away (the game's AddStats never runs).
    private static bool OtherModDropsStats() => false;

    private static IEnumerator RunCompat()
    {
        var rig = Rig.Begin(CompatName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(CompatName);
        Harmony other = null;
        try
        {
            var greyling = CreatureName(Greyling);
            rig.ClearKills();
            rig.SetTames(null);
            rig.SetKills(greyling, 4f, melee: 4f);
            var errorsBefore = _watch.Errors().Count;
            var page = new Page();

            // ----- another mod adds an entry and moves Player Statistics -----
            other = new Harmony(ModInfo.Guid + ".selftest.othermod");
            other.Patch(AccessTools.Method(typeof(TextsDialog), nameof(TextsDialog.UpdateTextsList)),
                postfix: new HarmonyMethod(typeof(SelfTests), nameof(OtherModEditsList)));
            yield return ReadPage(page);
            if (c.Check(page.Ok, $"with another mod editing the list, Player Statistics could not be read: {page.Problem}"))
            {
                c.Check(page.Topics.Count > 2 && page.Topics[0] == OtherModTopic && page.Index == 1,
                    $"setup: the other mod's entry is first and Player Statistics was moved to the second place (it is entry {page.Index + 1} of {page.Topics.Count})");
                var s = ParseSection(page.Entry);
                var shown = ParseSection(page.Shown);
                c.Check(s.Found && SameRows(s.Creatures, $"{Loc(greyling)}: 4 killed (melee 4)") && VanillaStart(s.Rest),
                    $"Player Statistics still starts with the Creatures section, vanilla text after (found {s.Describe()})");
                c.Check(shown.Found && SameRows(shown.Creatures, s.Creatures.ToArray()), "and the text area shows it when the moved entry is picked");
            }
            other.UnpatchSelf();

            // ----- another mod takes the Player Statistics entry away -----
            Patches.TextsDialogPatches.TestResetMissingLogged();
            var mark = _watch.Mark();
            other.Patch(AccessTools.Method(typeof(TextsDialog), nameof(TextsDialog.AddStats)),
                prefix: new HarmonyMethod(typeof(SelfTests), nameof(OtherModDropsStats)));
            yield return ReadPage(page);
            var second = BuildEntry();
            var anySection = false;
            if (page.Dialog != null)
            {
                foreach (var entry in page.Dialog.m_texts)
                {
                    anySection |= entry != null && entry.m_text != null && entry.m_text.IndexOf(Header, StringComparison.Ordinal) >= 0;
                }
            }
            c.Check(page.Index < 0 && page.Topics.Count > 0 && second == null,
                $"setup: with the entry taken away the list has no Player Statistics ({page.Topics.Count} entries)");
            c.Check(!anySection, "no Creatures section is put anywhere else");
            c.Check(SameLines(_watch.Since(mark, LogLevel.Debug, MissingLine), MissingLine),
                $"the mod says so once in the log, not at every open ({_watch.Since(mark, LogLevel.Debug, MissingLine).Count} line(s) for 2 list builds)");
            other.UnpatchSelf();
            other = null;

            // ----- the other mod gone: all as before -----
            yield return ReadPage(page);
            c.Check(page.Ok && ParseSection(page.Entry).Found && page.Topics.IndexOf(OtherModTopic) < 0,
                $"without the other mod the page is as before ({page.Problem})");
            var errors = _watch.Errors();
            c.Check(errors.Count == errorsBefore,
                $"no error from this mod in any of these cases ({errors.Count - errorsBefore} new: {(errors.Count > errorsBefore ? errors[errors.Count - 1] : "")})");
        }
        finally
        {
            if (other != null)
            {
                other.UnpatchSelf();
            }
            Patches.TextsDialogPatches.TestResetMissingLogged();
            rig.End();
        }
        c.Report();
    }

    // ---------- percreature.log ----------

    // Own small test: it look at the whole run so far (it run last of this mod's tests), other tests stay clean of it.
    private static IEnumerator RunLog()
    {
        var c = new Checks(LogName);
        yield return null;
        var plugin = FindPlugin();
        if (plugin == null || _watch == null)
        {
            SelfTest.Fail(LogName, "plugin object or log watch not found");
            yield break;
        }
        var errors = _watch.Errors();
        c.Note($"{_watch.Seen} log line(s) of this mod seen since it was turned on at game start; feature is {plugin.State}");
        c.Check(_watch.Seen > 0, "the log watch has seen this mod's lines (it listens since the mod was turned on)");
        c.Check(plugin.IsActive, $"the feature is active ({plugin.State}: {plugin.StatusText})");
        for (var i = 0; i < errors.Count && i < 5; i++)
        {
            c.Note($"error line: {errors[i]}");
        }
        c.Check(errors.Count == 0,
            $"no error or exception line of this mod (its own log lines, or lines of any source that name it) since it was turned on: {errors.Count} found"
            + (errors.Count > 0 ? $", first: {errors[0]}" : ""));
        c.Report();
    }
}
#endif
