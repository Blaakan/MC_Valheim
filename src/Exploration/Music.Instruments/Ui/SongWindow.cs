using System;
using System.Collections.Generic;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MC.Exploration.MusicInstrumentsMod;

// Me = the song window (Attack with an instrument in hand). Own overlay canvas (order 650: above inventory 600, under
// HudMessage 1000 / menu 1700 / console 5000) with game GUI scale, raycaster, focus group above every other one and a
// full-screen see-through blocker. Look = vanilla parts: wood frame of the Texts dialog, dialog fonts, clones of the
// craft button and the recipe list scroll bar. Built on first Open, kept (hidden) until world exit.
// List = built-in songs, server songs (SongShare: when the server shares; client ask the list at every Open, rows come
// when it arrive), header, MIDI files of the songs folder (scanned at every Open; hidden when the server's rules forbid
// own songs). A server song is downloaded only when PICKED (click, Enter, pad A, Play, Perform, part keys), never while
// keys just move over it (each request counts on the server's budget): details line show progress; Play/Perform on a
// song still coming start it when it is here. Rows
// virtualized: a small pool of row objects bound to the list items around the scroll position (500 files = 9 rows).
// Performance own the life: Open / Update (every frame while open) / Close / Destroy. Me call back Performance only
// for Play (StartAuto), Perform (StartMiniGame), Close (CloseWindow) and the Repeat flag.
// Keys while open (Performance hold KeyCapture: game keys off): Up/Down choose, Left/Right part, Enter play; gamepad:
// D-pad up/down choose, left/right part, A play, X perform, Y repeat. Esc / B close (MenuPatches).
internal static class SongWindow
{
    private const int WantedOrder = 650;
    private const float Width = 800f;
    private const float Height = 784f;
    private const float Side = 44f;
    private const float RowH = 46f;
    private const float TitleTop = 20f;
    private const float ListTop = 70f;
    private const int ShownRows = 10; // built-in songs + MIDI header + hint: all in view without scrolling
    private const float RowInset = 6f;
    private const float ListH = ShownRows * RowH + 2f * RowInset;
    private const float ButtonW = 164f;
    private const float ButtonH = 46f;
    private const float ButtonGap = 14f;
    private const float RepeatDelay = 0.35f;
    private const float RepeatEvery = 0.09f;
    private const float DoubleClick = 0.35f;
    private const float LoadRest = 0.25f; // MIDI file read when the selection rest this long (not while keys repeat)

    private const string WindowName = ModInfo.Guid + ".SongWindow";
    private const string MidiHeader = "Your MIDI songs";
    private const string ServerHeader = "Server songs";
    private const string FreePlayOnKeyboard = "Free play is played on the keyboard.";
    private const string NoSongText = "Choose a song.";
    private const string CannotPlay = "You cannot play now.";
    private const string NavKeys = "Up/Down: choose a song    Left/Right: part    Enter: play    Esc: close";
    // No X: perform on the pad: the rhythm game has keyboard lanes only (v1).
    private const string NavPad = "D-pad: choose a song and part    A: play    Y: repeat    B: close";

    private enum ItemKind : byte
    {
        Song,
        Header,
        Hint,
    }

    private struct Item
    {
        internal ItemKind Kind;
        internal SongEntry Entry;
        internal string Text;
    }

    private sealed class RowView
    {
        internal GameObject Go;
        internal RectTransform Rt;
        internal Button Button;
        internal Image Selected;
        internal TextMeshProUGUI Title;
        internal TextMeshProUGUI Info;
        internal int Bound = -1;
    }

    private static GameObject _root;
    private static Canvas _canvas;
    private static UIGroupHandler _group;
    private static RectTransform _window;
    private static TextMeshProUGUI _title;
    private static ScrollRect _scroll;
    private static RectTransform _viewport;
    private static RectTransform _content;
    private static TextMeshProUGUI _details;
    private static Button _partButton;
    private static TMP_Text _partLabel;
    private static TextMeshProUGUI _status;
    private static Button _play;
    private static Button _perform;
    private static Button _repeat;
    private static TMP_Text _repeatLabel;
    private static Button _close;
    private static TextMeshProUGUI _helpPerform;
    private static TextMeshProUGUI _helpNav;
    private static readonly List<RowView> Rows = new List<RowView>(12);
    private static readonly List<Item> Items = new List<Item>(64);

    private static bool _open;
    private static InstrumentKind _kind;
    private static int _selected = -1;
    private static int _part = -1;
    private static int _boundFirst = -1;
    private static bool _rowsDirty;
    private static bool _repeatShown;
    private static bool _repeatKnown;
    private static bool _padShown;
    private static bool _padKnown;
    private static int _heldDir;
    private static float _repeatAt;
    private static bool _dirLatched;
    private static bool _sideHeld;
    private static bool _loadPending;
    private static float _loadAt;
    private static int _lastClickItem = -1;
    private static float _lastClickAt = -9f;
    private static bool _keysChecked;
    private static bool _arrowsOk;
    private static bool _enterOk;
    private static bool _padEnterOk;
    private static int _listVersionShown = -1;
    private static int _progressShown = -1;
    private static SongEntry _startWhenReady; // Play/Perform pressed on a server song still downloading
    private static bool _startMiniGame;

    internal static bool IsOpen => _open && _root != null;

    // ---------------------------------------------------------------- life

