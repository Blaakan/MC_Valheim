#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using HarmonyLib;
using MC.Shared;
using UnityEngine;
using InputSystem = UnityEngine.InputSystem.InputSystem;
using Key = UnityEngine.InputSystem.Key;
using Keyboard = UnityEngine.InputSystem.Keyboard;
using KeyboardState = UnityEngine.InputSystem.LowLevel.KeyboardState;
using Mouse = UnityEngine.InputSystem.Mouse;
using MouseState = UnityEngine.InputSystem.LowLevel.MouseState;
using Object = UnityEngine.Object;

namespace MC.Exploration.MusicInstrumentsMod;

// Debug build only. Tools of the self tests (SelfTests.cs has the first twelve tests and the Rig):
//   Seen      messages the game showed (MessageHud.ShowMessage, "new recipe" queue): Debug-only patches
//   LogTap    this mod's own log lines (warnings, errors, info) since the mod started
//   buttons   the game's named buttons pressed like a key would (ZInput.ButtonDef: what vanilla code reads)
//   real keys key and mouse events through the Input System (what a real key press makes): the raw reads of the mod
//   Extra     more things a test changes on the player and world, put back in Restore (finally, no yield)
internal static partial class SelfTests
{
    // ---------- time (real seconds: also while the game is paused) ----------

