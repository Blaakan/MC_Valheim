#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.CrossbowStaysLoadedMod;

// Debug build only. TESTING.md "weapon selection" (W01 to W05): which reload weapons keep their load, by the
// Weapons settings. Same test body twice: single-player run force the settings in memory (cfg never written),
// multiplayer run (own config folder, fresh default cfg) set the real ConfigEntry, so SettingChanged and the cfg
// defaults are in the checked path. Real swaps, real reloads, real eitr use.
internal static partial class SelfTests
{
    private const string ExtraWithDundr = HookName + ", " + DundrName;

    // real = multiplayer run. Settings must be the defaults there (fresh cfg); in a single-player run the player's
    // own cfg may differ: defaults forced in memory then, and said.
    private static WeaponSettings DefaultSettings(Rig rig, Checks c, bool real)
    {
        var settings = rig.UseSettings(real);
        if (real)
        {
            c.Check(WeaponSettings.AtDefaults, $"the Weapons settings of this run are not the defaults ({WeaponSettings.Current})");
        }
        else if (!WeaponSettings.AtDefaults)
        {
            settings.Defaults();
            c.Note($"the Weapons settings of this game are not the defaults ({WeaponSettings.Current}): the defaults are {settings.How} for this test");
        }
        return settings;
    }

    // The list rule (the one decision every patch of mine ask) over every crossbow the game knows: Crossbows skill
    // reload weapons. keep(name) = what the list must say under the settings of the moment. Real swaps are done with
    // two of them; this cover the rest.
    private static void CheckCrossbowList(Checks c, string label, Func<string, bool> keep)
    {
        var crossbows = 0;
        var wrong = new List<string>();
        foreach (var pair in ReloadWeapons())
        {
            if (pair.Value.m_skillType != Skills.SkillType.Crossbows || pair.Value.m_attack.m_consumeItem)
            {
                continue;
            }
            crossbows++;
            if (LoadedState.IsEligible(pair.Key, pair.Value) != keep(pair.Key))
            {
                wrong.Add(pair.Key);
            }
        }
        c.Check(crossbows >= 2 && wrong.Count == 0,
            $"{label}: the game has {crossbows} crossbow(s); the mod's keep list is wrong for: {string.Join(", ", wrong.ToArray())}");
    }

    // With the defaults on: every crossbow and the hook keep, Dundr not.
    private static void CheckDefaultList(Checks c)
    {
        var all = ReloadWeapons();
        CheckCrossbowList(c, "defaults", _ => true);
        c.Check(all.ContainsKey(HookName) && LoadedState.IsEligible(HookName, all[HookName]),
            $"defaults: {HookName} is {(all.ContainsKey(HookName) ? "not on the keep list" : "no reload weapon of this game")}");
        c.Check(all.ContainsKey(DundrName) && !LoadedState.IsEligible(DundrName, all[DundrName]),
            $"defaults: {DundrName} is {(all.ContainsKey(DundrName) ? "on the keep list" : "no reload weapon of this game")}");
        c.Note($"reload weapons of this game ({all.Count}): {string.Join(", ", new List<string>(all.Keys).ToArray())}");
    }

    // W01: defaults. Two crossbows and the grappling hook keep their load across a swap; Dundr reload and pay eitr
    // again.
    private static IEnumerator DefaultsBody(Rig rig, Checks c, bool real)
    {
        var settings = DefaultSettings(rig, c, real);
        CheckDefaultList(c);

        var second = SecondCrossbow();
        var bow = rig.Give(BowName);
        var arbalest = rig.Give(ArbalestName);
        var other = second != null ? rig.Give(second) : null;
        var hook = rig.Give(HookName);
        var dundr = rig.Give(DundrName);
        if (!c.Check(bow != null && arbalest != null && other != null && hook != null && dundr != null,
                $"could not give {BowName} ({bow != null}), {ArbalestName} ({arbalest != null}), a second crossbow '{second}' ({other != null}), "
                + $"{HookName} ({hook != null}) and {DundrName} ({dundr != null})"))
        {
            yield break;
        }
        rig.EmptyHands();
        rig.EitrOn();
        if (!c.Check(Game.m_eitrRate > 0f && ReloadEitr(dundr) > 0f,
                $"a reload of {DundrName} costs no eitr in this world (eitr rate {F(Game.m_eitrRate)}, reload eitr {F(ReloadEitr(dundr))}): 'costs eitr again' cannot be checked"))
        {
            yield break;
        }

        yield return Cycle(rig, c, arbalest, bow, true, "defaults");
        yield return Cycle(rig, c, other, bow, true, "defaults");
        yield return Cycle(rig, c, hook, bow, true, "defaults");
        yield return Cycle(rig, c, dundr, bow, false, "defaults");
        c.Summary = $"with the default Weapons settings ({(real ? "fresh cfg of the run" : settings.How)}) {ArbalestName}, {second} and {HookName} "
                    + $"were loaded at once after a swap to {BowName} and back, {DundrName} reloaded and used eitr again";
    }