    internal static void Open(InstrumentKind kind)
    {
        try
        {
            if (UiKit.Headless)
            {
                return;
            }
            if (_root == null && !Build())
            {
                // No window: never leave the player frozen behind an invisible one.
                Performance.CloseWindow();
                return;
            }
            _kind = kind;
            CheckKeys();
            var name = InstrumentContent.DisplayName(kind);
            _title.text = string.IsNullOrEmpty(name) ? "Songs" : name + " songs";
            _helpPerform.text = "Perform: play the notes in time with " + LaneKeys.Label(0) + " " + LaneKeys.Label(1) + " "
                                + LaneKeys.Label(2) + " " + LaneKeys.Label(3) + " (your MiniGame keys)";
            _padKnown = false;
            _repeatKnown = false;
            SetStatus(null);
            SongShare.AskList();
            RebuildItems();
            _selected = -1;
            _part = -1;
            _loadPending = false;
            // The free play line names the player's own keys (labels by keyboard layout).
            SongLibrary.FreePlay.Info = "Play the keyboard like a piano: " + FreePlayKeys.Label(0) + " to "
                                        + FreePlayKeys.Label(11) + " and " + FreePlayKeys.Label(12) + " to "
                                        + FreePlayKeys.Label(FreePlayMap.KeyCount - 1)
                                        + ", black keys on the row above; Space: octave up";
            var last = Find(Performance.LastSongId);
            var pick = last >= 0 ? last : NextSong(-1, 1);
            if (PadInput.Active && pick >= 0 && Items[pick].Entry.Source == SongSource.FreePlay)
            {
                pick = NextSong(pick, 1); // on a pad, A plays the first song, as before free play
            }
            Select(pick, keepPart: true, loadNow: true);
            _canvas.sortingOrder = UiKit.OrderAboveFrameBuffer(WantedOrder);
            _group.m_groupPriority = UiKit.TopPriority(_group);
            _heldDir = 0;
            _repeatAt = 0f;
            // Key or stick still down from before the window: me wait till it was let go once.
            _dirLatched = true;
            _sideHeld = true;
            _lastClickItem = -1;
            _root.SetActive(true);
            _open = true;
            _content.anchoredPosition = Vector2.zero;
            _scroll.StopMovement();
            ScrollTo(_selected, centre: true);
            Refresh();
            BindRows();
        }
        catch (Exception e)
        {
            PatchGuard.Report("SongWindow.Open", e);
            // Half-open window: let the player go (Performance would hold the keys for a window nobody sees).
            try
            {
                if (!IsOpen)
                {
                    Performance.CloseWindow();
                }
            }
            catch (Exception e2)
            {
                PatchGuard.Report("SongWindow.Open close", e2);
            }
        }
    }

