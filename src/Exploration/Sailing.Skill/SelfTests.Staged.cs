#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SailingSkillMod;

// Debug build only. Me = self tests that need a real log out or a real restart (same class as SelfTests.cs). One small
// test per thing, so one can be fixed or dropped alone:
//   sailing.relog.level        T11: Sailing 23 with progress, Skills panel read, real log out to the main menu and same
//                              world loaded again (relog test): same level, same progress (data and bar), definition and
//                              icon still there, mod active, then "resetskill Sailing" typed in the console: row gone;
//                              no warning or error of the mod all the way
//   sailing.disabled.level     L02: first game leaves Sailing 31 with progress on the character; second game started
//                              with every MC mod Enabled = false: status "Off (disabled in settings).", no ship patch,
//                              skill still known to the game, level and progress as left (loaded from the character
//                              file by the game itself), Skills panel row with level, own drawn icon, tooltip, bar;
//                              no warning or error of the mod since that game started
//   sailing.disabled.saved     L02: same game (mod off): a Sailing level written by the game's own character save
//                              (as autosave and log out do) is in the save data once and in the character FILE read
//                              back from disk: what the next start (mod on again) loads
//   sailing.disabled.first-log L03: second game reads the whole log of the first game (every test played, then quit
//                              from the world): no warning or error line of the mod, no error that names the mod's
//                              code. Fails when the tests of T01-T16 did not all pass in that game (filtered run:
//                              nothing played), or when the file holds no Unity line at all
// Relog test registered in OnActivated; disabled-start tests in BindConfig (mod never active in the second game).
// Character's own Sailing entry kept as text ("none" or "level|progress") and put back by the last part.
internal static partial class SelfTests
{
    private const string RelogLevelName = "sailing.relog.level";
    private const string DisabledLevelName = "sailing.disabled.level";
    private const string DisabledSavedName = "sailing.disabled.saved";
    private const string DisabledLogName = "sailing.disabled.first-log";

    private const string FirstLogFile = "LogOutput.first.log";   // tools/Test-InWorld.ps1 write it before the second game
    private const string UnityLogSource = "Unity Log";            // BepInEx name for Unity's own messages in the log file
    private const string OffStatus = "Off (disabled in settings).";

    // OnActivated (RegisterMore).
    private static void RegisterRelogTests()
    {
        SelfTest.RegisterRelog(RelogLevelName, RelogLevelBefore, null, RelogLevelAfter);
    }

    // OnDeactivated (UnregisterMore).
    private static void UnregisterRelogTests()
    {
        SelfTest.UnregisterRelog(RelogLevelName);
    }

    // BindConfig (RegisterAlways: every start). Never unregistered: in the second game the mod is off from the start.
    private static void RegisterStaged()
    {
        SelfTest.RegisterDisabledStart(DisabledLevelName, DisabledLevelLeave, DisabledLevelCheck);
        SelfTest.RegisterDisabledStart(DisabledSavedName, null, DisabledSavedCheck);
        SelfTest.RegisterDisabledStart(DisabledLogName, null, DisabledLogCheck);
    }

    // ---------- Sailing entry kept and put back ----------

    private static string SailingState(Player player)
    {
        return player.GetSkills().m_skillData.TryGetValue(SailingSkill.Type, out var skill) && skill != null
            ? N(skill.m_level) + "|" + N(skill.m_accumulator)
            : "none";
    }