    // W02: Dundr added to ExtraItems while in game: before it reload after a swap, after it keep its charge, no eitr.
    private static IEnumerator ExtraBody(Rig rig, Checks c, bool real)
    {
        var settings = rig.UseSettings(real);
        settings.Set(true, HookName, "");
        var bow = rig.Give(BowName);
        var dundr = rig.Give(DundrName);
        if (!c.Check(bow != null && dundr != null, $"could not give {BowName} ({bow != null}) and {DundrName} ({dundr != null})"))
        {
            yield break;
        }
        rig.EmptyHands();
        rig.EitrOn();
        if (!c.Check(Game.m_eitrRate > 0f && ReloadEitr(dundr) > 0f,
                $"a reload of {DundrName} costs no eitr in this world (eitr rate {F(Game.m_eitrRate)}): 'no eitr' cannot be told from a reload"))
        {
            yield break;
        }

        yield return Cycle(rig, c, dundr, bow, false, $"ExtraItems = {HookName}");

        // Changed while in game, weapon in hand: no restart, no new reload needed.
        settings.Set(true, ExtraWithDundr, "");
        if (real)
        {
            c.Check(Plugin.ExtraItems.Value == ExtraWithDundr, $"the ExtraItems setting reads '{Plugin.ExtraItems.Value}' after it was set");
        }
        yield return Cycle(rig, c, dundr, bow, true, $"ExtraItems = {ExtraWithDundr}");
        c.Summary = $"ExtraItems changed to '{ExtraWithDundr}' while in game ({settings.How}): {DundrName} reloaded after a swap before, "
                    + "and kept its charge with no eitr used after";
    }

    // W03: ExcludedItems = CrossbowArbalest: Arbalest reload after a swap, another crossbow keep; cleared: Arbalest keep.
    private static IEnumerator ExcludedBody(Rig rig, Checks c, bool real)
    {
        var settings = rig.UseSettings(real);
        settings.Set(true, HookName, ArbalestName);
        var second = SecondCrossbow();
        var bow = rig.Give(BowName);
        var arbalest = rig.Give(ArbalestName);
        var other = second != null ? rig.Give(second) : null;
        if (!c.Check(bow != null && arbalest != null && other != null,
                $"could not give {BowName} ({bow != null}), {ArbalestName} ({arbalest != null}) and a second crossbow '{second}' ({other != null})"))
        {
            yield break;
        }
        rig.EmptyHands();

        CheckCrossbowList(c, $"ExcludedItems = {ArbalestName}", name => name != ArbalestName);
        yield return Cycle(rig, c, arbalest, bow, false, $"ExcludedItems = {ArbalestName}");
        yield return Cycle(rig, c, other, bow, true, $"ExcludedItems = {ArbalestName}");

        settings.Set(true, HookName, "");
        if (real)
        {
            c.Check(Plugin.ExcludedItems.Value == "", $"the ExcludedItems setting reads '{Plugin.ExcludedItems.Value}' after it was cleared");
        }
        CheckCrossbowList(c, "ExcludedItems cleared", _ => true);
        yield return Cycle(rig, c, arbalest, bow, true, "ExcludedItems cleared");

        // What a player does: Arbalest already loaded and marked, then excluded while in game. Mark must stop counting.
        settings.Set(true, HookName, ArbalestName);
        yield return MarkedMustReload(rig, c, arbalest, bow, $"ExcludedItems = {ArbalestName} set while a loaded {ArbalestName} is in hand");
        c.Summary = $"ExcludedItems = {ArbalestName} ({settings.How}): {ArbalestName} reloaded after a swap while {second} kept its load; "
                    + $"cleared again: {ArbalestName} kept its load; excluded again with a loaded, marked {ArbalestName} in hand: it reloaded after the next swap";
    }