    private static IEnumerator Real(float seconds)
    {
        var end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            yield return null;
        }
    }

    private static IEnumerator WaitReal(Func<bool> done, float timeout)
    {
        var end = Time.realtimeSinceStartup + timeout;
        while (!done() && Time.realtimeSinceStartup < end)
        {
            yield return null;
        }
    }

    // ---------- messages seen ----------

    private static class Seen
    {
        internal struct Msg
        {
            internal MessageHud.MessageType Type;
            internal string Text;
        }

        internal static readonly List<Msg> Messages = new List<Msg>();
        internal static readonly List<KeyValuePair<string, string>> Unlocks = new List<KeyValuePair<string, string>>();

        internal static void Clear()
        {
            Messages.Clear();
            Unlocks.Clear();
        }

        internal static void Add(MessageHud.MessageType type, string text)
        {
            if (Messages.Count > 2000)
            {
                Messages.Clear();
            }
            Messages.Add(new Msg { Type = type, Text = text ?? "" });
        }

        internal static void AddUnlock(string topic, string description)
        {
            if (Unlocks.Count > 2000)
            {
                Unlocks.Clear();
            }
            Unlocks.Add(new KeyValuePair<string, string>(topic ?? "", description ?? ""));
        }

        // Messages of that kind holding the text, from index 'from' on.
        internal static int Count(MessageHud.MessageType type, string part, int from = 0)
        {
            var n = 0;
            for (var i = Math.Max(0, from); i < Messages.Count; i++)
            {
                if (Messages[i].Type == type && Messages[i].Text.IndexOf(part, StringComparison.Ordinal) >= 0)
                {
                    n++;
                }
            }
            return n;
        }

        internal static bool Unlocked(string topic, string description)
        {
            foreach (var u in Unlocks)
            {
                if (u.Key == topic && u.Value == description)
                {
                    return true;
                }
            }
            return false;
        }

        internal static string Tail(int count = 6)
        {
            var parts = new List<string>();
            for (var i = Math.Max(0, Messages.Count - count); i < Messages.Count; i++)
            {
                parts.Add(Messages[i].Type + ": " + Messages[i].Text);
            }
            return parts.Count == 0 ? "(no message)" : string.Join(" | ", parts.ToArray());
        }
    }

    [HarmonyPatch(typeof(MessageHud), nameof(MessageHud.ShowMessage))]
    private static class SeenMessagePatch
    {
        [HarmonyPrefix]
        private static void Prefix(MessageHud.MessageType type, string text)
        {
            try
            {
                Seen.Add(type, text);
            }
            catch (Exception e)
            {
                PatchGuard.Report("SelfTests MessageHud.ShowMessage prefix", e);
            }
        }
    }

    [HarmonyPatch(typeof(MessageHud), nameof(MessageHud.QueueUnlockMsg))]
    private static class SeenUnlockPatch
    {
        [HarmonyPrefix]
        private static void Prefix(string topic, string description)
        {
            try
            {
                Seen.AddUnlock(topic, description);
            }
            catch (Exception e)
            {
                PatchGuard.Report("SelfTests MessageHud.QueueUnlockMsg prefix", e);
            }
        }
    }

    // ---------- own log lines ----------

    // Me listen to the BepInEx log for lines of this mod's own log source (any thread: lock). Kept from the first
    // activation to game exit, also over a turn off and on.
    private sealed class LogTap : ILogListener
    {
        private static readonly LogTap Instance = new LogTap();
        private static readonly object Gate = new object();
        private static readonly List<string> Problems = new List<string>();  // warnings and errors
        private static readonly List<string> Infos = new List<string>();
        private static int _infoTotal;
        private static bool _installed;

        internal static void Install()
        {
            if (_installed)
            {
                return;
            }
            _installed = true;
            BepInEx.Logging.Logger.Listeners.Add(Instance);
        }

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            try
            {
                if (eventArgs == null || eventArgs.Source == null || eventArgs.Source.SourceName != ModInfo.Name)
                {
                    return;
                }
                var text = eventArgs.Data as string ?? eventArgs.Data?.ToString();
                if (text == null)
                {
                    return;
                }
                lock (Gate)
                {
                    if ((eventArgs.Level & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) != 0)
                    {
                        if (Problems.Count < 2000)
                        {
                            Problems.Add(((eventArgs.Level & LogLevel.Warning) != 0 ? "W " : "E ") + text);
                        }
                    }
                    else if ((eventArgs.Level & (LogLevel.Info | LogLevel.Message)) != 0)
                    {
                        if (Infos.Count > 600)
                        {
                            Infos.RemoveRange(0, 300);
                        }
                        Infos.Add(text);
                        _infoTotal++;
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

        // Warnings and errors so far (count now = a mark for "since").
        internal static int ProblemMark
        {
            get
            {
                lock (Gate)
                {
                    return Problems.Count;
                }
            }
        }

        internal static List<string> ProblemsSince(int mark)
        {
            lock (Gate)
            {
                var list = new List<string>();
                for (var i = Math.Max(0, mark); i < Problems.Count; i++)
                {
                    list.Add(Problems[i]);
                }
                return list;
            }
        }

        internal static int CountProblems(int mark, string part)
        {
            var n = 0;
            foreach (var line in ProblemsSince(mark))
            {
                if (line.IndexOf(part, StringComparison.Ordinal) >= 0)
                {
                    n++;
                }
            }
            return n;
        }

        internal static int InfoMark
        {
            get
            {
                lock (Gate)
                {
                    return _infoTotal;
                }
            }
        }

        // An info line holding the text came after the mark (lines older than the last few hundred are forgotten).
        internal static bool InfoSince(int mark, string part)
        {
            lock (Gate)
            {
                var first = _infoTotal - Infos.Count;
                for (var i = Math.Max(0, mark - first); i < Infos.Count; i++)
                {
                    if (Infos[i].IndexOf(part, StringComparison.Ordinal) >= 0)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        // Lines a tester would not count against the mod: self test results and clean-up notes, and the warnings the
        // tests provoke on purpose (a lane key the game cannot use, the test files of the server songs folder).
        internal static bool Expected(string line) =>
            line.IndexOf(SelfTest.Prefix, StringComparison.Ordinal) >= 0
            || line.IndexOf("Self test clean-up", StringComparison.Ordinal) >= 0
            || line.IndexOf("selftest", StringComparison.Ordinal) >= 0
            || (line.StartsWith("W MiniGame.Lane", StringComparison.Ordinal) && line.IndexOf("Key = ", StringComparison.Ordinal) > 0);
    }

    // ---------- the game's named buttons ----------

    private static ZInput.ButtonDef Btn(string name) =>
        ZInput.instance != null && ZInput.instance.m_buttons.TryGetValue(name, out var b) ? b : null;

    // Button goes down and stays down (like a held key) until Let.
    private static bool Press(string name)
    {
        var b = Btn(name);
        if (b == null)
        {
            return false;
        }
        b.Press();
        return true;
    }

    private static void Let(string name)
    {
        if (ZInput.instance != null)
        {
            ZInput.ResetButtonStatus(name);
        }
    }

    // One press: down for a few frames (every Update and FixedUpdate sees it once), then forgotten.
    private static IEnumerator Tap(string name, int frames = 3)
    {
        Press(name);
        yield return Frames(frames);
        Let(name);
    }

    // ---------- real key and mouse events (Input System) ----------

    // A test sent real key or mouse events: Extra.Restore lets every key go.
    private static bool _realKeysUsed;

    // Keyboard state with exactly these keys down (none = all up). False = no keyboard or a key the game cannot map.
    private static bool KeysDown(params KeyCode[] codes)
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return false;
        }
        _realKeysUsed = true;
        var keys = new Key[codes.Length];
        for (var i = 0; i < codes.Length; i++)
        {
            if (!ZInput.TryKeyCodeToKey(codes[i], out keys[i]))
            {
                return false;
            }
        }
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        return true;
    }

    // Game window not in front: the Input System switches keyboard and mouse off and throws their events away
    // (InputManager.OnFocusChanged; ZInput reads only what the Input System holds). For a test of real key events
    // me tell it to ignore focus and switch the two devices on again, so the events land the same way with the window
    // behind another one. RealKeysBack puts the setting back (the devices stay on until the next focus change, which
    // sorts them: in the background no key of the player reaches them anyway).
    private static bool _backgroundSaved;
    private static UnityEngine.InputSystem.InputSettings.BackgroundBehavior _background;

    // True = keyboard and mouse take events now. forced[0] = the window was not in front and focus is ignored.
    private static bool RealKeysOn(bool[] forced)
    {
        forced[0] = false;
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        if (keyboard == null || mouse == null)
        {
            return false;
        }
        if (Application.runInBackground)
        {
            // Also when the window is in front now: it may go behind another one half way through the test.
            var settings = InputSystem.settings;
            if (!_backgroundSaved)
            {
                _backgroundSaved = true;
                _background = settings.backgroundBehavior;
            }
            settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
            if (!keyboard.enabled)
            {
                InputSystem.EnableDevice(keyboard);
            }
            if (!mouse.enabled)
            {
                InputSystem.EnableDevice(mouse);
            }
            forced[0] = !Application.isFocused;
        }
        return keyboard.enabled && mouse.enabled && (Application.isFocused || Application.runInBackground);
    }

    private static void RealKeysBack()
    {
        if (_backgroundSaved)
        {
            _backgroundSaved = false;
            InputSystem.settings.backgroundBehavior = _background;
        }
    }

    private static bool MouseRight(bool down)
    {
        var mouse = Mouse.current;
        if (mouse == null)
        {
            return false;
        }
        _realKeysUsed = true;
        var state = new MouseState { position = mouse.position.ReadValue() };
        if (down)
        {
            state = state.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Right, true);
        }
        InputSystem.QueueStateEvent(mouse, state);
        return true;
    }

    // ---------- small things many tests need ----------

    private static bool MenuShown => Menu.instance != null && Menu.instance.m_root != null && Menu.instance.m_root.gameObject.activeSelf;

    // Chat open for typing. The input box counts too (the game shows it only while the chat is open: Chat.Awake hides
    // it, the Chat key shows it): a text field does not always say "focused" when the game window is not in front.
    private static bool ChatTyping =>
        Chat.instance != null && (Chat.instance.m_wasFocused
                                  || (Chat.instance.m_input != null && (Chat.instance.m_input.isFocused || Chat.instance.m_input.gameObject.activeSelf)));

    // Chat input shut the way Esc shuts it (Chat.Update): a test that opened it never leaves the keys with the chat.
    private static void CloseChat()
    {
        var chat = Chat.instance;
        if (chat == null || chat.m_input == null)
        {
            return;
        }
        var events = UnityEngine.EventSystems.EventSystem.current;
        if (events != null)
        {
            events.SetSelectedGameObject(null);
        }
        chat.m_input.gameObject.SetActive(false);
        chat.m_focused = false;
        chat.m_wasFocused = false;
        Let("Chat");
    }

    private static int PlayingFlag(Player player) =>
        player.m_nview != null && player.m_nview.IsValid() ? player.m_nview.GetZDO().GetInt(InstrumentPose.PlayingKey) : -1;

    private static float Flat(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return (a - b).magnitude;
    }

    // Root mean square of a sound source over the next 'seconds' (real time). Null or destroyed source = silent.
    private static IEnumerator Measure(Emitter emitter, float seconds, float[] rms)
    {
        if (emitter != null)
        {
            emitter.ResetMeasure();
        }
        yield return Real(seconds);
        rms[0] = emitter != null ? emitter.Rms : 0f;
    }

    private static int EmitterCount() => Object.FindObjectsByType<Emitter>(FindObjectsSortMode.None).Length;

    // A song made here: notes at the given times (seconds), as a one-part MIDI file (480 ticks per quarter, 120 bpm).
    private static byte[] MidiFrom(List<Note> notes)
    {
        const double ticksPerSecond = 480.0 * 120.0 / 60.0;
        var ms = new MemoryStream();
        foreach (var ch in "MThd")
        {
            ms.WriteByte((byte)ch);
        }
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte(6);
        ms.WriteByte(0);
        ms.WriteByte(1);
        ms.WriteByte(0);
        ms.WriteByte(2);
        ms.WriteByte(480 >> 8);
        ms.WriteByte(480 & 0xFF);
        var us = 500000; // 120 bpm
        WriteTrack(ms, new List<byte[]> { Ev(0, 0xFF, 0x51, 3, (byte)(us >> 16), (byte)(us >> 8), (byte)us), Ev(0, 0xFF, 0x2F, 0) });
        WriteTrack(ms, TrackOf(notes, 0, ticksPerSecond));
        return ms.ToArray();
    }

    // 'count' notes every 'gap' seconds from 'start', going up a scale.
    private static void AddRun(List<Note> notes, float start, int count, float gap, float length = 0.25f)
    {
        int[] scale = { 0, 2, 4, 5, 7, 9, 11, 12 };
        for (var i = 0; i < count; i++)
        {
            notes.Add(new Note(start + i * gap, length, (byte)(72 + scale[i % scale.Length]), 96));
        }
    }

    private static string TempFolder(string name)
    {
        var folder = Path.Combine(Path.GetTempPath(), name);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, true);
        }
        Directory.CreateDirectory(folder);
        return folder;
    }

    // The mod turned off for real through the framework (patches removed, OnDeactivated run), without the Enabled
    // setting: the framework asks LocalBlocker at its refresh. The framework writes its own Status line; nothing else.
    private static void RealOff()
    {
        Plugin.TestBlocker = "Inactive: turned off by its own self test (test only).";
        FeatureRegistry.RefreshAll();
    }

    // Back on (also when it was never off). Rules of the tests are gone by then (OnDeactivated cleared them).
    private static void RealOn()
    {
        if (Plugin.TestBlocker == null)
        {
            return;
        }
        Plugin.TestBlocker = null;
        FeatureRegistry.RefreshAll();
    }

    private static bool ModActive
    {
        get
        {
            var view = FeatureRegistry.Find(ModInfo.Guid);
            return view != null && view.Value.IsActive;
        }
    }

    // Me = more state a test changes, put back in Restore (finally: no yield). Use next to the Rig; Restore me first.
    private sealed class Extra
    {
        private readonly Player _player;
        private readonly float _health;
        private readonly bool _repeat;
        private readonly string _lastSong;
        private readonly int _lastPart;
        private readonly string _songsFolder;
        private readonly bool _hudHidden;
        private readonly bool _hadRested;
        private readonly Vector3 _position;
        private readonly List<string> _folders = new List<string>();
        private readonly List<ItemDrop.ItemData> _items = new List<ItemDrop.ItemData>();
        private PlayerController _controller;
        private bool _controllerSaved;
        private bool _controllerEnabled;
        private bool _envSaved;
        private string _env;
        private bool _staminaSaved;
        private bool _hadStamina;
        private float _stamina;
        private bool _knownSaved;
        private HashSet<string> _recipes;
        private HashSet<string> _materials;
        private Dictionary<string, int> _stations;
        private bool _consoleSaved;
        private bool _console;
        private bool _shareTouched;

        internal Extra(Player player)
        {
            _player = player;
            _health = player.GetHealth();
            _repeat = Performance.Repeat;
            _lastSong = Performance.LastSongId;
            _lastPart = Performance.LastPart;
            _songsFolder = SongLibrary.ConfiguredFolder;
            _hudHidden = Hud.instance != null && Hud.instance.m_userHidden;
            _hadRested = player.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectRested);
            _position = player.transform.position;
        }

        // Empty temp folder, deleted at the end.
        internal string Folder(string name)
        {
            var folder = TempFolder(name);
            _folders.Add(folder);
            return folder;
        }

        // The player's own songs folder for this test (memory only): an empty temp folder.
        internal string SongsFolder(string name)
        {
            var folder = Folder(name);
            SongLibrary.ConfiguredFolder = folder;
            return folder;
        }

        // Server songs of this game (host): on, from this folder (memory only).
        internal void Share(string folder)
        {
            _shareTouched = true;
            SongShare.TestFolder = folder;
            SongShare.TestShare = true;
            SongShare.ServerSettingsChanged();
        }

        // No server songs in this test, whatever the player's own setting says.
        internal void NoShare()
        {
            _shareTouched = true;
            SongShare.TestShare = false;
            SongShare.ServerSettingsChanged();
        }

        // An item the test put in the inventory by another way than the Rig (picked up): removed at the end.
        internal void Track(ItemDrop.ItemData item)
        {
            if (item != null)
            {
                _items.Add(item);
            }
        }

        // The game reads the keyboard and mouse again (the Rig switched the player controller off).
        internal void Controller(bool on)
        {
            if (_controller == null)
            {
                _controller = _player.GetComponent<PlayerController>();
            }
            if (_controller == null)
            {
                return;
            }
            if (!_controllerSaved)
            {
                _controllerSaved = true;
                _controllerEnabled = _controller.enabled;
            }
            _controller.enabled = on;
        }

        internal PlayerController ControllerObject
        {
            get
            {
                if (_controller == null)
                {
                    _controller = _player.GetComponent<PlayerController>();
                }
                return _controller;
            }
        }

        internal void Weather(string env)
        {
            var man = EnvMan.instance;
            if (!_envSaved)
            {
                _envSaved = true;
                _env = man.m_debugEnv;
            }
            man.m_debugEnv = env;
            man.ForceInstantEnvironmentSwitch();
        }

        // Stamina used like in a normal game (the test world has stamina use switched off).
        internal void RealStamina()
        {
            var zone = ZoneSystem.instance;
            if (zone == null || _staminaSaved)
            {
                return;
            }
            _staminaSaved = true;
            _hadStamina = zone.GetGlobalKey(GlobalKeys.StaminaRate, out _stamina);
            zone.RemoveGlobalKey(GlobalKeys.StaminaRate);
        }

        // What the character knows (recipes, materials, stations): kept as it was.
        internal void SaveKnown()
        {
            if (_knownSaved)
            {
                return;
            }
            _knownSaved = true;
            _recipes = new HashSet<string>(_player.m_knownRecipes);
            _materials = new HashSet<string>(_player.m_knownMaterial);
            _stations = new Dictionary<string, int>(_player.m_knownStations);
        }

        // Put back what the character knew. Call it again after the Rig took the test's items out of the inventory
        // (taking one out makes the game learn the others still in it), and drop the "new ..." notes not shown yet.
        internal void Known()
        {
            if (!_knownSaved)
            {
                return;
            }
            _player.m_knownRecipes.Clear();
            foreach (var r in _recipes)
            {
                _player.m_knownRecipes.Add(r);
            }
            _player.m_knownMaterial.Clear();
            foreach (var m in _materials)
            {
                _player.m_knownMaterial.Add(m);
            }
            _player.m_knownStations.Clear();
            foreach (var s in _stations)
            {
                _player.m_knownStations[s.Key] = s.Value;
            }
            if (MessageHud.instance != null)
            {
                MessageHud.instance.m_unlockMsgQueue.Clear();
            }
        }

        internal void Console(bool enabled)
        {
            if (!_consoleSaved)
            {
                _consoleSaved = true;
                _console = global::Console.m_consoleEnabled;
            }
            global::Console.SetConsoleEnabled(enabled);
        }

        internal void Restore()
        {
            Try("mod on", RealOn);
            Try("menu", () =>
            {
                if (MenuShown)
                {
                    Menu.instance.Hide();
                }
                Game.Unpause();
            });
            Try("buttons", () =>
            {
                if (ZInput.instance != null)
                {
                    ZInput.ResetAllButtonStates();
                }
            });
            Try("real keys", () =>
            {
                if (_realKeysUsed)
                {
                    KeysDown();
                    MouseRight(false);
                    _realKeysUsed = false;
                }
                RealKeysBack();
            });
            Try("chat", () =>
            {
                if (Chat.instance != null && Chat.instance.m_input != null && Chat.instance.m_input.gameObject.activeSelf)
                {
                    CloseChat();
                }
            });
            Try("console", () =>
            {
                var console = global::Console.instance;
                if (console != null && console.m_chatWindow != null && console.m_chatWindow.gameObject.activeSelf)
                {
                    console.m_chatWindow.gameObject.SetActive(false);
                }
                if (_consoleSaved)
                {
                    global::Console.SetConsoleEnabled(_console);
                }
            });
            Try("screens", () =>
            {
                if (InventoryGui.instance != null && InventoryGui.IsVisible())
                {
                    InventoryGui.instance.Hide();
                }
                if (Minimap.instance != null && Minimap.instance.m_mode == Minimap.MapMode.Large)
                {
                    Minimap.instance.SetMapMode(Minimap.MapMode.Small);
                }
                if (Hud.instance != null)
                {
                    Hud.instance.m_userHidden = _hudHidden;
                }
            });
            Try("player", () =>
            {
                _player.StopEmote();
                _player.m_swimTimer = 999f;
                _player.m_autoRun = false;
                _player.m_sleeping = false;
                var seman = _player.GetSEMan();
                seman.RemoveStatusEffect(SEMan.s_statusEffectBurning, true);
                if (!_hadRested)
                {
                    seman.RemoveStatusEffect(SEMan.s_statusEffectRested, true);
                }
                if (_player.GetHealth() < _health)
                {
                    _player.SetHealth(_health);
                }
                if (Flat(_player.transform.position, _position) > 0.5f && !_player.IsDead())
                {
                    _player.transform.position = _position;
                    if (_player.m_body != null)
                    {
                        _player.m_body.position = _position;
                        _player.m_body.linearVelocity = Vector3.zero;
                    }
                }
            });
            Try("controller", () =>
            {
                if (_controllerSaved && _controller != null)
                {
                    _controller.enabled = _controllerEnabled;
                }
            });
            Try("known", Known);
            Try("weather", () =>
            {
                if (_envSaved && EnvMan.instance != null)
                {
                    var changed = EnvMan.instance.m_debugEnv != _env;
                    EnvMan.instance.m_debugEnv = _env;
                    if (changed)
                    {
                        EnvMan.instance.ForceInstantEnvironmentSwitch();
                    }
                }
            });
            Try("stamina", () =>
            {
                if (_staminaSaved && _hadStamina && ZoneSystem.instance != null)
                {
                    ZoneSystem.instance.SetGlobalKey(GlobalKeys.StaminaRate, _stamina);
                }
            });
            Try("songs", () =>
            {
                Performance.Repeat = _repeat;
                Performance.LastSongId = _lastSong;
                Performance.LastPart = _lastPart;
                SongLibrary.ConfiguredFolder = _songsFolder;
                if (_shareTouched)
                {
                    SongShare.TestShare = null;
                    SongShare.TestFolder = null;
                    SongShare.ServerSettingsChanged();
                }
            });
            Try("items", () =>
            {
                var inv = _player.GetInventory();
                foreach (var item in _items)
                {
                    if (inv.ContainsItem(item))
                    {
                        if (_player.IsItemEquiped(item))
                        {
                            _player.UnequipItem(item, false);
                        }
                        inv.RemoveItem(item);
                    }
                }
            });
            foreach (var folder in _folders)
            {
                try
                {
                    if (Directory.Exists(folder))
                    {
                        Directory.Delete(folder, true);
                    }
                }
                catch (Exception)
                {
                    // Temp folder: fine to leave.
                }
            }
        }

        private static void Try(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up ({what}) failed: {e.Message}");
            }
        }
    }
}
#endif