    // Text of SailingState back on the character. Anything else (also no text at all) = no Sailing entry.
    private static void PutSailingState(Player player, string state)
    {
        var parts = (state ?? "").Split('|');
        if (parts.Length == 2
            && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var level)
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var progress))
        {
            SetSailing(player, level, progress);
            return;
        }
        player.GetSkills().ResetSkill(SailingSkill.Type);
        HelmSkill.Invalidate();
    }

    // Bytes of one skill record as vanilla Skills.Save write it: type, level, progress.
    private static byte[] SkillRecord(Skills.SkillType type, float level, float progress)
    {
        var record = new ZPackage();
        record.Write((int)type);
        record.Write(level);
        record.Write(progress);
        return record.GetArray();
    }

    private static void HidePanelIfShown()
    {
        if (InventoryGui.instance != null && InventoryGui.IsVisible())
        {
            ClosePanel();
        }
    }

    // ---------- sailing.relog.level (T11) ----------

    private const float RelogLevel = 23f;
    private const float RelogXp = 3.3f;

    private static string _relogState = "none";   // character's own Sailing entry before the test
    private static bool _relogLeft;               // part before the log out left its level
    private static int _relogMark;
    private static float _relogBar = -1f;         // "next level" bar of the Skills panel before the log out

    private static IEnumerator RelogLevelBefore()
    {
        var c = new Checks(RelogLevelName);
        var player = Player.m_localPlayer;
        if (player == null || player.GetSkills() == null)
        {
            SelfTest.Fail(RelogLevelName, "before the log out: no local player");
            yield break;
        }
        // Part cut short (throw, timeout) = probe end the test, the part after never run: me put the level back here.
        var handedOver = false;
        try
        {
            _relogLeft = false;
            _relogBar = -1f;
            _relogMark = LogMark();
            _relogState = SailingState(player);
            var view = FeatureRegistry.Find(ModInfo.Guid);
            c.Check(view != null && view.Value.IsActive, $"the mod is not active before the log out ({(view != null ? view.Value.State : "not registered")})");
            var skill = SetSailing(player, RelogLevel, RelogXp);
            _relogLeft = true;
            c.Check(skill.GetLevelPercentage() > 0f && skill.GetLevelPercentage() < 1f,
                $"Sailing {F(RelogLevel)} with {F(RelogXp)} XP is not 'some progress in the bar' ({F(skill.GetLevelPercentage())})");
            yield return ShowInventory(c);
            var rows = OpenPanel();
            var row = FindRow(rows, SailingSkill.DisplayName);
            _relogBar = row != null ? row.Progress : -1f;
            // Bar value must be readable (GuiBar.m_value): a bar me cannot read prove nothing after the log in.
            c.Check(row != null && row.Level == "23" && row.Progress > 0f,
                $"before the log out the Skills panel shows Sailing '{(row != null ? row.Level : "no row")}' with bar {(row != null ? F(row.Progress) : "?")}, "
                + "expected 23 with some progress (-1 = the bar could not be read)");
            yield return null;
            yield return null;
            ClosePanel();
            yield return null;
            yield return null;
            c.Report($" before the log out: Sailing {F(RelogLevel)} with progress {F(RelogXp)} on the character (before the test: {_relogState})");
            handedOver = true;
        }
        finally
        {
            HidePanelIfShown();
            if (!handedOver && _relogLeft)
            {
                try
                {
                    PutSailingState(player, _relogState);
                }
                catch (Exception e)
                {
                    Log.Error($"Sailing self test clean-up failed: {e}");
                }
                _relogLeft = false;
            }
        }
    }

    private static IEnumerator RelogLevelAfter()
    {
        var c = new Checks(RelogLevelName);
        var player = Player.m_localPlayer;
        if (player == null || player.GetSkills() == null)
        {
            SelfTest.Fail(RelogLevelName, "after logging back in: no local player");
            yield break;
        }
        var skills = player.GetSkills();
        var type = SailingSkill.Type;
        try
        {
            if (!c.Check(_relogLeft, "the part before the log out left no Sailing level: nothing to compare after logging back in"))
            {
                c.Report();
                yield break;
            }
            // Same level and progress, in the character loaded from the save.
            var have = skills.m_skillData.TryGetValue(type, out var skill) && skill != null;
            c.Check(have && skill.m_level == RelogLevel && skill.m_accumulator == RelogXp,
                $"after logging back in Sailing is {(have ? F(skill.m_level) + " with progress " + F(skill.m_accumulator) : "missing")}, "
                + $"expected {F(RelogLevel)} with progress {F(RelogXp)}");
            c.Check(have && ReferenceEquals(skill.m_info, SailingSkill.Def) && ReferenceEquals(skills.GetSkillDef(type), SailingSkill.Def),
                "after logging back in the Sailing entry does not carry the mod's skill definition");
            var view = FeatureRegistry.Find(ModInfo.Guid);
            c.Check(view != null && view.Value.IsActive, $"the mod is not active after logging back in ({(view != null ? view.Value.State : "not registered")})");

            // Same row in the Skills panel, bar included.
            yield return ShowInventory(c);
            var rows = OpenPanel();
            if (c.Check(rows != null, "no inventory UI after logging back in: the Skills panel was not read"))
            {
                var row = FindRow(rows, SailingSkill.DisplayName);
                var icon = SailingSkill.Def.m_icon;
                c.Check(row != null && row.Level == "23" && icon != null && row.Icon == icon,
                    $"after logging back in the Skills panel shows Sailing '{(row != null ? row.Level : "no row")}', expected 23 with the mod's icon");
                c.Check(row != null && _relogBar > 0f && Near(row.Progress, _relogBar, 1e-4f),
                    $"after logging back in the Sailing bar is at {(row != null ? F(row.Progress) : "?")}, before the log out {F(_relogBar)} (-1 = the bar could not be read)");
                yield return null;
                yield return null;

                // "resetskill Sailing" typed in the console of the new session: the row is gone.
                var printed = RunConsole("resetskill Sailing");
                c.Check(printed != null && printed.Contains("Skill Sailing reset") && !skills.m_skillData.ContainsKey(type),
                    $"'resetskill Sailing' after logging back in did not remove the skill (console: '{Join(printed)}')");
                HelmSkill.Invalidate();
                rows = OpenPanel();
                c.Check(rows != null && rows.All(r => r.Name != SailingSkill.DisplayName),
                    "the Skills panel still has a Sailing row after 'resetskill Sailing'");
                yield return null;
                yield return null;
                ClosePanel();
            }
            CheckQuiet(c, _relogMark);
            c.Report(" after logging back in: same level, same progress, row gone after the reset");
        }
        finally
        {
            HidePanelIfShown();
            try
            {
                PutSailingState(player, _relogState);
            }
            catch (Exception e)
            {
                Log.Error($"Sailing self test clean-up failed: {e}");
            }
            _relogLeft = false;
        }
    }

    // ---------- sailing.disabled.level (L02) ----------

    private const float DisabledLevel = 31f;
    private const float DisabledXp = 2.5f;
    private const string DisabledBeforeKey = DisabledLevelName + ".before";

    // First game, last thing before the quit: a Sailing level with progress on the character (saved by the quit).
    private static IEnumerator DisabledLevelLeave()
    {
        var player = Player.m_localPlayer;
        if (player == null || player.GetSkills() == null)
        {
            SelfTest.Fail(DisabledLevelName, "first game: no local player");
            yield break;
        }
        var before = SailingState(player);
        var carried = SelfTest.Carry(DisabledBeforeKey, before);
        var skill = SetSailing(player, DisabledLevel, DisabledXp);
        yield return null;
        var view = FeatureRegistry.Find(ModInfo.Guid);
        var state = view != null ? view.Value.State : "not registered";
        if (carried && skill.m_level == DisabledLevel && skill.m_accumulator == DisabledXp)
        {
            SelfTest.Pass(DisabledLevelName,
                $"first game (mod {state}): Sailing {F(DisabledLevel)} with progress {F(DisabledXp)} left on the character for the restart (before: {before})");
        }
        else
        {
            SelfTest.Fail(DisabledLevelName,
                $"first game: could not leave a Sailing level for the restart (value kept for the second game: {carried}, Sailing now {SailingState(player)})");
        }
    }

    // Second game: Enabled = false in the cfg since before the start.
    private static IEnumerator DisabledLevelCheck()
    {
        var c = new Checks(DisabledLevelName);
        var player = Player.m_localPlayer;
        if (player == null || player.GetSkills() == null || Localization.instance == null)
        {
            SelfTest.Fail(DisabledLevelName, "second game: no local player");
            yield break;
        }
        var skills = player.GetSkills();
        var type = SailingSkill.Type;
        try
        {
            var view = FeatureRegistry.Find(ModInfo.Guid);
            c.Check(SelfTest.Stage == SelfTest.DisabledStage && view != null && !view.Value.IsActive
                    && view.Value.State == nameof(ModState.Disabled) && view.Value.Status == OffStatus,
                $"started with Enabled = false: stage '{SelfTest.Stage}', state {(view != null ? view.Value.State : "not registered")}, "
                + $"status '{(view != null ? view.Value.Status : "?")}' (expected Disabled, '{OffStatus}')");
            c.Check(OwnShipPrefixes() == 0, "the mod's ship patches are applied although it is off");

            // The skill stays a real skill for the game (registration is always on), with the level the first game left.
            c.Check(Skills.IsSkillValid(type) && ReferenceEquals(skills.GetSkillDef(type), SailingSkill.Def)
                    && skills.m_skills.Count(d => d != null && d.m_skill == type) == 1,
                "with the mod off the game does not know the Sailing skill (exactly one definition expected)");
            var have = skills.m_skillData.TryGetValue(type, out var skill) && skill != null;
            c.Check(have && skill.m_level == DisabledLevel && skill.m_accumulator == DisabledXp && ReferenceEquals(skill.m_info, SailingSkill.Def),
                $"with the mod off since the start Sailing is {(have ? F(skill.m_level) + " with progress " + F(skill.m_accumulator) : "missing")}, "
                + $"expected {F(DisabledLevel)} with progress {F(DisabledXp)} as the first game left it");

            // Still listed in the Skills panel with its level and icon.
            yield return ShowInventory(c);
            var rows = OpenPanel();
            if (c.Check(rows != null, "no inventory UI: the Skills panel was not read"))
            {
                var row = FindRow(rows, SailingSkill.DisplayName);
                var icon = SailingSkill.Def.m_icon;
                c.Check(row != null && row.Level == "31",
                    $"with the mod off the Skills panel shows Sailing '{(row != null ? row.Level : "no row (or two)")}', expected 31; rows: "
                    + string.Join(", ", rows.Select(r => r.Name).ToArray()));
                c.Check(row != null && icon != null && row.Icon == icon,
                    $"with the mod off the Sailing row shows icon '{(row != null && row.Icon != null ? row.Icon.name : "none")}', expected the mod's own");
                var drawn = IconDrawn(row != null ? row.Icon : null, out var picture);
                c.Check(drawn, "with the mod off the Sailing row's icon is not the drawn sail: " + picture);
                c.Check(row != null && row.Progress > 0f,
                    $"with the mod off the Sailing bar is at {(row != null ? F(row.Progress) : "?")}, expected some progress (-1 = the bar could not be read)");
                c.Check(row != null && row.Tooltip == "$" + SailingSkill.DescriptionKey
                        && Localization.instance.Localize(row.Tooltip) == SailingSkill.Description,
                    "with the mod off the Sailing row's tooltip is not the skill's description");
                // One picture for the human who checks the look.
                yield return new WaitForSeconds(0.7f);
                SelfTest.Screenshot(DisabledLevelName, "skills");
                yield return null;
                yield return null;
                ClosePanel();
            }

            // No error in the log: watcher listens since plugin start of this game.
            if (c.Check(_watch != null, "the log watcher is not running"))
            {
                // Watcher that hear nothing prove nothing: the mod's ready line come after the watcher start, mod on or off.
                var heard = MyLines(0, BepInEx.Logging.LogLevel.Info).Count(l => l.StartsWith(Log.ReadyMarker, StringComparison.Ordinal));
                c.Check(heard > 0, $"the log watcher heard no '{Log.ReadyMarker}' line of {ModInfo.Name} since this game started: its count of warnings and errors proves nothing");
                var count = _watch.MyProblemCount;
                c.Check(count == 0, $"{count} warning(s) or error(s) from {ModInfo.Name} since the game started with the mod off: {Join(_watch.MyProblems(), 5)}");
            }
            c.Report(" (game started with every MC mod Enabled = false)");
        }
        finally
        {
            HidePanelIfShown();
            try
            {
                PutSailingState(player, SelfTest.Carried(DisabledBeforeKey));
            }
            catch (Exception e)
            {
                Log.Error($"Sailing self test clean-up failed: {e}");
            }
        }
    }

    // ---------- sailing.disabled.saved (L02) ----------

    private const float SavedLevel = 44f;
    private const float SavedXp = 4.4f;

    // The game's own character save now (vanilla Game.SavePlayerProfile: what autosave and log out call). Null = done.
    private static string SaveCharacterNow()
    {
        try
        {
            Game.instance.SavePlayerProfile(false);
            return null;
        }
        catch (Exception e)
        {
            return e.GetType().Name + ": " + e.Message;
        }
    }

    // Character file of this profile read from disk into a new profile object (nothing of the running game touched):
    // how many times the record is in its player data. -1 = file not read (why say it).
    private static int RecordsInCharacterFile(PlayerProfile profile, byte[] record, out string why)
    {
        why = "";
        try
        {
            var onDisk = new PlayerProfile(profile.GetFilename(), profile.m_fileSource);
            if (!onDisk.Load() || onDisk.m_playerData == null)
            {
                why = "the character file could not be loaded or holds no player data";
                return -1;
            }
            return CountBytes(onDisk.m_playerData, record);
        }
        catch (Exception e)
        {
            why = e.GetType().Name + ": " + e.Message;
            return -1;
        }
    }

    // Common part of sailing.disabled.saved and sailing.mp.vanilla-server.saved: level on the character, save data,
    // character file. Caller puts the character's own Sailing entry back and saves again.
    private static void CheckLevelReachesFile(Checks c, Player player, PlayerProfile profile, string when)
    {
        var type = SailingSkill.Type;
        SetSailing(player, SavedLevel, SavedXp);
        var record = SkillRecord(type, SavedLevel, SavedXp);
        var what = $"'Sailing, level {F(SavedLevel)}, progress {F(SavedXp)}'";
        var data = new ZPackage();
        player.Save(data);
        var inData = CountBytes(data.GetArray(), record);
        c.Check(inData == 1, $"{when}: the character's save data holds the record {what} {inData} times, expected once");
        var error = SaveCharacterNow();
        if (!c.Check(error == null, $"{when}: the game's character save threw {error}"))
        {
            return;
        }
        var inFile = RecordsInCharacterFile(profile, record, out var why);
        c.Check(inFile == 1,
            $"{when}: the character file '{profile.GetFilename()}' written by the game holds the record {what} {inFile} times, expected once"
            + (why.Length > 0 ? " (" + why + ")" : ""));
    }

    private static IEnumerator DisabledSavedCheck()
    {
        var c = new Checks(DisabledSavedName);
        var player = Player.m_localPlayer;
        var profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
        if (player == null || player.GetSkills() == null || profile == null)
        {
            SelfTest.Fail(DisabledSavedName, "second game: no local player or no character profile");
            yield break;
        }
        var before = SailingState(player);
        var touched = false;
        try
        {
            var view = FeatureRegistry.Find(ModInfo.Guid);
            if (!c.Check(SelfTest.Stage == SelfTest.DisabledStage && view != null && view.Value.State == nameof(ModState.Disabled),
                    $"this is not a game started with Enabled = false (stage '{SelfTest.Stage}', state {(view != null ? view.Value.State : "not registered")})"))
            {
                c.Report();
                yield break;
            }
            touched = true;
            CheckLevelReachesFile(c, player, profile, "mod off since the start");
            yield return null;
            c.Report(" (the character file written with the mod off holds the level and progress: what the next start loads)");
        }
        finally
        {
            if (touched)
            {
                try
                {
                    PutSailingState(player, before);
                    var error = SaveCharacterNow();
                    if (error != null)
                    {
                        Log.Error($"Sailing self test clean-up failed: character save threw {error}");
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"Sailing self test clean-up failed: {e}");
                }
            }
        }
    }

    // ---------- sailing.disabled.first-log (L03) ----------

    // BepInEx log file line: "[Warning:Sailing Skill] text" (level padded to 7, source to 10). Lines without that head
    // belong to the entry above (stack traces).
    private static readonly Regex LogHead = new Regex(@"^\[(?<level>[A-Za-z]+)\s*:\s*(?<source>[^\]]*?)\s*\] ?(?<text>.*)$", RegexOptions.Compiled);

    // Tests that play TESTING.md T01 to T16 (the ones their "Automated" notes name, single-player run). L03 talks about
    // the log of that play-through: each must run to its end and pass in the first game. Else a filtered run (-Only)
    // with nothing played reads as a clean log. T01's real Tab press (sailing.input.tab) and T11's real log out
    // (sailing.relog.level) are part of the play-through too: both run in the first game, before the quit.
    private static readonly string[] PlayThroughTests =
    {
        SkillName, PanelName, MathName, HelmName, XpName, HudName, MapName, DamageName, RamName, ReachName, UpwindName,
        PaddleName, HeelName, CrewName, InputTabName, RelogLevelName,
    };

    private static IEnumerator DisabledLogCheck()
    {
        var c = new Checks(DisabledLogName);
        var dir = AppDomain.CurrentDomain.GetData(SelfTest.RunDirSlot) as string;
        var path = string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, FirstLogFile);
        if (!c.Check(path != null && File.Exists(path),
                $"the copy of the first game's log was not found ({path ?? "no run folder"}): tools/Test-InWorld.ps1 writes it before the second game"))
        {
            c.Report();
            yield break;
        }
        string[] lines = null;
        string readError = null;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception e)
        {
            readError = e.Message;
        }
        if (!c.Check(lines != null, $"could not read {path}: {readError}"))
        {
            c.Report();
            yield break;
        }
        yield return null;

        var myCode = typeof(Plugin).Namespace + ".";
        var endHead = SelfTest.Prefix + " END ";
        var problems = new List<string>();
        var passedTests = new HashSet<string>();
        var mine = 0;
        var mineAfterDone = 0;
        var unityLines = 0;
        var doneAt = -1;
        var entryError = false;   // entry above is an error of another source
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var head = LogHead.Match(line);
            if (!head.Success)
            {
                // Continuation (stack trace) of the entry above.
                if (entryError && (line.IndexOf(myCode, StringComparison.Ordinal) >= 0 || line.IndexOf(ModInfo.Guid, StringComparison.Ordinal) >= 0))
                {
                    problems.Add($"line {i + 1}: error naming the mod's code: {FirstLine(line.Trim())}");
                    entryError = false;   // one line per stack
                }
                continue;
            }
            var level = head.Groups["level"].Value;
            var source = head.Groups["source"].Value;
            var text = head.Groups["text"].Value;
            var bad = level == "Warning" || level == "Error" || level == "Fatal";
            var error = level == "Error" || level == "Fatal";
            entryError = false;
            if (text.IndexOf(SelfTest.Prefix + " DONE ", StringComparison.Ordinal) >= 0)
            {
                doneAt = i;
            }
            if (source == UnityLogSource)
            {
                unityLines++;
            }
            // Probe's line "[selftest] END <name>: passed (...)": that test ran to its end in the first game.
            if (source != ModInfo.Name && text.StartsWith(endHead, StringComparison.Ordinal))
            {
                var colon = text.IndexOf(": ", endHead.Length, StringComparison.Ordinal);
                if (colon > endHead.Length && string.CompareOrdinal(text, colon + 2, "passed", 0, 6) == 0)
                {
                    passedTests.Add(text.Substring(endHead.Length, colon - endHead.Length));
                }
            }
            if (source == ModInfo.Name)
            {
                mine++;
                if (doneAt >= 0)
                {
                    mineAfterDone++;
                }
                // Results of the tests themselves ("[selftest] FAIL ...") are judged by the run, not here.
                if (bad && !text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal))
                {
                    problems.Add($"line {i + 1}: {level}: {FirstLine(text)}");
                }
                continue;
            }
            // Error of another source (test result lines of other mods and of the probe aside: the run judges them).
            if (error && !text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal))
            {
                if (text.IndexOf(myCode, StringComparison.Ordinal) >= 0 || text.IndexOf(ModInfo.Guid, StringComparison.Ordinal) >= 0)
                {
                    problems.Add($"line {i + 1}: error naming the mod's code: {FirstLine(text)}");
                }
                else
                {
                    entryError = true;
                }
            }
        }
        c.Note($"first game's log: {lines.Length} lines, {mine} from {ModInfo.Name}, {mineAfterDone} of them after the tests ended (the quit from the world)");
        foreach (var problem in problems.Take(10))
        {
            c.Note("log: " + problem);
        }
        c.Check(mine > 0 && doneAt >= 0,
            $"the first game's log does not hold a whole run ({mine} lines from {ModInfo.Name}, tests-ended line found {doneAt >= 0})");
        var notPlayed = PlayThroughTests.Where(t => !passedTests.Contains(t)).ToList();
        c.Check(notPlayed.Count == 0,
            $"{notPlayed.Count} of the {PlayThroughTests.Length} tests that play TESTING.md T01 to T16 did not run to their end and pass in the first game "
            + $"({string.Join(", ", notPlayed.Take(6).ToArray())}{(notPlayed.Count > 6 ? ", ..." : "")}): this log is not the log of a whole play-through "
            + "(run tools/Test-InWorld.ps1 without -Only, or with -Only sailing.)");
        c.Check(unityLines > 0,
            "the first game's log file holds no line of Unity itself: BepInEx does not write them to the file ([Logging.Disk] WriteUnityLog = true "
            + "in BepInEx.cfg is needed), so an exception in the mod's code that only Unity reports would not show here");
        c.Check(problems.Count == 0,
            $"{problems.Count} warning or error line(s) from {ModInfo.Name}, or error(s) naming the mod's code, in the whole first game, quit included"
            + (problems.Count > 0 ? ", first: " + problems[0] : ""));
        c.Report(" (whole log of the first game, quit from the world included)");
    }
}
#endif