    // Weapon loaded in hand and marked (it was on the list a moment ago), setting just took it off the list. Other
    // weapon in hand, back: the mark still on the item must not count, the game must do a full reload. Cycle start
    // with a weapon that never got a mark under such a setting; this is the other road into the same rule.
    private static IEnumerator MarkedMustReload(Rig rig, Checks c, ItemDrop.ItemData weapon, ItemDrop.ItemData other, string label)
    {
        var p = rig.P;
        var s = new Swap();
        var name = PrefabOf(weapon);
        var loadTime = weapon.GetWeaponLoadingTime();
        if (!c.Check(ReferenceEquals(p.m_weaponLoaded, weapon) && LoadedState.HasStamp(weapon) && !LoadedState.IsEligible(weapon),
                $"{label}: set-up: {name} must be loaded in hand with a Loaded mark and off the mod's list (loaded in hand "
                + $"{ReferenceEquals(p.m_weaponLoaded, weapon)}, mark '{MarkOf(weapon)}', on the list {LoadedState.IsEligible(weapon)})"))
        {
            yield break;
        }
        yield return Take(rig, other, s, 0f);
        if (!c.Check(s.InHand && !p.IsItemEquiped(weapon) && p.m_weaponLoaded == null,
                $"{label}: swapping to {PrefabOf(other)} did not put {name} away (other weapon in hand {s.InHand}, {name} still equipped {p.IsItemEquiped(weapon)})"))
        {
            yield break;
        }
        // Today the mark stays on the item (nothing take it off at put away). Gone = reload below is still what must happen.
        if (!LoadedState.HasStamp(weapon))
        {
            c.Note($"{label}: the Loaded mark of {name} was taken off when it was put away; the reload after swapping back is checked all the same");
        }
        yield return Take(rig, weapon, s, loadTime + 4f);
        if (!c.Check(s.InHand, $"{label}: {name} did not come back into the hand"))
        {
            yield break;
        }
        c.Check(!s.LoadedAtOnce && s.ReloadQueued, $"{label}: the marked {name} did not start a reload after swapping back ({s})");
        c.Check(s.Loaded && s.Seconds >= 0.8f * loadTime,
            $"{label}: the reload of the marked {name} after swapping back took {F(s.Seconds)} s (loaded {s.Loaded}), a full reload takes {F(loadTime)} s");
        c.Note($"{label}: {name} back in hand: {s} (full reload {F(loadTime)} s)");
    }

    // W04: Crossbows = false: a crossbow reload after a swap, the hook keep; true again: the crossbow keep.
    private static IEnumerator CrossbowsOffBody(Rig rig, Checks c, bool real)
    {
        var settings = rig.UseSettings(real);
        settings.Set(false, HookName, "");
        var bow = rig.Give(BowName);
        var arbalest = rig.Give(ArbalestName);
        var hook = rig.Give(HookName);
        if (!c.Check(bow != null && arbalest != null && hook != null,
                $"could not give {BowName} ({bow != null}), {ArbalestName} ({arbalest != null}) and {HookName} ({hook != null})"))
        {
            yield break;
        }
        rig.EmptyHands();

        CheckCrossbowList(c, "Crossbows = false", _ => false);
        yield return Cycle(rig, c, arbalest, bow, false, "Crossbows = false");
        yield return Cycle(rig, c, hook, bow, true, "Crossbows = false");

        settings.Set(true, HookName, "");
        if (real)
        {
            c.Check(Plugin.KeepCrossbows.Value, "the Crossbows setting still reads false after it was set back to true");
        }
        CheckCrossbowList(c, "Crossbows = true again", _ => true);
        yield return Cycle(rig, c, arbalest, bow, true, "Crossbows = true again");

        // What a player does: crossbow already loaded and marked, then Crossbows turned off while in game.
        settings.Set(false, HookName, "");
        yield return MarkedMustReload(rig, c, arbalest, bow, $"Crossbows = false set while a loaded {ArbalestName} is in hand");
        c.Summary = $"Crossbows = false ({settings.How}): {ArbalestName} reloaded after a swap while {HookName} kept its load; "
                    + $"true again: {ArbalestName} kept its load; false again with a loaded, marked {ArbalestName} in hand: it reloaded after the next swap";
    }