    internal static void Close()
    {
        if (!_open)
        {
            return;
        }
        _open = false;
        _startWhenReady = null;
        try
        {
            DropSelection();
            if (_root != null)
            {
                _root.SetActive(false);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("SongWindow.Close", e);
        }
    }

    internal static void Destroy()
    {
        _open = false;
        try
        {
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("SongWindow.Destroy", e);
        }
        _root = null;
        _canvas = null;
        _group = null;
        _window = null;
        _title = null;
        _scroll = null;
        _viewport = null;
        _content = null;
        _details = null;
        _partButton = null;
        _partLabel = null;
        _status = null;
        _play = null;
        _perform = null;
        _repeat = null;
        _repeatLabel = null;
        _close = null;
        _helpPerform = null;
        _helpNav = null;
        Rows.Clear();
        Items.Clear();
        _selected = -1;
        _part = -1;
        _boundFirst = -1;
        _loadPending = false;
        _listVersionShown = -1;
        _progressShown = -1;
    }

    // Every frame while open (Performance.Tick, Player.Update postfix). No allocation unless something changed.
    internal static void Update()
    {
        if (!IsOpen)
        {
            return;
        }
        try
        {
            if (HandleKeys())
            {
                return; // a song started: window closed
            }
            if (!IsOpen)
            {
                return;
            }
            if (_loadPending && Time.unscaledTime >= _loadAt)
            {
                LoadSelected(pick: false);
            }
            if (SongShare.ListVersion != _listVersionShown)
            {
                RebuildKeepSelection(); // server list came (or changed)
            }
            else if (SongShare.ProgressVersion != _progressShown)
            {
                _progressShown = SongShare.ProgressVersion;
                _rowsDirty = true; // a download moved or ended
                Refresh();
                if (StartWhenReady())
                {
                    return; // the song came and started: window closed
                }
            }
            var pad = PadInput.Active;
            if (!_padKnown || pad != _padShown)
            {
                _padKnown = true;
                _padShown = pad;
                _helpNav.text = pad ? NavPad : NavKeys;
            }
            if (!_repeatKnown || Performance.Repeat != _repeatShown)
            {
                _repeatKnown = true;
                _repeatShown = Performance.Repeat;
                UiKit.SetText(_repeatLabel, _repeatShown ? "Repeat: On" : "Repeat: Off");
            }
            BindRows();
        }
        catch (Exception e)
        {
            PatchGuard.Report("SongWindow.Update", e);
        }
    }

    // ---------------------------------------------------------------- build

    private static bool Build()
    {
        try
        {
            BuildObjects();
            Log.Debug("Song window built.");
            return true;
        }
        catch (Exception e)
        {
            PatchGuard.Report("SongWindow.Build", e);
            Destroy();
            return false;
        }
    }

    private static void BuildObjects()
    {
        // Old root may be gone by an outside destroy: its rows and list are dead, start clean.
        Rows.Clear();
        Items.Clear();
        _selected = -1;
        _part = -1;
        _boundFirst = -1;
        _rowsDirty = true;
        _loadPending = false;
        _root = UiKit.MakeRootCanvas(WindowName, WantedOrder, out _canvas);
        _root.AddComponent<GraphicRaycaster>();
        var group = _root.AddComponent<CanvasGroup>(); // UIGroupHandler toggle its interactable
        group.alpha = 1f;
        group.interactable = true;
        group.blocksRaycasts = true;
        _group = _root.AddComponent<UIGroupHandler>();
        _group.m_defaultElement = null;
        _group.m_groupPriority = 1;
        var rootRt = (RectTransform)_root.transform;

        // Full-screen see-through blocker: clicks beside the window never reach the game or other windows.
        var blocker = UiKit.MakeImage(rootRt, "Blocker", new Color(0f, 0f, 0f, 0f), raycast: true);
        UiKit.Fill(blocker.rectTransform);
        blocker.canvasRenderer.cullTransparentMesh = false; // culled = no raycast hit

        _window = UiKit.Rect(rootRt, "Window");
        _window.anchorMin = new Vector2(0.5f, 0.5f);
        _window.anchorMax = new Vector2(0.5f, 0.5f);
        _window.pivot = new Vector2(0.5f, 0.5f);
        _window.anchoredPosition = new Vector2(0f, 10f);
        _window.sizeDelta = new Vector2(Width, Height);

        var frame = UiKit.MakeImage(_window, "Frame", UiKit.FrameFallback, raycast: true);
        UiKit.Fill(frame.rectTransform, -10f);
        if (!UiKit.CopyLook(frame, UiKit.WoodFrameSource()))
        {
            frame.color = UiKit.FrameFallback;
            Log.Debug("Song window: vanilla wood frame not found, plain frame used.");
        }

        var titleStyle = UiKit.TitleStyle();
        var body = UiKit.BodyStyle();
        _title = UiKit.MakeText(_window, "Title", titleStyle, 32f, UiKit.HeaderOrange, TextAlignmentOptions.Center);
        UiKit.FromTop(_title.rectTransform, Side, TitleTop, Width - 2f * Side, 44f);

        BuildList(body);

        var gui = InventoryGui.instance;
        var source = gui != null ? gui.m_craftButton : null;
        var detailTop = ListTop + ListH + 8f;
        _details = UiKit.MakeText(_window, "Details", body, 18f, UiKit.TextLight, TextAlignmentOptions.MidlineLeft);
        UiKit.FromTop(_details.rectTransform, Side + 4f, detailTop, Width - 2f * Side - 8f, 26f);

        _partButton = MakeSelector(body);
        UiKit.FromTop((RectTransform)_partButton.transform, Side, detailTop + 30f, Width - 2f * Side, 34f);

        _status = UiKit.MakeText(_window, "Status", body, 17f, UiKit.ErrorRed, TextAlignmentOptions.MidlineLeft, wrap: true);
        UiKit.FromTop(_status.rectTransform, Side + 4f, detailTop + 66f, Width - 2f * Side - 8f, 40f);

        var buttonsTop = detailTop + 110f;
        var total = 4f * ButtonW + 3f * ButtonGap;
        var x = (Width - total) * 0.5f;
        _play = UiKit.MakeButton(_window, "Play", source, "Play", OnPlayClicked, out _);
        UiKit.FromTop((RectTransform)_play.transform, x, buttonsTop, ButtonW, ButtonH);
        x += ButtonW + ButtonGap;
        _perform = UiKit.MakeButton(_window, "Perform", source, "Perform", OnPerformClicked, out _);
        UiKit.FromTop((RectTransform)_perform.transform, x, buttonsTop, ButtonW, ButtonH);
        x += ButtonW + ButtonGap;
        _repeat = UiKit.MakeButton(_window, "Repeat", source, "Repeat: Off", OnRepeatClicked, out _repeatLabel);
        UiKit.FromTop((RectTransform)_repeat.transform, x, buttonsTop, ButtonW, ButtonH);
        x += ButtonW + ButtonGap;
        _close = UiKit.MakeButton(_window, "Close", source, "Close", OnCloseClicked, out _);
        UiKit.FromTop((RectTransform)_close.transform, x, buttonsTop, ButtonW, ButtonH);

        var helpTop = buttonsTop + ButtonH + 8f;
        _helpPerform = UiKit.MakeText(_window, "HelpPerform", body, 16f, UiKit.TextDim, TextAlignmentOptions.Center);
        UiKit.FromTop(_helpPerform.rectTransform, Side, helpTop, Width - 2f * Side, 22f);
        _helpNav = UiKit.MakeText(_window, "HelpKeys", body, 15f, UiKit.TextGrey, TextAlignmentOptions.Center);
        UiKit.FromTop(_helpNav.rectTransform, Side, helpTop + 22f, Width - 2f * Side, 22f);
    }

    // Dark pane > scroll view (vertical, clamped) > viewport (RectMask2D) > content; pooled rows; bar on the right.
    private static void BuildList(TMP_Text body)
    {
        var pane = UiKit.MakeImage(_window, "ListPane", UiKit.PaneFallback, raycast: true);
        UiKit.FromTop(pane.rectTransform, Side, ListTop, Width - 2f * Side, ListH);
        if (!UiKit.CopyLook(pane, UiKit.PaneSource()))
        {
            pane.color = UiKit.PaneFallback;
        }
        pane.canvasRenderer.cullTransparentMesh = false; // wheel over empty list space still scrolls

        var gui = InventoryGui.instance;
        var sbSource = gui != null ? gui.m_recipeListScroll : null;
        var sourceW = sbSource != null ? ((RectTransform)sbSource.transform).rect.width : 0f;
        var sbW = Mathf.Clamp(sourceW > 1f ? sourceW : 12f, 8f, 22f);
        var sb = UiKit.CloneScrollbar(pane.transform, "Scrollbar", sbSource);
        var sbRt = (RectTransform)sb.transform;
        sbRt.anchorMin = new Vector2(1f, 0f);
        sbRt.anchorMax = new Vector2(1f, 1f);
        sbRt.pivot = new Vector2(1f, 0.5f);
        sbRt.offsetMin = new Vector2(-sbW - 4f, 6f);
        sbRt.offsetMax = new Vector2(-4f, -6f);
        sbRt.localScale = Vector3.one;

        _scroll = pane.gameObject.AddComponent<ScrollRect>();
        _viewport = UiKit.Rect(pane.transform, "Viewport");
        _viewport.anchorMin = Vector2.zero;
        _viewport.anchorMax = Vector2.one;
        _viewport.pivot = new Vector2(0.5f, 1f);
        _viewport.offsetMin = new Vector2(RowInset, RowInset);
        _viewport.offsetMax = new Vector2(-sbW - 10f, -RowInset);
        _viewport.gameObject.AddComponent<RectMask2D>();
        _content = UiKit.Rect(_viewport, "Content");
        _content.anchorMin = new Vector2(0f, 1f);
        _content.anchorMax = new Vector2(1f, 1f);
        _content.pivot = new Vector2(0.5f, 1f);
        _content.anchoredPosition = Vector2.zero;
        _content.sizeDelta = new Vector2(0f, RowH);

        _scroll.viewport = _viewport;
        _scroll.content = _content;
        _scroll.horizontal = false;
        _scroll.vertical = true;
        _scroll.movementType = ScrollRect.MovementType.Clamped;
        _scroll.inertia = true;
        _scroll.decelerationRate = 0.135f;
        _scroll.scrollSensitivity = VanillaScrollSensitivity(gui);
        _scroll.verticalScrollbar = sb;
        _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        // Selected-row look: the crafting row's "selected" image when there, else warm glow.
        Image selectedSource = null;
        if (gui != null && gui.m_recipeElementPrefab != null)
        {
            selectedSource = UiKit.FindImage(gui.m_recipeElementPrefab.transform, "selected");
        }

        var pool = Mathf.CeilToInt(ListH / RowH) + 2;
        for (var p = 0; p < pool; p++)
        {
            Rows.Add(MakeRow(p, body, selectedSource));
        }
    }

    private static RowView MakeRow(int slot, TMP_Text body, Image selectedSource)
    {
        var bg = UiKit.MakeImage(_content, "Row" + slot, Color.white, raycast: true);
        var rt = bg.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, RowH - 2f);
        rt.anchoredPosition = new Vector2(0f, -slot * RowH);
        bg.canvasRenderer.cullTransparentMesh = false;
        var button = bg.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        var colors = ColorBlock.defaultColorBlock;
        colors.normalColor = new Color(1f, 1f, 1f, 0f);
        colors.highlightedColor = new Color(1f, 0.9f, 0.7f, 0.1f);
        colors.pressedColor = new Color(1f, 0.85f, 0.6f, 0.18f);
        colors.selectedColor = new Color(1f, 1f, 1f, 0f);
        colors.disabledColor = new Color(1f, 1f, 1f, 0f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.onClick.AddListener(() => OnRowClicked(slot));

        var selected = UiKit.MakeImage(rt, "Selected", new Color(1f, 0.72f, 0.3f, 0.22f));
        UiKit.Fill(selected.rectTransform);
        if (selectedSource != null && UiKit.CopyLook(selected, selectedSource))
        {
            selected.color = new Color(selected.color.r, selected.color.g, selected.color.b, Mathf.Max(0.5f, selected.color.a));
        }
        selected.enabled = false;

        var title = UiKit.MakeText(rt, "Title", body, 20f, UiKit.TextLight, TextAlignmentOptions.TopLeft);
        var trt = title.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.pivot = new Vector2(0f, 1f);
        trt.offsetMin = new Vector2(12f, 4f);
        trt.offsetMax = new Vector2(-10f, -3f);

        var info = UiKit.MakeText(rt, "Info", body, 15f, UiKit.TextDim, TextAlignmentOptions.BottomLeft);
        var irt = info.rectTransform;
        irt.anchorMin = new Vector2(0f, 0f);
        irt.anchorMax = new Vector2(1f, 0f);
        irt.pivot = new Vector2(0f, 0f);
        irt.offsetMin = new Vector2(12f, 3f);
        irt.offsetMax = new Vector2(-10f, 22f);

        return new RowView
        {
            Go = bg.gameObject,
            Rt = rt,
            Button = button,
            Selected = selected,
            Title = title,
            Info = info,
        };
    }

    // Part chooser: dark pane strip with "<  Part: ...  >", click = next part. Own button (a stretched craft button
    // sprite would look wrong this wide).
    private static Button MakeSelector(TMP_Text body)
    {
        var bg = UiKit.MakeImage(_window, "Part", UiKit.PaneFallback, raycast: true);
        if (!UiKit.CopyLook(bg, UiKit.PaneSource()))
        {
            bg.color = UiKit.PaneFallback;
        }
        bg.canvasRenderer.cullTransparentMesh = false;
        var button = bg.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        var colors = ColorBlock.defaultColorBlock;
        colors.normalColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        colors.highlightedColor = new Color(1f, 0.92f, 0.75f, 1f);
        colors.pressedColor = new Color(1f, 0.85f, 0.6f, 1f);
        colors.selectedColor = colors.normalColor;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.onClick.AddListener(OnPartClicked);

        var left = UiKit.MakeText(bg.transform, "Prev", body, 20f, UiKit.Gold, TextAlignmentOptions.MidlineLeft);
        UiKit.Fill(left.rectTransform);
        left.rectTransform.offsetMin = new Vector2(12f, 0f);
        left.text = "<";
        var right = UiKit.MakeText(bg.transform, "Next", body, 20f, UiKit.Gold, TextAlignmentOptions.MidlineRight);
        UiKit.Fill(right.rectTransform);
        right.rectTransform.offsetMax = new Vector2(-12f, 0f);
        right.text = ">";
        var label = UiKit.MakeText(bg.transform, "Label", body, 17f, UiKit.TextLight, TextAlignmentOptions.Center);
        UiKit.Fill(label.rectTransform);
        label.rectTransform.offsetMin = new Vector2(34f, 0f);
        label.rectTransform.offsetMax = new Vector2(-34f, 0f);
        label.text = "Part: Automatic";
        _partLabel = label;
        return button;
    }

    // Wheel speed of the vanilla recipe list (the input backend decide the wheel step size), else a fair guess.
    private static float VanillaScrollSensitivity(InventoryGui gui)
    {
        var root = gui != null ? gui.m_recipeListRoot : null;
        var vanilla = root != null ? root.GetComponentInParent<ScrollRect>(true) : null;
        return vanilla != null && vanilla.scrollSensitivity > 0f ? vanilla.scrollSensitivity : 30f;
    }

    // ---------------------------------------------------------------- list

    private static void RebuildItems()
    {
        Items.Clear();
        Items.Add(new Item { Kind = ItemKind.Song, Entry = SongLibrary.FreePlay });
        foreach (var song in SongLibrary.Presets)
        {
            Items.Add(new Item { Kind = ItemKind.Song, Entry = song });
        }
        _listVersionShown = SongShare.ListVersion;
        _progressShown = SongShare.ProgressVersion;
        var shared = SongShare.WindowSongs(out var showShared, out var sharedHint);
        if (showShared)
        {
            Items.Add(new Item { Kind = ItemKind.Header, Text = ServerHeader });
            foreach (var song in shared)
            {
                Items.Add(new Item { Kind = ItemKind.Song, Entry = song });
            }
            if (!string.IsNullOrEmpty(sharedHint))
            {
                Items.Add(new Item { Kind = ItemKind.Hint, Text = sharedHint });
            }
        }
        Items.Add(new Item { Kind = ItemKind.Header, Text = MidiHeader });
        if (!ServerRules.Current.AllowPlayerSongs)
        {
            Items.Add(new Item { Kind = ItemKind.Hint, Text = Performance.PlayerSongsOff });
        }
        else
        {
            var midi = SongLibrary.ScanMidiFolder(out var error);
            foreach (var song in midi)
            {
                Items.Add(new Item { Kind = ItemKind.Song, Entry = song });
            }
            if (midi.Count == 0)
            {
                Items.Add(new Item { Kind = ItemKind.Hint, Text = "Put .mid files in " + SongLibrary.Folder });
            }
            if (!string.IsNullOrEmpty(error))
            {
                Items.Add(new Item { Kind = ItemKind.Hint, Text = error });
            }
        }
        _content.sizeDelta = new Vector2(0f, Mathf.Max(RowH, Items.Count * RowH));
        _boundFirst = -1;
        _rowsDirty = true;
    }

    // List again (server list came) with the same song selected when it is still there; the selection kept in view
    // (rows above it may have come), double-click memory forgotten (rows moved under the cursor).
    private static void RebuildKeepSelection()
    {
        var old = _selected;
        var id = SelectedEntry != null ? SelectedEntry.Id : null;
        RebuildItems();
        _lastClickItem = -1;
        var index = Find(id);
        if (index >= 0)
        {
            _selected = index;
        }
        else
        {
            _selected = -1;
            Select(NextSong(-1, 1), keepPart: false, loadNow: false);
        }
        if (_selected >= 0 && _selected != old)
        {
            ScrollTo(_selected, centre: false);
        }
        Refresh();
    }

    // Read (own or host file) or get (downloaded server song) the entry's MIDI data. A server song not here yet is
    // asked for only on a pick; else only the session cache is looked at.
    private static void LoadEntry(SongEntry entry, bool pick)
    {
        if (entry.Source == SongSource.Server && entry.Path == null)
        {
            if (pick)
            {
                SongShare.Fetch(entry);
            }
            else
            {
                SongShare.FromCache(entry);
            }
        }
        else
        {
            SongLibrary.Load(entry); // fills parts, length, info, error (cached while the file stays the same)
        }
    }

    private static bool NeedsDownload(SongEntry e) =>
        e != null && e.Source == SongSource.Server && e.Path == null && e.Score == null && e.Error == null;

    // Play/Perform pressed while the song was coming: start it now that it is here (still selected), or say why not.
    // True = it started (window closed).
    private static bool StartWhenReady()
    {
        var w = _startWhenReady;
        if (w == null)
        {
            return false;
        }
        if (w != SelectedEntry)
        {
            _startWhenReady = null;
            return false;
        }
        if (w.Score != null)
        {
            _startWhenReady = null;
            return StartSelected(_startMiniGame);
        }
        if (w.Error != null || !SongShare.IsDownloading(w))
        {
            _startWhenReady = null;
            SetStatus(w.Error ?? w.Pending);
        }
        return false;
    }

    private static int Find(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return -1;
        }
        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i].Kind == ItemKind.Song && Items[i].Entry.Id == id)
            {
                return i;
            }
        }
        return -1;
    }

    // Next song item from 'from' in direction dir (headers and hints skipped); -1 = none.
    private static int NextSong(int from, int dir)
    {
        for (var i = from + dir; i >= 0 && i < Items.Count; i += dir)
        {
            if (Items[i].Kind == ItemKind.Song)
            {
                return i;
            }
        }
        return -1;
    }

    private static SongEntry SelectedEntry =>
        _selected >= 0 && _selected < Items.Count && Items[_selected].Kind == ItemKind.Song ? Items[_selected].Entry : null;

    // keepPart: the remembered part when this is the last song played (window open), else Automatic.
    // loadNow false (keys): a MIDI file is read only when the selection rest LoadRest, never on each repeat step.
    // pick: the player chose this song (click, test): a server song not here yet is downloaded. Keys moving over it,
    // or the window opening on it, never download.
    private static void Select(int index, bool keepPart, bool loadNow, bool pick = false)
    {
        if (index < 0 || index >= Items.Count || Items[index].Kind != ItemKind.Song)
        {
            return;
        }
        if (index == _selected)
        {
            if (loadNow && _loadPending)
            {
                LoadSelected(pick);
            }
            else if (loadNow && pick && Items[index].Entry.Source == SongSource.Server)
            {
                LoadSelected(pick: true); // picked again after a failed download: ask again (no-op while it is coming)
            }
            return;
        }
        _selected = index;
        _startWhenReady = null;
        var entry = Items[index].Entry;
        _loadPending = false;
        if (entry.Source != SongSource.Preset)
        {
            if (loadNow)
            {
                LoadEntry(entry, pick);
            }
            else
            {
                _loadPending = true;
                _loadAt = Time.unscaledTime + LoadRest;
            }
        }
        _part = -1;
        if (keepPart && entry.Id == Performance.LastSongId && Performance.LastPart >= 0 && Performance.LastPart < entry.Parts.Count)
        {
            _part = Performance.LastPart;
        }
        SetStatus(null);
        _rowsDirty = true;
        Refresh();
    }

    // Read the selected MIDI file now (rest reached, or Play / Perform / part chooser need it; pick = download a
    // server song not here yet).
    private static void LoadSelected(bool pick)
    {
        _loadPending = false;
        var e = SelectedEntry;
        if (e == null || e.Source == SongSource.Preset)
        {
            return;
        }
        LoadEntry(e, pick);
        _rowsDirty = true;
        Refresh();
    }

    private static void Move(int dir)
    {
        var next = _selected < 0 ? NextSong(-1, 1) : NextSong(_selected, dir);
        if (next < 0)
        {
            return;
        }
        Select(next, keepPart: false, loadNow: false);
        ScrollTo(next, centre: false);
        if (ZInput.IsGamepadActive() && GamepadRumble.instance != null)
        {
            GamepadRumble.instance.PlayGlobalSelectVibration();
        }
    }

    // Scroll so the item is in view (centre = middle of the view, used at open).
    private static void ScrollTo(int index, bool centre)
    {
        if (index < 0 || _content == null)
        {
            return;
        }
        var view = _viewport.rect.height > 1f ? _viewport.rect.height : ListH - 2f * RowInset;
        var max = Mathf.Max(0f, Items.Count * RowH - view);
        var top = index * RowH;
        var y = _content.anchoredPosition.y;
        if (centre)
        {
            y = top - (view - RowH) * 0.5f;
        }
        else if (top < y)
        {
            y = top;
        }
        else if (top + RowH > y + view)
        {
            y = top + RowH - view;
        }
        y = Mathf.Clamp(y, 0f, max);
        if (!Mathf.Approximately(y, _content.anchoredPosition.y))
        {
            _scroll.StopMovement();
            _content.anchoredPosition = new Vector2(0f, y);
        }
    }

    // Pool rows to the items in view. Rebinds only when the first visible item or the data changed.
    private static void BindRows()
    {
        if (_content == null)
        {
            return;
        }
        var first = Mathf.Max(0, (int)(_content.anchoredPosition.y / RowH));
        if (first == _boundFirst && !_rowsDirty)
        {
            return;
        }
        _boundFirst = first;
        _rowsDirty = false;
        for (var p = 0; p < Rows.Count; p++)
        {
            var row = Rows[p];
            var index = first + p;
            if (index >= Items.Count)
            {
                row.Bound = -1;
                UiKit.SetActive(row.Go, false);
                continue;
            }
            UiKit.SetActive(row.Go, true);
            row.Bound = index;
            var pos = new Vector2(0f, -index * RowH);
            if (row.Rt.anchoredPosition != pos)
            {
                row.Rt.anchoredPosition = pos;
            }
            Bind(row, Items[index], index == _selected);
        }
    }

    private static void Bind(RowView row, Item item, bool selected)
    {
        row.Selected.enabled = selected;
        switch (item.Kind)
        {
            case ItemKind.Header:
                row.Button.interactable = false;
                SetRowTitle(row, item.Text, 20f, UiKit.HeaderOrange, TextAlignmentOptions.MidlineLeft, wrap: false);
                UiKit.SetText(row.Info, "");
                break;
            case ItemKind.Hint:
                row.Button.interactable = false;
                SetRowTitle(row, item.Text, 15f, UiKit.TextDim, TextAlignmentOptions.MidlineLeft, wrap: true);
                UiKit.SetText(row.Info, "");
                break;
            default:
            {
                var e = item.Entry;
                var bad = e.Error != null;
                row.Button.interactable = true;
                SetRowTitle(row, e.Title, 20f, bad ? UiKit.TextGrey : UiKit.TextLight, TextAlignmentOptions.TopLeft, wrap: false);
                UiKit.SetText(row.Info, bad ? e.Error : e.Pending ?? e.Info);
                UiKit.SetColor(row.Info, bad ? Dim(UiKit.ErrorRed) : UiKit.TextDim);
                break;
            }
        }
    }

    private static void SetRowTitle(RowView row, string text, float size, Color color, TextAlignmentOptions align, bool wrap)
    {
        var t = row.Title;
        if (!Mathf.Approximately(t.fontSize, size))
        {
            t.fontSize = size;
        }
        if (t.alignment != align)
        {
            t.alignment = align;
        }
        var mode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        if (t.textWrappingMode != mode)
        {
            t.textWrappingMode = mode;
        }
        UiKit.SetColor(t, color);
        UiKit.SetText(t, text ?? "");
    }

    private static Color Dim(Color c) => new Color(c.r * 0.85f, c.g * 0.85f, c.b * 0.85f, c.a);

    // ---------------------------------------------------------------- details

    // Details line, part button, Play/Perform state. On selection or data change only.
    private static void Refresh()
    {
        if (_details == null)
        {
            return;
        }
        var e = SelectedEntry;
        if (e == null)
        {
            UiKit.SetText(_details, NoSongText);
            UiKit.SetColor(_details, UiKit.TextDim);
        }
        else if (e.Error != null)
        {
            UiKit.SetText(_details, e.Title + ": " + e.Error);
            UiKit.SetColor(_details, UiKit.ErrorRed);
        }
        else if (e.Pending != null)
        {
            UiKit.SetText(_details, e.Title + "   -   " + e.Pending);
            UiKit.SetColor(_details, UiKit.TextDim);
        }
        else
        {
            UiKit.SetText(_details, string.IsNullOrEmpty(e.Info) ? e.Title : e.Title + "   -   " + e.Info);
            UiKit.SetColor(_details, UiKit.TextLight);
        }
        var showPart = e != null && e.Source != SongSource.Preset && e.Error == null && e.Parts.Count > 0;
        UiKit.SetActive(_partButton.gameObject, showPart);
        if (showPart)
        {
            UiKit.SetText(_partLabel, "Part: " + PartLabel(e, _part));
        }
        // A server song not here yet is playable too: Play / Perform download it and start it when it came.
        var playable = e != null && e.Error == null;
        _play.interactable = playable;
        _perform.interactable = playable;
    }

    private static string PartLabel(SongEntry e, int part)
    {
        if (part >= 0 && part < e.Parts.Count)
        {
            return e.Parts[part];
        }
        var auto = SongLibrary.AutoPart(e, _kind);
        if (auto >= 0 && auto < e.Parts.Count)
        {
            return "Automatic (" + e.Parts[auto] + ")";
        }
        switch (_kind)
        {
            case InstrumentKind.Flute:
                return "Automatic (no tune found)";
            case InstrumentKind.Lyre:
                return "Automatic (the tune and a bass line)";
            case InstrumentKind.Tambourine:
                return "Automatic (the drums, else the tune's rhythm)";
            default:
                return "Automatic";
        }
    }

    private static void CyclePart(int dir)
    {
        if (_loadPending || NeedsDownload(SelectedEntry))
        {
            LoadSelected(pick: true); // parts known only after the read (or the download)
        }
        var e = SelectedEntry;
        if (e == null || e.Source == SongSource.Preset || e.Error != null || e.Parts.Count == 0)
        {
            return;
        }
        // Order: Automatic (-1), 0, 1, ... Count-1, then Automatic again.
        var n = e.Parts.Count + 1;
        var k = (_part + 1 + dir) % n;
        if (k < 0)
        {
            k += n;
        }
        _part = k - 1;
        SetStatus(null);
        Refresh();
    }

    private static void SetStatus(string text)
    {
        if (_status != null)
        {
            UiKit.SetText(_status, text ?? "");
        }
    }

    // ---------------------------------------------------------------- actions

    // Play or Perform the selected song. Success: Performance closes the window. Failure: reason in the window.
    private static bool StartSelected(bool miniGame)
    {
        if (_loadPending)
        {
            LoadSelected(pick: false); // its error (bad file) show here, not as a failed start
        }
        var e = SelectedEntry;
        if (e == null)
        {
            SetStatus(NoSongText);
            return false;
        }
        if (NeedsDownload(e))
        {
            SongShare.Fetch(e); // from the cache at once, else asked for (no-op while it is coming)
            if (e.Score == null)
            {
                if (e.Error == null && SongShare.IsDownloading(e))
                {
                    _startWhenReady = e; // starts when it came (Update), details line show progress
                    _startMiniGame = miniGame;
                    SetStatus(null);
                }
                else
                {
                    SetStatus(e.Error ?? e.Pending);
                }
                _rowsDirty = true;
                Refresh();
                return false;
            }
        }
        if (e.Error != null)
        {
            SetStatus(e.Error);
            return false;
        }
        string error;
        var ok = e.Source == SongSource.FreePlay
            ? Performance.StartFreePlay(out error)
            : miniGame ? Performance.StartMiniGame(e, _part, out error) : Performance.StartAuto(e, _part, out error);
        if (ok)
        {
            return true;
        }
        if (IsOpen)
        {
            SetStatus(string.IsNullOrEmpty(error) ? CannotPlay : error);
            _rowsDirty = true; // a MIDI file may have shown its error only now
            Refresh();
        }
        return false;
    }

    private static void OnPlayClicked() => Guard("SongWindow Play", () => { StartSelected(miniGame: false); });

    private static void OnPerformClicked() => Guard("SongWindow Perform", () => { StartSelected(miniGame: true); });

    private static void OnRepeatClicked() => Guard("SongWindow Repeat", ToggleRepeat);

    private static void OnCloseClicked() => Guard("SongWindow Close", Performance.CloseWindow);

    private static void OnPartClicked() => Guard("SongWindow Part", () => CyclePart(1));

    private static void ToggleRepeat()
    {
        Performance.Repeat = !Performance.Repeat;
        _repeatKnown = false;
        if (_repeatLabel != null)
        {
            _repeatShown = Performance.Repeat;
            _repeatKnown = true;
            UiKit.SetText(_repeatLabel, _repeatShown ? "Repeat: On" : "Repeat: Off");
        }
    }

    // Click on a row: select it; second click on the same row soon after = Play.
    private static void OnRowClicked(int slot)
    {
        try
        {
            if (!IsOpen || slot < 0 || slot >= Rows.Count)
            {
                return;
            }
            var index = Rows[slot].Bound;
            if (index < 0 || index >= Items.Count || Items[index].Kind != ItemKind.Song)
            {
                return;
            }
            var now = Time.unscaledTime;
            var twice = index == _lastClickItem && now - _lastClickAt < DoubleClick;
            _lastClickItem = index;
            _lastClickAt = now;
            Select(index, keepPart: false, loadNow: true, pick: true);
            if (twice)
            {
                _lastClickItem = -1;
                StartSelected(miniGame: false);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("SongWindow row click", e);
        }
    }

    private static void Guard(string site, Action action)
    {
        try
        {
            if (IsOpen)
            {
                action();
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report(site, e);
        }
    }

    // Me buttons have navigation None: a click never select them (Selectable.OnPointerDown skip it). Only safety: if
    // some other code select one of ours, me drop it at close, so no hidden object keep the event-system selection.
    private static void DropSelection()
    {
        var system = EventSystem.current;
        var selected = system != null ? system.currentSelectedGameObject : null;
        if (selected != null && _root != null && selected.transform.IsChildOf(_root.transform))
        {
            system.SetSelectedGameObject(null);
        }
    }

    // ---------------------------------------------------------------- keys

    // Raw keys (ZInput throw on keys it cannot map: checked once). Arrow keys and Enter are not game keys by default.
    private static void CheckKeys()
    {
        if (_keysChecked)
        {
            return;
        }
        _keysChecked = true;
        _arrowsOk = LaneKeys.IsUsableKey(KeyCode.UpArrow) && LaneKeys.IsUsableKey(KeyCode.DownArrow)
                    && LaneKeys.IsUsableKey(KeyCode.LeftArrow) && LaneKeys.IsUsableKey(KeyCode.RightArrow);
        _enterOk = LaneKeys.IsUsableKey(KeyCode.Return);
        _padEnterOk = LaneKeys.IsUsableKey(KeyCode.KeypadEnter);
        if (!_arrowsOk || !_enterOk)
        {
            Log.Debug($"Song window keys: arrows {_arrowsOk}, Enter {_enterOk} (the game cannot read the others).");
        }
    }

    // True = a song started (window gone).
    private static bool HandleKeys()
    {
        var pad = PadInput.Active;
        var stickY = pad ? ZInput.GetJoyLeftStickY() : 0f; // > 0 = down
        var stickX = pad ? ZInput.GetJoyLeftStickX() : 0f;

        // Up / down: own edge + own repeat (D-pad and stick names repeat by themselves in ZInput).
        var dir = 0;
        if ((_arrowsOk && KeyHeld(KeyCode.UpArrow)) || (pad && (ZInput.GetButton("JoyDPadUp") || stickY < -0.5f)))
        {
            dir = -1;
        }
        else if ((_arrowsOk && KeyHeld(KeyCode.DownArrow)) || (pad && (ZInput.GetButton("JoyDPadDown") || stickY > 0.5f)))
        {
            dir = 1;
        }
        if (_dirLatched)
        {
            // Held since before Open: no scroll until it was let go once.
            if (dir == 0)
            {
                _dirLatched = false;
            }
            dir = 0;
        }
        var now = Time.unscaledTime;
        if (dir != _heldDir)
        {
            _heldDir = dir;
            if (dir != 0)
            {
                Move(dir);
                _repeatAt = now + RepeatDelay;
            }
        }
        else if (dir != 0 && now >= _repeatAt)
        {
            Move(dir);
            _repeatAt = now + RepeatEvery;
        }

        // Left / right: part, one step per press.
        var side = 0;
        if ((_arrowsOk && KeyHeld(KeyCode.LeftArrow)) || (pad && (ZInput.GetButton("JoyDPadLeft") || stickX < -0.5f)))
        {
            side = -1;
        }
        else if ((_arrowsOk && KeyHeld(KeyCode.RightArrow)) || (pad && (ZInput.GetButton("JoyDPadRight") || stickX > 0.5f)))
        {
            side = 1;
        }
        if (side == 0)
        {
            _sideHeld = false;
        }
        else if (!_sideHeld)
        {
            _sideHeld = true;
            CyclePart(side);
        }

        var enter = (_enterOk && KeyDown(KeyCode.Return)) || (_padEnterOk && KeyDown(KeyCode.KeypadEnter));
        if (enter)
        {
            // Same press no open chat. Chat.Update prefix (InputPatches) cover frames where chat read first; this one
            // cover chat reading after us in the frame Enter close the window (prefix then see no window).
            ZInput.ResetButtonStatus("Chat");
            return StartSelected(miniGame: false);
        }
        if (pad)
        {
            if (ZInput.GetButtonDown("JoyButtonA"))
            {
                ZInput.ResetButtonStatus("JoyButtonA");
                var e = SelectedEntry;
                if (e != null && e.Source == SongSource.FreePlay)
                {
                    // Free play is the keyboard as a piano: no run the pad cannot play (like the rhythm game).
                    SetStatus(FreePlayOnKeyboard);
                    return false;
                }
                return StartSelected(miniGame: false);
            }
            if (ZInput.GetButtonDown("JoyButtonX"))
            {
                // Rhythm game lanes are keyboard keys: no run the pad cannot play.
                ZInput.ResetButtonStatus("JoyButtonX");
                SetStatus("The rhythm game is played on the keyboard (" + LaneKeys.Label(0) + " " + LaneKeys.Label(1) + " "
                          + LaneKeys.Label(2) + " " + LaneKeys.Label(3) + ").");
            }
            if (ZInput.GetButtonDown("JoyButtonY"))
            {
                ZInput.ResetButtonStatus("JoyButtonY");
                ToggleRepeat();
            }
        }
        return false;
    }

    // Raw key reads of the window (arrows, Enter): one place each.
    private static bool KeyHeld(KeyCode key)
    {
#if DEBUG
        if (TestHeldKey == key && key != KeyCode.None)
        {
            return true;
        }
#endif
        return ZInput.GetKey(key, false);
    }

    private static bool KeyDown(KeyCode key)
    {
#if DEBUG
        if (TestDownKey == key && key != KeyCode.None)
        {
            TestDownKey = KeyCode.None;
            return true;
        }
#endif
        return ZInput.GetKeyDown(key, false);
    }

#if DEBUG
    // ---------------------------------------------------------------- self-test helpers

    // Self test: a window key held (arrows) / pressed once (Enter) without a keyboard. None = no key.
    internal static KeyCode TestHeldKey;
    internal static KeyCode TestDownKey;

    internal static bool Built => _root != null;

    internal static string HelpNavText => _helpNav != null ? _helpNav.text : null;

    internal static string HelpPerformText => _helpPerform != null ? _helpPerform.text : null;

    internal static string RepeatText => _repeatLabel != null ? _repeatLabel.text : null;

    internal static string PartText => _partLabel != null && _partButton != null && _partButton.gameObject.activeSelf ? _partLabel.text : null;

    internal static string TitleText => _title != null ? _title.text : null;

    // The list as shown, top to bottom: "song|<id>|<title>", "header|<text>", "hint|<text>".
    internal static List<string> TestItems()
    {
        var list = new List<string>(Items.Count);
        foreach (var item in Items)
        {
            switch (item.Kind)
            {
                case ItemKind.Header:
                    list.Add("header|" + item.Text);
                    break;
                case ItemKind.Hint:
                    list.Add("hint|" + item.Text);
                    break;
                default:
                    list.Add("song|" + item.Entry.Id + "|" + item.Entry.Title);
                    break;
            }
        }
        return list;
    }

    // Info line of a song as its row shows it (error, download state or info). Null = not in the list.
    internal static string TestRowInfo(string songId)
    {
        var index = Find(songId);
        if (index < 0)
        {
            return null;
        }
        var e = Items[index].Entry;
        return e.Error ?? e.Pending ?? e.Info;
    }

    // The selected song has a row on screen (bound and inside the viewport).
    internal static bool SelectedInView
    {
        get
        {
            if (_selected < 0 || _content == null || _viewport == null)
            {
                return false;
            }
            var view = _viewport.rect.height > 1f ? _viewport.rect.height : ListH - 2f * RowInset;
            var top = _selected * RowH;
            var y = _content.anchoredPosition.y;
            return top >= y - 0.5f && top + RowH <= y + view + 0.5f;
        }
    }

    // Click the row of a song through its button (what a mouse click on the row calls). False = that song has no
    // row on screen (scrolled away) or the window is closed.
    internal static bool TestClickRow(string songId)
    {
        if (!IsOpen)
        {
            return false;
        }
        var index = Find(songId);
        if (index < 0)
        {
            return false;
        }
        ScrollTo(index, centre: false);
        BindRows();
        foreach (var row in Rows)
        {
            if (row.Bound == index && row.Go.activeSelf && row.Button.interactable)
            {
                row.Button.onClick.Invoke();
                return true;
            }
        }
        return false;
    }

    internal static string SelectedId => SelectedEntry != null ? SelectedEntry.Id : null;

    internal static int SelectedPart => _part;

    internal static string StatusText => _status != null ? _status.text : null;

    internal static string DetailsText => _details != null ? _details.text : null;

    internal static int ItemCount => Items.Count;

    // Select a song by id (like a click). False = not in the list or window closed.
    internal static bool TestSelect(string songId)
    {
        if (!IsOpen)
        {
            return false;
        }
        var index = Find(songId);
        if (index < 0)
        {
            return false;
        }
        Select(index, keepPart: false, loadNow: true, pick: true);
        ScrollTo(index, centre: false);
        BindRows();
        return true;
    }

    // Press a window button without the mouse: "Play", "Perform", "Repeat", "Part", "Close". False = unknown, window
    // closed or button off (Play/Perform need a playable song).
    internal static bool TestPress(string button)
    {
        if (!IsOpen)
        {
            return false;
        }
        switch (button)
        {
            case "Play":
                if (!_play.interactable)
                {
                    return false;
                }
                OnPlayClicked();
                return true;
            case "Perform":
                if (!_perform.interactable)
                {
                    return false;
                }
                OnPerformClicked();
                return true;
            case "Repeat":
                OnRepeatClicked();
                return true;
            case "Part":
                if (!_partButton.gameObject.activeSelf)
                {
                    return false;
                }
                OnPartClicked();
                return true;
            case "Close":
                OnCloseClicked();
                return true;
        }
        return false;
    }

    // Short text dump: canvas, window parts, bound rows.
    internal static string DescribeLayout()
    {
        var sb = new System.Text.StringBuilder();
        if (_root == null)
        {
            return "Song window: not built.";
        }
        sb.Append("Song window: open ").Append(IsOpen).Append(", items ").Append(Items.Count).Append(", selected ")
            .Append(_selected).Append(" (").Append(SelectedId ?? "none").Append("), part ").Append(_part)
            .Append(", group priority ").Append(_group != null ? _group.m_groupPriority : 0)
            .Append(", screen ").Append(Screen.width).Append('x').Append(Screen.height).Append('\n');
        if (_window != null)
        {
            var corners = new Vector3[4];
            _window.GetWorldCorners(corners);
            sb.Append("Window on screen: (").Append(corners[0].x.ToString("F0")).Append(',').Append(corners[0].y.ToString("F0"))
                .Append(")-(").Append(corners[2].x.ToString("F0")).Append(',').Append(corners[2].y.ToString("F0")).Append(")\n");
        }
        UiKit.Describe(sb, _root.transform, 0, 3);
        foreach (var row in Rows)
        {
            sb.Append("Row slot -> item ").Append(row.Bound).Append(row.Go.activeSelf ? " [on] " : " [off] ")
                .Append(row.Title.text).Append(UiKit.DrawFlag(row.Title)).Append(" | ").Append(row.Info.text)
                .Append(UiKit.DrawFlag(row.Info)).Append(row.Selected.enabled ? " (selected)" : "").Append('\n');
        }
        return sb.ToString();
    }
#endif
}
