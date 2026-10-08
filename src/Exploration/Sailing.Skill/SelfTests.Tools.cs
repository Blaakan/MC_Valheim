#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MC.Exploration.SailingSkillMod;

// Debug build only. Me = tools of the self tests (same class as SelfTests.cs):
//   LogWatch      BepInEx log listener from plugin start to quit: every line of this mod (Debug lines too, whatever the
//                 log file level), and warnings/errors of every source. Tests read "since mark".
//   MessageSpy    HUD messages (top left queue with icon, centre text), polled each frame by the test.
//   Panel         Skills panel opened like the player do it (inventory, Skills button): rows read from the UI objects.
//   RunConsole    console command through Terminal.TryRunCommand (cheats on only for the call, profile cheat marks
//                 put back).
//   WindScope     fixed wind at once (vanilla debug wind fields), put back after.
internal static partial class SelfTests
{
    // ---------- log watch ----------

    private struct LogLine
    {
        internal LogLevel Level;
        internal string Source;
        internal string Text;
    }

    private sealed class LogWatch : ILogListener
    {
        private const int Max = 6000;
        private const int Cut = 2000;

        private readonly object _gate = new object();
        private readonly List<LogLine> _lines = new List<LogLine>();
        private readonly List<string> _mine = new List<string>();
        private int _dropped;
        private int _mineCount;

        // Warnings and errors of this mod since game start (me keep first 50 as text), never cut.
        internal int MyProblemCount
        {
            get
            {
                lock (_gate)
                {
                    return _mineCount;
                }
            }
        }

        internal List<string> MyProblems()
        {
            lock (_gate)
            {
                return new List<string>(_mine);
            }
        }

        // Place in the stream: lines after it are "since mark".
        internal int Mark()
        {
            lock (_gate)
            {
                return _dropped + _lines.Count;
            }
        }

        internal List<LogLine> Since(int mark)
        {
            lock (_gate)
            {
                var from = Mathf.Max(0, mark - _dropped);
                return from >= _lines.Count ? new List<LogLine>() : _lines.GetRange(from, _lines.Count - from);
            }
        }

        // Log events come from any thread: lock, never throw.
        public void LogEvent(object sender, LogEventArgs e)
        {
            try
            {
                if (e == null)
                {
                    return;
                }
                var source = e.Source != null ? e.Source.SourceName : "";
                var mine = source == ModInfo.Name;
                var bad = (e.Level & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) != 0;
                if (!mine && !bad)
                {
                    return;
                }
                var text = e.Data as string ?? (e.Data != null ? e.Data.ToString() : "");
                // Test result lines (FAIL = error line of this source) are no mod problem: me skip them.
                if (mine && text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal))
                {
                    return;
                }
                lock (_gate)
                {
                    if (mine && bad)
                    {
                        _mineCount++;
                        if (_mine.Count < 50)
                        {
                            _mine.Add(e.Level + ": " + FirstLine(text));
                        }
                    }
                    _lines.Add(new LogLine { Level = e.Level, Source = source, Text = text });
                    if (_lines.Count > Max)
                    {
                        _lines.RemoveRange(0, Cut);
                        _dropped += Cut;
                    }
                }
            }
            catch
            {
                // Me never throw inside the logger.
            }
        }