    // ---------- W02 through the cfg file (multiplayer run only: the run has its own config folder) ----------

    private static IEnumerator RunMpCfgFile()
    {
        var c = new Checks(MpCfgFileName);
        var plugin = Self();
        if (plugin == null || !SelfTest.IsMultiplayerRun)
        {
            // Never in a single-player run: that would write the player's own cfg file.
            SelfTest.Fail(MpCfgFileName, "this test edits the cfg file and runs only in a multiplayer run (own config folder)");
            yield break;
        }
        var path = plugin.Config.ConfigFilePath;
        var value = Plugin.ExtraItems.Value;
        var original = new string[1];
        try
        {
            yield return CfgFileBody(c, path, original);
        }
        finally
        {
            Safe("cfg file", () =>
            {
                if (original[0] != null && File.ReadAllText(path) != original[0])
                {
                    File.WriteAllText(path, original[0]);
                }
                Plugin.ExtraItems.Value = value;
            });
        }
        c.Report();
    }

    private static IEnumerator CfgFileBody(Checks c, string path, string[] original)
    {
        var dundr = SharedOf(DundrName);
        if (!c.Check(dundr != null && File.Exists(path), $"no {DundrName} item ({dundr != null}) or no cfg file at '{path}'"))
        {
            yield break;
        }
        c.Check(WeaponSettings.AtDefaults && !LoadedState.IsEligible(DundrName, dundr),
            $"before the edit: settings {WeaponSettings.Current}, {DundrName} on the keep list {LoadedState.IsEligible(DundrName, dundr)}");

        original[0] = File.ReadAllText(path);
        var edited = EditSetting(original[0], "ExtraItems", ExtraWithDundr, out var found);
        if (!c.Check(found, "the cfg file has no 'ExtraItems = ...' line"))
        {
            yield break;
        }
        File.WriteAllText(path, edited);
        yield return Until(() => Plugin.ExtraItems.Value == ExtraWithDundr, 20f);
        c.Check(Plugin.ExtraItems.Value == ExtraWithDundr,
            $"20 s after 'ExtraItems = {ExtraWithDundr}' was written to the cfg file the setting still reads '{Plugin.ExtraItems.Value}'");
        c.Check(LoadedState.IsEligible(DundrName, dundr), $"after the cfg edit {DundrName} is not on the keep list");

        File.WriteAllText(path, original[0]);
        yield return Until(() => Plugin.ExtraItems.Value != ExtraWithDundr, 20f);
        c.Check(Plugin.ExtraItems.Value != ExtraWithDundr && !LoadedState.IsEligible(DundrName, dundr),
            $"20 s after the cfg file was put back the setting reads '{Plugin.ExtraItems.Value}', {DundrName} on the keep list {LoadedState.IsEligible(DundrName, dundr)}");
        c.Summary = $"'ExtraItems = {ExtraWithDundr}' typed into the cfg file while in game put {DundrName} on the keep list with no restart, and the old line took it off again";
    }

    // "Key = value" line of a BepInEx cfg text replaced (one setting per line).
    private static string EditSetting(string text, string key, string value, out bool found)
    {
        found = false;
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith(key + " =", StringComparison.Ordinal))
            {
                continue;
            }
            lines[i] = key + " = " + value + (lines[i].EndsWith("\r", StringComparison.Ordinal) ? "\r" : "");
            found = true;
        }
        return string.Join("\n", lines);
    }

    // ---------- W05: [reload weapon] log lines ----------

    private static bool ParseDumpLine(string line, out string prefab, out bool keeps)
    {
        const string tail = "keeps load (config) ";
        prefab = null;
        keeps = false;
        var start = DumpMark.Length;
        var colon = line.IndexOf(':', start);
        var at = line.LastIndexOf(tail, StringComparison.Ordinal);
        if (colon <= start || at < 0)
        {
            return false;
        }
        prefab = line.Substring(start, colon - start).Trim();
        return prefab.Length > 0 && bool.TryParse(line.Substring(at + tail.Length).Trim(), out keeps);
    }

    // real = multiplayer run: fresh default cfg, so the lines must say what TESTING.md W05 says. Single-player run with
    // other settings in the player's cfg: lines only compared with the list rule under those settings, and said.
    private static IEnumerator RunDebugList(string name, bool real)
    {
        var c = new Checks(name);
        var lines = _dump != null ? _dump.Lines() : new List<string>();
        if (!c.Check(lines.Count > 0, "no [reload weapon] line of the mod went to the log since the game started"))
        {
            c.Report();
            yield break;
        }
        var logged = new Dictionary<string, bool>(StringComparer.Ordinal);
        var unread = 0;
        foreach (var line in lines)
        {
            if (ParseDumpLine(line, out var prefab, out var keeps))
            {
                logged[prefab] = keeps;
            }
            else
            {
                unread++;
            }
        }
        c.Check(unread == 0, $"{unread} of {lines.Count} [reload weapon] line(s) do not end with 'keeps load (config) True/False'");

        // One line per reload weapon of the game, and each says what the list rule says (settings are the same as
        // at the first spawn: no test of mine leaves them changed).
        var all = ReloadWeapons();
        var missing = new List<string>();
        var differ = new List<string>();
        foreach (var pair in all)
        {
            if (!logged.TryGetValue(pair.Key, out var keeps))
            {
                missing.Add(pair.Key);
            }
            else if (keeps != LoadedState.IsEligible(pair.Key, pair.Value))
            {
                differ.Add($"{pair.Key} (line {keeps})");
            }
        }
        c.Check(missing.Count == 0, $"reload weapon(s) of the game without a [reload weapon] line: {string.Join(", ", missing.ToArray())}");
        c.Check(differ.Count == 0, $"line(s) that differ from the mod's list under the current settings: {string.Join(", ", differ.ToArray())}");

        var defaults = WeaponSettings.AtDefaults;
        if (real)
        {
            c.Check(defaults, $"the Weapons settings of this run are not the defaults ({WeaponSettings.Current})");
        }
        if (defaults)
        {
            var crossbows = 0;
            var wrong = new List<string>();
            foreach (var pair in all)
            {
                if (pair.Value.m_skillType != Skills.SkillType.Crossbows || pair.Value.m_attack.m_consumeItem)
                {
                    continue;
                }
                crossbows++;
                if (!logged.TryGetValue(pair.Key, out var keeps) || !keeps)
                {
                    wrong.Add(pair.Key);
                }
            }
            c.Check(crossbows >= 2 && wrong.Count == 0,
                $"with defaults: {crossbows} crossbow(s) in the game; line missing or 'keeps load (config) False' for: {string.Join(", ", wrong.ToArray())}");
            c.Check(logged.TryGetValue(HookName, out var hook) && hook, $"with defaults: the {HookName} line is missing or says False");
            c.Check(logged.TryGetValue(DundrName, out var dundr) && !dundr, $"with defaults: the {DundrName} line is missing or says True");
            c.Summary = $"{lines.Count} [reload weapon] lines in the log, default Weapons settings: True for all {crossbows} crossbows and {HookName}, False for {DundrName}";
        }
        else
        {
            c.Note($"the Weapons settings of this game are not the defaults ({WeaponSettings.Current}): the lines were only compared with the mod's list "
                   + "under these settings; what they say with defaults is checked by crossbow.mp.debug-list (fresh cfg)");
            c.Summary = $"{lines.Count} [reload weapon] lines in the log, each says what the mod's list says under the settings of this game (not the defaults)";
        }
        c.Report();
    }

    // ---------- W01 with real hotbar key presses ----------

    private static IEnumerator RunInput()
    {
        var rig = Rig.Create(InputName, 2);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(InputName);
        try
        {
            yield return InputBody(rig, c);
        }
        finally
        {
            SelfTest.ReleaseInput();
            rig.Restore();
        }
        c.Report();
    }

    // Key down, held, up, through the probe's input driver: the game read it like a press on the keyboard.
    private static IEnumerator PressKey(Checks c, KeyCode key, bool[] sent)
    {
        sent[0] = c.Check(SelfTest.Key(key, true), $"the press of {key} was not taken (no probe drives the input of this game)");
        if (!sent[0])
        {
            yield break;
        }
        yield return new WaitForSecondsRealtime(0.15f);
        SelfTest.Key(key, false);
        yield return null;
    }

    private static IEnumerator InputBody(Rig rig, Checks c)
    {
        var p = rig.P;
        var settings = rig.UseSettings(false);
        if (!WeaponSettings.AtDefaults)
        {
            settings.Defaults();
            c.Note($"the Weapons settings of this game are not the defaults: the defaults are {settings.How} for this test");
        }

        // Two free hotbar slots (row 0 of the inventory, keys 1 to 8: Player.Update, UseHotbarItem).
        var free = new List<int>();
        for (var x = 0; x < Mathf.Min(8, rig.Inv.GetWidth()) && free.Count < 2; x++)
        {
            if (rig.Inv.GetItemAt(x, 0) == null)
            {
                free.Add(x);
            }
        }
        if (!c.Check(free.Count == 2, $"the hotbar has {free.Count} free slot(s), the test needs 2"))
        {
            yield break;
        }
        var crossbow = rig.GiveAt(ArbalestName, free[0]);
        var bow = rig.GiveAt(BowName, free[1]);
        if (!c.Check(crossbow != null && bow != null && crossbow.m_gridPos.x == free[0] && crossbow.m_gridPos.y == 0
                     && bow.m_gridPos.x == free[1] && bow.m_gridPos.y == 0,
                $"could not put {ArbalestName} into hotbar slot {free[0] + 1} and {BowName} into slot {free[1] + 1}"))
        {
            yield break;
        }
        rig.EmptyHands();
        var crossbowKey = (KeyCode)((int)KeyCode.Alpha1 + free[0]);
        var bowKey = (KeyCode)((int)KeyCode.Alpha1 + free[1]);
        // Hotbar1 to Hotbar8 are fixed to the digit keys (ZInput keyboard set-up: not rebindable); shown text noted.
        c.Note($"hotbar slot {free[0] + 1}: key {crossbowKey}, the game shows it as '{ZInput.instance.GetBoundKeyString("Hotbar" + (free[0] + 1), true)}'; "
               + $"slot {free[1] + 1}: key {bowKey}, shown as '{ZInput.instance.GetBoundKeyString("Hotbar" + (free[1] + 1), true)}'");
        if (!c.Check(p.TakeInput(), "the game takes no player input now (a window, the console, the chat or the map is open)"))
        {
            yield break;
        }

        var s = new Swap();
        var sent = new bool[1];
        var loadTime = crossbow.GetWeaponLoadingTime();

        yield return PressKey(c, crossbowKey, sent);
        if (!sent[0])
        {
            yield break;
        }
        yield return Arrive(rig, crossbow, s, p.m_eitr, loadTime + 4f, 0f);
        if (!c.Check(s.InHand && s.Loaded && LoadedState.IsLoaded(crossbow),
                $"key {crossbowKey} did not bring {ArbalestName} into the hand and through its reload ({s})"))
        {
            yield break;
        }

        yield return PressKey(c, bowKey, sent);
        if (!sent[0])
        {
            yield break;
        }
        yield return Arrive(rig, bow, s, p.m_eitr, 0f, 0f);
        if (!c.Check(s.InHand && !p.IsItemEquiped(crossbow) && p.m_weaponLoaded == null,
                $"key {bowKey} did not swap to {BowName} ({BowName} in hand {s.InHand}, {ArbalestName} still equipped {p.IsItemEquiped(crossbow)})"))
        {
            yield break;
        }

        yield return PressKey(c, crossbowKey, sent);
        if (!sent[0])
        {
            yield break;
        }
        yield return Arrive(rig, crossbow, s, p.m_eitr, 1f, 0.5f);
        c.Check(s.InHand && s.LoadedAtOnce && !s.ReloadQueued,
            $"after the keys {crossbowKey}, {bowKey}, {crossbowKey} the {ArbalestName} was not loaded at once ({s})");
        c.Summary = $"real key presses {crossbowKey} (reload), {bowKey}, {crossbowKey}: {ArbalestName} came back loaded at once, no reload";
    }
}
#endif