        public void Dispose()
        {
        }
    }

    private static LogWatch _watch;

    private static void StartWatch()
    {
        if (_watch != null)
        {
            return;
        }
        _watch = new LogWatch();
        BepInEx.Logging.Logger.Listeners.Add(_watch);
    }

    private static int LogMark() => _watch != null ? _watch.Mark() : 0;

    // Lines of this mod at one level since the mark.
    private static List<string> MyLines(int mark, LogLevel level)
    {
        var result = new List<string>();
        if (_watch == null)
        {
            return result;
        }
        foreach (var line in _watch.Since(mark))
        {
            if (line.Source == ModInfo.Name && line.Level == level)
            {
                result.Add(line.Text);
            }
        }
        return result;
    }

    private static int CountLogged(int mark, LogLevel level, string part)
    {
        var count = 0;
        foreach (var text in MyLines(mark, level))
        {
            if (text.IndexOf(part, StringComparison.Ordinal) >= 0)
            {
                count++;
            }
        }
        return count;
    }

    private static bool LoggedExact(int mark, LogLevel level, string text) => MyLines(mark, level).Contains(text);

    // Warnings and errors of one log source since the mark ("level: text").
    private static List<string> Problems(int mark, string source)
    {
        var result = new List<string>();
        if (_watch == null)
        {
            return result;
        }
        foreach (var line in _watch.Since(mark))
        {
            if (line.Source == source && (line.Level & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) != 0)
            {
                result.Add(line.Level + ": " + FirstLine(line.Text));
            }
        }
        return result;
    }

    private static string FirstLine(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }
        var cut = text.IndexOf('\n');
        var line = cut > 0 ? text.Substring(0, cut).TrimEnd() : text;
        return line.Length > 300 ? line.Substring(0, 300) + "..." : line;
    }

    private static string Join(List<string> lines, int max = 3)
    {
        if (lines == null || lines.Count == 0)
        {
            return "none";
        }
        var shown = lines.Count > max ? lines.GetRange(0, max) : lines;
        return string.Join(" | ", shown.ToArray()) + (lines.Count > max ? $" (+{lines.Count - max} more)" : "");
    }

    // ---------- HUD messages ----------

    // Vanilla keep top-left messages in a queue (one shown per second) and the centre text in one label. Me look every
    // frame: each message object is seen in the queue or as the current one for at least one frame.
    // Queue message type = private class of the game: me read it by reflection, never name the type.
    private sealed class MessageSpy
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static readonly FieldInfo QueueField = typeof(MessageHud).GetField("m_msgQeue", Any);
        private static readonly FieldInfo CurrentField = typeof(MessageHud).GetField("currentMsg", Any);
        private static FieldInfo _textField;
        private static FieldInfo _iconField;

        private readonly HashSet<object> _seen = new HashSet<object>();
        private string _centre;

        // False = game message queue not found (renamed): me cannot read top-left messages.
        internal static bool CanRead => QueueField != null && CurrentField != null;

        internal readonly List<KeyValuePair<string, Sprite>> TopLeft = new List<KeyValuePair<string, Sprite>>();
        internal readonly List<string> Centre = new List<string>();

        // Messages already there are not mine. Me wipe old centre text: same text again would not show as new.
        internal MessageSpy()
        {
            var hud = MessageHud.instance;
            if (hud == null)
            {
                return;
            }
            foreach (var m in Queue(hud))
            {
                _seen.Add(m);
            }
            var current = CurrentField != null ? CurrentField.GetValue(hud) : null;
            if (current != null)
            {
                _seen.Add(current);
            }
            if (hud.m_messageCenterText != null)
            {
                hud.m_messageCenterText.text = "";
            }
            _centre = "";
        }

        internal void Poll()
        {
            var hud = MessageHud.instance;
            if (hud == null)
            {
                return;
            }
            foreach (var m in Queue(hud))
            {
                Add(m);
            }
            Add(CurrentField != null ? CurrentField.GetValue(hud) : null);
            var centre = hud.m_messageCenterText != null ? hud.m_messageCenterText.text : "";
            if (centre != _centre)
            {
                _centre = centre;
                if (!string.IsNullOrEmpty(centre))
                {
                    Centre.Add(centre);
                }
            }
        }

        // Copy: the game may change its queue while me walk it.
        private static List<object> Queue(MessageHud hud)
        {
            var result = new List<object>();
            if (QueueField != null && QueueField.GetValue(hud) is IEnumerable queue)
            {
                foreach (var m in queue)
                {
                    result.Add(m);
                }
            }
            return result;
        }

        private void Add(object m)
        {
            if (m == null || !_seen.Add(m))
            {
                return;
            }
            if (_textField == null)
            {
                _textField = m.GetType().GetField("m_text", Any);
                _iconField = m.GetType().GetField("m_icon", Any);
            }
            var text = _textField != null ? _textField.GetValue(m) as string : null;
            var icon = _iconField != null ? _iconField.GetValue(m) as Sprite : null;
            TopLeft.Add(new KeyValuePair<string, Sprite>(text ?? "", icon));
        }

        internal bool SawTopLeft(string text, Sprite icon)
        {
            foreach (var pair in TopLeft)
            {
                if (pair.Key == text && pair.Value == icon)
                {
                    return true;
                }
            }
            return false;
        }

        internal bool SawTopLeft(string text)
        {
            foreach (var pair in TopLeft)
            {
                if (pair.Key == text)
                {
                    return true;
                }
            }
            return false;
        }
    }

    // ---------- Skills panel ----------

    private sealed class PanelRow
    {
        internal string Name;
        internal string Level;
        internal Sprite Icon;
        internal string Tooltip;
        internal float Progress = -1f;   // value of the "next level" bar, -1 = could not read
    }

    private static readonly FieldInfo GuiBarValue =
        typeof(GuiBar).GetField("m_value", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    // Me show the inventory like the player (Tab) and wait until the Skills dialog can run. Vanilla SkillsDialog.Setup
    // start a coroutine on the dialog; the inventory animator switch the dialog's parent on only on a later frame, and
    // Unity log the error "Coroutine couldn't be started because the the game object 'Skills' is inactive!" when
    // OnOpenSkills come in the same frame as Show (no player click that fast). Call before OpenPanel. Bounded: 2 s,
    // then the caller go on and a NOTE say so.
    private static IEnumerator ShowInventory(Checks c)
    {
        var gui = InventoryGui.instance;
        if (gui == null || gui.m_skillsDialog == null || Player.m_localPlayer == null)
        {
            yield break;
        }
        // Vanilla IsVisible stay true one frame after Hide: me ask the animator too (hidden this very frame = show again).
        if (!InventoryGui.IsVisible() || !gui.m_animator.GetBool("visible"))
        {
            gui.Show(null);
        }
        var parent = gui.m_skillsDialog.transform.parent;
        var until = Time.realtimeSinceStartup + 2f;
        while (parent != null && !parent.gameObject.activeInHierarchy && Time.realtimeSinceStartup < until)
        {
            yield return null;
            if (!gui.m_animator.GetBool("visible"))
            {
                gui.Show(null);   // something closed it meanwhile (vanilla does when the player teleports or dies)
            }
        }
        if (parent != null && !parent.gameObject.activeInHierarchy)
        {
            c.Note("the inventory did not come up within 2 s: the Skills panel is read while it is hidden");
        }
    }

    // Me press the inventory's Skills button (vanilla InventoryGui.OnOpenSkills), read every shown row. Inventory shown
    // first with ShowInventory (here only as a last resort). Null = no UI. Caller close it with ClosePanel.
    private static List<PanelRow> OpenPanel()
    {
        var gui = InventoryGui.instance;
        if (gui == null || gui.m_skillsDialog == null || Player.m_localPlayer == null)
        {
            return null;
        }
        if (!InventoryGui.IsVisible())
        {
            gui.Show(null);
        }
        gui.OnOpenSkills();
        var rows = new List<PanelRow>();
        foreach (var element in gui.m_skillsDialog.m_elements)
        {
            if (element == null || !element.activeSelf)
            {
                continue;
            }
            var row = new PanelRow();
            var name = Utils.FindChild(element.transform, "name");
            var level = Utils.FindChild(element.transform, "leveltext");
            var icon = Utils.FindChild(element.transform, "icon");
            var bar = Utils.FindChild(element.transform, "currentlevel");
            row.Name = name != null && name.GetComponent<TMP_Text>() != null ? name.GetComponent<TMP_Text>().text : "";
            row.Level = level != null && level.GetComponent<TMP_Text>() != null ? level.GetComponent<TMP_Text>().text : "";
            row.Icon = icon != null && icon.GetComponent<Image>() != null ? icon.GetComponent<Image>().sprite : null;
            var tooltip = element.GetComponentInChildren<UITooltip>();
            row.Tooltip = tooltip != null ? tooltip.m_text : "";
            var guiBar = bar != null ? bar.GetComponent<GuiBar>() : null;
            if (guiBar != null && GuiBarValue != null && GuiBarValue.GetValue(guiBar) is float value)
            {
                row.Progress = value;
            }
            rows.Add(row);
        }
        return rows;
    }

    private static void ClosePanel()
    {
        var gui = InventoryGui.instance;
        if (gui == null)
        {
            return;
        }
        if (gui.m_skillsDialog != null)
        {
            gui.m_skillsDialog.OnClose();
        }
        gui.Hide();
    }

    private static PanelRow FindRow(List<PanelRow> rows, string name)
    {
        if (rows == null)
        {
            return null;
        }
        PanelRow found = null;
        foreach (var row in rows)
        {
            if (row.Name == name)
            {
                if (found != null)
                {
                    return null;   // two rows with one name: never right
                }
                found = row;
            }
        }
        return found;
    }

    // ---------- console ----------

    // One console line as the player type it (Terminal.TryRunCommand: vanilla parse, vanilla name fix-up from the Tab
    // list, vanilla command). Cheats are on only during the call. Vanilla mark the character as cheater and count the
    // command: me put those marks back (throwaway character, but other tests read stats). Returns the lines the
    // console printed, and remove them from the console again. Null = no console.
    private static List<string> RunConsole(string command)
    {
        var console = global::Console.instance;
        var game = Game.instance;
        var profile = game != null ? game.GetPlayerProfile() : null;
        if (console == null || profile == null)
        {
            return null;
        }
        var cheat = Terminal.m_cheat;
        var used = profile.m_usedCheats;
        var stats = new List<Dictionary<PlayerStatType, float>>();
        var known = new List<Dictionary<string, float>>();
        foreach (var s in profile.m_playerStats)
        {
            stats.Add(s != null ? new Dictionary<PlayerStatType, float>(s.m_stats) : null);
            known.Add(s != null ? new Dictionary<string, float>(s.m_knownCommands) : null);
        }
        var before = console.m_chatBuffer.Count;
        var printed = new List<string>();
        try
        {
            Terminal.m_cheat = true;
            // Vanilla ask "confirm cheats" on a character that never cheated (answer cached per frame).
            profile.m_usedCheats = true;
            Achievements.m_cheatCheckFrame = -1;
            console.TryRunCommand(command, false, true);
            for (var i = Mathf.Min(before, console.m_chatBuffer.Count); i < console.m_chatBuffer.Count; i++)
            {
                printed.Add(console.m_chatBuffer[i]);
            }
        }
        finally
        {
            Terminal.m_cheat = cheat;
            profile.m_usedCheats = used;
            Achievements.m_cheatCheckFrame = -1;
            for (var i = 0; i < profile.m_playerStats.Length && i < stats.Count; i++)
            {
                var s = profile.m_playerStats[i];
                if (s == null || stats[i] == null)
                {
                    continue;
                }
                s.m_stats.Clear();
                foreach (var pair in stats[i])
                {
                    s.m_stats[pair.Key] = pair.Value;
                }
                s.m_knownCommands.Clear();
                foreach (var pair in known[i])
                {
                    s.m_knownCommands[pair.Key] = pair.Value;
                }
            }
            if (console.m_chatBuffer.Count >= before && printed.Count == console.m_chatBuffer.Count - before)
            {
                console.m_chatBuffer.RemoveRange(before, printed.Count);
                console.UpdateChat();
            }
        }
        return printed;
    }

    // What Tab makes of "<command> <typed>" in the console: vanilla Terminal.tabCycle on the console's own input box,
    // with the command's Tab list (same call as Terminal.UpdateInput). Input box and Tab state put back. Null = the
    // input box did not take the text (then the caller reads the Tab list itself; why = what the box held instead).
    // A console never opened in this session has an input box that never built its text: its caret stay at 0.
    private static string TabComplete(string command, string typed, out string why)
    {
        why = "";
        var console = global::Console.instance;
        if (console == null || console.m_input == null || !Terminal.commands.TryGetValue(command, out var cmd) || cmd == null)
        {
            why = "no console input box, or no such command";
            return null;
        }
        var input = console.m_input;
        var keepText = input.text;
        var keepCaret = console.m_tabCaretPosition;
        var keepEnd = console.m_tabCaretPositionEnd;
        var keepIndex = console.m_tabIndex;
        var keepLength = console.m_tabLength;
        var keepOptions = new List<string>(console.m_tabOptions);
        try
        {
            var line = command + " " + typed;
            input.text = line;
            input.caretPosition = line.Length;
            if (input.text != line || input.caretPosition != line.Length)
            {
                why = $"the box holds '{input.text}' with the caret at {input.caretPosition}, not at {line.Length}";
                return null;
            }
            console.m_tabCaretPosition = -1;
            console.tabCycle(typed, cmd.GetTabOptions(), false);
            return input.text;
        }
        catch (Exception e)
        {
            // Input box of a console that was never opened may refuse the caret: caller reads the Tab list itself.
            why = e.GetType().Name + ": " + e.Message;
            return null;
        }
        finally
        {
            input.text = keepText;
            console.m_tabCaretPosition = keepCaret;
            console.m_tabCaretPositionEnd = keepEnd;
            console.m_tabIndex = keepIndex;
            console.m_tabLength = keepLength;
            console.m_tabOptions.Clear();
            console.m_tabOptions.AddRange(keepOptions);
        }
    }

    // ---------- skills ----------

    // Skills object like a character get when it load (login, respawn, main menu preview): game own skill list, then
    // Skills.Awake (my always-on postfix add the Sailing definition). Not the player's: load into it change nothing of
    // the character. Caller destroy its GameObject.
    private static Skills FreshSkills(Player player)
    {
        var go = new GameObject("MC_SailingSkill_SelfTest_Skills");
        go.SetActive(false);
        var fresh = go.AddComponent<Skills>();
        foreach (var def in player.GetSkills().m_skills)
        {
            if (def != null && def.m_skill != SailingSkill.Type)
            {
                fresh.m_skills.Add(def);
            }
        }
        go.SetActive(true);   // Awake run now
        return fresh;
    }

    // Level reached from Sailing 0 with samples of the same size (vanilla Skill.Raise: one level per call at most, what
    // is over is lost).
    private static int LevelAfter(float metresPerSample, float metres, float xpPerKm, float multiplier)
    {
        var skill = new Skills.Skill(SailingSkill.Def);
        var samples = Mathf.RoundToInt(metres / metresPerSample);
        for (var i = 0; i < samples; i++)
        {
            skill.Raise(metresPerSample / 1000f * xpPerKm * multiplier);
        }
        return (int)skill.m_level;
    }

    // ---------- wind ----------

    // Vanilla debug wind (console "wind <angle> <intensity>") with no 5 s blend: target and current wind set together.
    // angle: where the wind blows TO (0 = +z, 90 = +x), like the console.
    private sealed class WindScope
    {
        private readonly EnvMan _env;
        private readonly bool _debug;
        private readonly float _angle;
        private readonly float _intensity;
        private readonly Vector4 _wind;
        private readonly Vector4 _dir1;
        private readonly Vector4 _dir2;
        private readonly float _timer;

        internal WindScope()
        {
            _env = EnvMan.instance;
            if (_env == null)
            {
                return;
            }
            _debug = _env.m_debugWind;
            _angle = _env.m_debugWindAngle;
            _intensity = _env.m_debugWindIntensity;
            _wind = _env.m_wind;
            _dir1 = _env.m_windDir1;
            _dir2 = _env.m_windDir2;
            _timer = _env.m_windTransitionTimer;
        }

        internal bool Ok => _env != null;

        // Unit vector the wind blows to.
        internal Vector3 Dir { get; private set; } = Vector3.forward;

        internal void Set(float angle, float intensity)
        {
            if (_env == null)
            {
                return;
            }
            intensity = Mathf.Clamp(intensity, 0.05f, 1f);
            var f = (float)Math.PI / 180f * angle;
            Dir = new Vector3(Mathf.Sin(f), 0f, Mathf.Cos(f));
            _env.m_debugWind = true;
            _env.m_debugWindAngle = angle;
            _env.m_debugWindIntensity = intensity;
            var wind = new Vector4(Dir.x, Dir.y, Dir.z, intensity);
            _env.m_windDir1 = wind;
            _env.m_windDir2 = wind;
            _env.m_wind = wind;
            _env.m_windTransitionTimer = -1f;
        }

        // finally: never throw. The game's own wind come back by itself (vanilla blend) from where it was.
        internal void Restore()
        {
            if (_env == null)
            {
                return;
            }
            _env.m_debugWind = _debug;
            _env.m_debugWindAngle = _angle;
            _env.m_debugWindIntensity = _intensity;
            _env.m_windDir1 = _dir1;
            _env.m_windDir2 = _dir2;
            _env.m_wind = _wind;
            _env.m_windTransitionTimer = _timer;
        }
    }

    // Bow direction for a ship that points <degrees> away from where the wind comes from (0 = straight into the wind,
    // 90 = wind from the side, 180 = wind from behind).
    private static Vector3 HeadingOffWind(Vector3 windTo, float degrees) =>
        Quaternion.Euler(0f, degrees, 0f) * -windTo;

    // ---------- small things ----------

    // Every test end with this: mod itself stay quiet (no warning, no error) while test run.
    private static void CheckQuiet(Checks c, int mark)
    {
        var problems = Problems(mark, ModInfo.Name);
        c.Check(problems.Count == 0, $"{ModInfo.Name} logged warnings or errors during the test: {Join(problems)}");
    }

    private static bool SameColor(Color a, Color b, float tolerance = 0.004f) =>
        Mathf.Abs(a.r - b.r) <= tolerance && Mathf.Abs(a.g - b.g) <= tolerance && Mathf.Abs(a.b - b.b) <= tolerance;

    private static string C(Color c) => $"({F(c.r)}, {F(c.g)}, {F(c.b)})";

    // Me wait until condition hold (true) or time over (false in result.Ok).
    private static IEnumerator WaitFor(Func<bool> condition, float seconds, Box result)
    {
        var until = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < until)
        {
            yield return null;
        }
        result.Ok = condition();
    }

    // Sailing entry of the character with a level and progress (entry made when missing).
    private static Skills.Skill SetSailing(Player player, float level, float accumulator)
    {
        var skill = player.GetSkills().GetSkill(SailingSkill.Type);
        skill.m_level = level;
        skill.m_accumulator = accumulator;
        HelmSkill.Invalidate();
        return skill;
    }

    private static void DestroyNow(GameObject go)
    {
        if (go != null)
        {
            Object.Destroy(go);
        }
    }
}
#endif
