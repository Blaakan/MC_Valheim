using System;
using System.Globalization;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MC.Exploration.MusicInstrumentsMod;

// Me = what the player sees while playing. Own overlay canvas (order 450: above the HUD at 400, under the inventory at
// 600) with the game GUI scale; stays up when the HUD is hidden (Ctrl+F3). No raycaster: never takes a click.
//   mini-game  low, right of the character: NoteField (meter, lanes, notes, hit line; one mesh, own nested canvas),
//              key labels under the line, judgement (fades 0.6 s), countdown during the lead-in, title, accuracy,
//              streak, meter state (left of the meter), "Encore! +N comfort" banner (2.5 s), stop hint
//   autoplay   small panel at the same place: "<title>   m:ss / m:ss   (repeat)" and the stop hint
//   free play  panel at the same place: title, a piano (17 white keys, 12 black keys between them, like FreePlayKeys
//              on the keyboard): each key shows its keyboard key, white keys also the note; lit while held; tambourine:
//              its four keys named by hit, the others greyed; the Space / stop hint
// Performance feeds me every frame (Update / UpdateAutoplay) and calls Hide on stop. Texts are set only when their
// value changes; fades use the renderer alpha (no mesh rebuild). A watchdog hides me when nobody fed me for a second
// (performance code failed before reaching me).
internal static class MiniGameHud
{
    private const int WantedOrder = 450;
    private const float BottomY = 150f;
    // Lane centre at 73.5 % of the screen width (+451 of 1920 right of the middle): character in the middle stay
    // free; share of the width, so wide screens and big GUI scale keep it clear of both. Key hints sit further right.
    private const float LaneAnchorX = 0.735f;
    private const float ColumnW = 300f;   // text column left of the meter (right-aligned)
    private const float ColumnGap = 12f;
    private const float AutoW = 540f;
    private const float WhiteW = 36f;
    private const float WhiteH = 140f;
    private const float WhiteGap = 2f;
    private const float BlackW = 24f;
    private const float BlackH = 86f;
    private const float FreePad = 14f;
    private const int FreeKeys = FreePlayMap.KeyCount;
    private const string StopHint = "Right click or Esc: stop";
    private const string PadStopHint = "Start or right click: stop";
    private const string SpaceHint = "Hold Space: octave up    " + StopHint;
    private const float JudgementTime = 0.6f;
    private const float EncoreTime = 2.5f;
    private const float FeedTimeout = 1f;
    private const string RootName = ModInfo.Guid + ".PlayHud";

    private static readonly string[] CountdownTexts = { "1", "2", "3" };
    private static readonly string[] TambourineHits = { "Thump", "Hit", "Jingle", "Shake" };

    private static GameObject _root;
    private static Canvas _canvas;
    private static GameObject _mini;
    private static GameObject _auto;
    private static NoteField _field;
    private static TextMeshProUGUI _title;
    private static TextMeshProUGUI _encore;
    private static TextMeshProUGUI _countdown;
    private static TextMeshProUGUI _judgement;
    private static TextMeshProUGUI _accuracy;
    private static TextMeshProUGUI _streak;
    private static TextMeshProUGUI _meterLabel;
    private static TextMeshProUGUI _meterState;
    private static TextMeshProUGUI _hint;
    private static readonly TextMeshProUGUI[] Keys = new TextMeshProUGUI[Chart.Lanes];
    private static readonly string[] KeysShown = new string[Chart.Lanes];  // label in Keys (a lane key changed live: new one)
    private static int _hintPadShown = -1;
    private static readonly TextMeshProUGUI[] HitNames = new TextMeshProUGUI[Chart.Lanes];
    private static TextMeshProUGUI _autoLine;
    private static TextMeshProUGUI _autoHint;
    private static GameObject _free;
    private static TextMeshProUGUI _freeTitle;
    private static TextMeshProUGUI _freeHint;
    private static int _freeHintPad = -1;
    private static readonly Image[] FreeBoxes = new Image[FreeKeys];
    private static readonly TextMeshProUGUI[] FreeKeyText = new TextMeshProUGUI[FreeKeys];
    private static readonly TextMeshProUGUI[] FreeNoteText = new TextMeshProUGUI[FreeKeys];  // white keys only
    private static readonly int[] FreePitch = new int[FreeKeys];
    private static readonly bool[] FreeLit = new bool[FreeKeys];
    private static readonly Color WhiteIdle = new Color(0.93f, 0.91f, 0.86f, 0.93f);
    private static readonly Color BlackIdle = new Color(0.09f, 0.08f, 0.07f, 0.96f);
    private static readonly Color WhiteUnused = new Color(0.93f, 0.91f, 0.86f, 0.25f);
    private static readonly Color BlackUnused = new Color(0.09f, 0.08f, 0.07f, 0.35f);
    private static readonly Color WhiteLit = new Color(1f, 0.8f, 0.38f, 1f);
    private static readonly Color BlackLit = new Color(0.86f, 0.6f, 0.18f, 1f);
    private static readonly Color InkDark = new Color(0.14f, 0.11f, 0.08f, 1f);
    private static readonly Color InkDim = new Color(0.36f, 0.31f, 0.25f, 1f);

    private static bool _miniShown;
    private static bool _autoShown;
    private static bool _freeShown;
    private static bool _freeLayoutKnown;
    private static InstrumentKind _freeKind;
    private static bool _freeOctaveUp;
    private static bool _buildFailed;
    private static float _lastFeed;

    // Change caches (texts rebuilt only when these move).
    private static MiniGame _game;
    private static int _accuracyShown = int.MinValue;
    private static int _streakShown = -1;
    private static int _meterStateShown = -1;
    private static int _countdownShown = -1;
    private static float _judgedAt = float.NaN;
    private static Judgement _judgedKind;
    private static int _judgedLane = int.MinValue;
    private static float _judgementAlpha = -1f;
    private static float _encoreAlpha = -1f;
    private static float _countdownAlpha = -1f;
    private static int _comfortShown = int.MinValue;
    private static string _autoTitle;
    private static int _autoTime = -1;
    private static int _autoLength = -1;
    private static bool _autoRepeat;

    internal static bool Shown => _miniShown || _autoShown || _freeShown;

    internal static float LastFeed => _lastFeed;

    // ---------------------------------------------------------------- feed

    internal static void Update(MiniGame game)
    {
        if (game == null)
        {
            Hide();
            return;
        }
        try
        {
            if (!Ensure())
            {
                return;
            }
            _lastFeed = Time.unscaledTime;
            ShowView(mini: true);
            if (!ReferenceEquals(game, _game))
            {
                NewGame(game);
            }
            Tick(game);
        }
        catch (Exception e)
        {
            PatchGuard.Report("MiniGameHud.Update", e);
        }
    }

    internal static void UpdateAutoplay(string title, float time, float length, bool repeat)
    {
        try
        {
            if (!Ensure())
            {
                return;
            }
            _lastFeed = Time.unscaledTime;
            ShowView(mini: false);
            var t = Mathf.Max(0, Mathf.FloorToInt(time));
            var l = Mathf.Max(0, Mathf.RoundToInt(length));
            if (t > l)
            {
                t = l;
            }
            if (!ReferenceEquals(title, _autoTitle) || t != _autoTime || l != _autoLength || repeat != _autoRepeat)
            {
                _autoTitle = title;
                _autoTime = t;
                _autoLength = l;
                _autoRepeat = repeat;
                _autoLine.text = (title ?? "") + "   " + Clock(t) + " / " + Clock(l) + (repeat ? "   (repeat)" : "");
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("MiniGameHud.UpdateAutoplay", e);
        }
    }

    // Free play, every frame: piano labels for this instrument and octave (rebuilt only when one changes), keys lit
    // while held.
    internal static void UpdateFreePlay(InstrumentKind kind, bool octaveUp)
    {
        try
        {
            if (!Ensure())
            {
                return;
            }
            _lastFeed = Time.unscaledTime;
            ShowFree();
            if (!_freeLayoutKnown || kind != _freeKind || octaveUp != _freeOctaveUp)
            {
                _freeLayoutKnown = true;
                _freeKind = kind;
                _freeOctaveUp = octaveUp;
                _freeHintPad = -1; // hint set below (device and instrument)
                _freeTitle.text = "Free play: " + InstrumentContent.DisplayName(kind)
                                  + (octaveUp && kind != InstrumentKind.Tambourine ? "   (octave up)" : "");
                for (var key = 0; key < FreeKeys; key++)
                {
                    var pitch = FreePlayMap.Pitch(kind, key, octaveUp);
                    FreePitch[key] = pitch;
                    FreeLit[key] = false;
                    var black = FreePlayMap.IsBlack(key);
                    FreeBoxes[key].color = pitch < 0 ? (black ? BlackUnused : WhiteUnused) : black ? BlackIdle : WhiteIdle;
                    UiKit.SetText(FreeKeyText[key], pitch < 0 ? "" : FreePlayKeys.Label(key));
                    if (FreeNoteText[key] != null)
                    {
                        UiKit.SetText(FreeNoteText[key], FreePlayMap.Name(kind, pitch));
                    }
                }
            }
            // Hint by device (pad: Start opens the menu, which stops) and instrument (tambourine: no octave).
            var pad = ZInput.IsGamepadActive() ? 1 : 0;
            if (pad != _freeHintPad)
            {
                _freeHintPad = pad;
                var stop = pad == 1 ? PadStopHint : StopHint;
                _freeHint.text = kind == InstrumentKind.Tambourine ? stop : "Hold Space: octave up    " + stop;
            }
            for (var key = 0; key < FreeKeys; key++)
            {
                var lit = FreePitch[key] >= 0 && FreePlayKeys.Held(key);
                if (lit != FreeLit[key])
                {
                    FreeLit[key] = lit;
                    var black = FreePlayMap.IsBlack(key);
                    FreeBoxes[key].color = lit ? (black ? BlackLit : WhiteLit) : black ? BlackIdle : WhiteIdle;
                }
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("MiniGameHud.UpdateFreePlay", e);
        }
    }

    internal static void Hide()
    {
        try
        {
            _miniShown = false;
            _autoShown = false;
            _freeShown = false;
            _game = null;
            if (_field != null)
            {
                _field.Game = null;
            }
            UiKit.SetActive(_root, false);
        }
        catch (Exception e)
        {
            PatchGuard.Report("MiniGameHud.Hide", e);
        }
    }

    internal static void Destroy()
    {
        _miniShown = false;
        _autoShown = false;
        _freeShown = false;
        _freeLayoutKnown = false;
        _game = null;
        _buildFailed = false;
        try
        {
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("MiniGameHud.Destroy", e);
        }
        _root = null;
        _canvas = null;
        _mini = null;
        _auto = null;
        _field = null;
        _title = null;
        _encore = null;
        _countdown = null;
        _judgement = null;
        _accuracy = null;
        _streak = null;
        _meterLabel = null;
        _meterState = null;
        _hint = null;
        _autoLine = null;
        _autoHint = null;
        _free = null;
        _freeTitle = null;
        _freeHint = null;
        for (var i = 0; i < FreeKeys; i++)
        {
            FreeBoxes[i] = null;
            FreeKeyText[i] = null;
            FreeNoteText[i] = null;
            FreeLit[i] = false;
        }
        for (var i = 0; i < Chart.Lanes; i++)
        {
            Keys[i] = null;
            KeysShown[i] = null;
            HitNames[i] = null;
        }
        _hintPadShown = -1;
        _autoTitle = null;
    }

    // ---------------------------------------------------------------- views

    private static bool Ensure()
    {
        if (_root != null)
        {
            return true;
        }
        if (_buildFailed || UiKit.Headless)
        {
            return false;
        }
        try
        {
            Build();
            return true;
        }
        catch (Exception e)
        {
            PatchGuard.Report("MiniGameHud.Build", e);
            Destroy();
            _buildFailed = true; // no new try every frame; world exit (Destroy) allows one again
            return false;
        }
    }

    private static void ShowView(bool mini)
    {
        if (mini != _miniShown || mini == _autoShown || _freeShown)
        {
            _miniShown = mini;
            _autoShown = !mini;
            _freeShown = false;
            UiKit.SetActive(_mini, mini);
            UiKit.SetActive(_auto, !mini);
            UiKit.SetActive(_free, false);
            if (!mini)
            {
                _game = null;
                _field.Game = null;
                _autoTitle = null;
            }
        }
        if (!_root.activeSelf)
        {
            _canvas.sortingOrder = UiKit.OrderAboveFrameBuffer(WantedOrder);
            _root.SetActive(true);
        }
    }

    private static void ShowFree()
    {
        if (!_freeShown || _miniShown || _autoShown)
        {
            _freeShown = true;
            _miniShown = false;
            _autoShown = false;
            UiKit.SetActive(_mini, false);
            UiKit.SetActive(_auto, false);
            UiKit.SetActive(_free, true);
            _game = null;
            if (_field != null)
            {
                _field.Game = null;
            }
            _autoTitle = null;
            _freeLayoutKnown = false;
        }
        if (!_root.activeSelf)
        {
            _canvas.sortingOrder = UiKit.OrderAboveFrameBuffer(WantedOrder);
            _root.SetActive(true);
        }
    }

    // New run: every cached value shown again; lane captions per instrument.
    private static void NewGame(MiniGame game)
    {
        _game = game;
        _field.Game = game;
        _accuracyShown = int.MinValue;
        _streakShown = -1;
        _meterStateShown = -1;
        _countdownShown = -1;
        _judgedAt = float.NaN;
        _judgedKind = Judgement.None;
        _judgedLane = int.MinValue;
        _comfortShown = int.MinValue;
        UiKit.SetText(_title, game.Title ?? "");
        var tambourine = game.Kind == InstrumentKind.Tambourine;
        for (var lane = 0; lane < Chart.Lanes; lane++)
        {
            KeysShown[lane] = game.LaneLabel(lane);
            UiKit.SetText(Keys[lane], KeysShown[lane]);
            UiKit.SetActive(HitNames[lane].gameObject, tambourine);
        }
        _judgementAlpha = -1f;
        _encoreAlpha = -1f;
        _countdownAlpha = -1f;
        SetAlpha(_judgement, ref _judgementAlpha, 0f);
        SetAlpha(_encore, ref _encoreAlpha, 0f);
        UiKit.SetActive(_countdown.gameObject, false);
    }

    // Renderer alpha only when it moved (each set dirty the canvas).
    private static void SetAlpha(TMP_Text t, ref float shown, float alpha)
    {
        if (alpha != shown)
        {
            shown = alpha;
            t.canvasRenderer.SetAlpha(alpha);
        }
    }

    private static void Tick(MiniGame game)
    {
        var clock = game.Clock;
        _field.SetVerticesDirty();

        // Lane key changed in the settings during the run: caption follow (Label cached: reference compare only).
        for (var lane = 0; lane < Chart.Lanes; lane++)
        {
            var label = game.LaneLabel(lane);
            if (!ReferenceEquals(label, KeysShown[lane]))
            {
                KeysShown[lane] = label;
                UiKit.SetText(Keys[lane], label);
            }
        }
        // Way out named for the device in use (pad: Start opens the menu, which ends the run).
        var pad = ZInput.IsGamepadActive() ? 1 : 0;
        if (pad != _hintPadShown)
        {
            _hintPadShown = pad;
            _hint.text = pad == 1 ? "Start or right click: stop" : "Right click or Esc: stop";
        }

        // Countdown 3 2 1 over the whole lead-in: lead-in cut in three equal steps (2.5 s = 0.83 s each), so the
        // first number last as long as the others.
        var left = game.LeadInLeft;
        var steps = left / (Judge.LeadIn / CountdownTexts.Length);
        var count = left > 0f ? Mathf.Clamp(Mathf.CeilToInt(steps), 1, CountdownTexts.Length) : 0;
        if (count != _countdownShown)
        {
            _countdownShown = count;
            UiKit.SetActive(_countdown.gameObject, count > 0);
            if (count > 0)
            {
                _countdown.text = CountdownTexts[count - 1];
            }
        }
        if (count > 0)
        {
            // Me pop each number in full, then fade it to a quarter over its step.
            var part = Mathf.Clamp01(steps - (count - 1));
            SetAlpha(_countdown, ref _countdownAlpha, Mathf.Clamp01(0.25f + 0.75f * part));
        }

        // Judgement over the judged lane, fading. New one = time, kind or lane moved (clock can repeat between frames).
        if (game.LastJudgementAt != _judgedAt || game.LastJudgement != _judgedKind || game.LastJudgementLane != _judgedLane)
        {
            _judgedAt = game.LastJudgementAt;
            _judgedKind = game.LastJudgement;
            _judgedLane = game.LastJudgementLane;
            string text;
            Color color;
            switch (game.LastJudgement)
            {
                case Judgement.Perfect:
                    text = "Perfect";
                    color = UiKit.Gold;
                    break;
                case Judgement.Good:
                    text = "Good";
                    color = new Color(0.62f, 0.9f, 0.52f, 1f);
                    break;
                case Judgement.Miss:
                case Judgement.Stray:
                    text = "Miss";
                    color = new Color(0.9f, 0.36f, 0.3f, 1f);
                    break;
                default:
                    text = "";
                    color = UiKit.TextLight;
                    break;
            }
            UiKit.SetText(_judgement, text);
            UiKit.SetColor(_judgement, color);
            var lane = Mathf.Clamp(game.LastJudgementLane, 0, Chart.Lanes - 1);
            var pos = new Vector2(NoteField.LaneCentre(lane) - 70f, NoteField.HitY + 16f);
            if (_judgement.rectTransform.anchoredPosition != pos)
            {
                _judgement.rectTransform.anchoredPosition = pos;
            }
        }
        var judgedAge = clock - game.LastJudgementAt;
        var judgedAlpha = judgedAge >= 0f && judgedAge < JudgementTime ? 1f - judgedAge / JudgementTime : 0f;
        SetAlpha(_judgement, ref _judgementAlpha, judgedAlpha);

        // Accuracy and streak.
        var acc = game.Accuracy;
        var accShown = acc < 0f ? -1 : Mathf.RoundToInt(acc * 100f);
        if (accShown != _accuracyShown)
        {
            _accuracyShown = accShown;
            _accuracy.text = accShown < 0 ? "Accuracy  -" : "Accuracy  " + accShown.ToString(CultureInfo.InvariantCulture) + "%";
        }
        var streak = game.Judge.Streak;
        if (streak != _streakShown)
        {
            _streakShown = streak;
            _streak.text = "Streak  " + streak.ToString(CultureInfo.InvariantCulture);
        }

        // Meter state (the bar itself is in the note field).
        var state = acc < 0f ? 2 : game.Meter.Waiting ? 0 : game.Meter.InFlow ? 1 : 3;
        if (state != _meterStateShown)
        {
            _meterStateShown = state;
            switch (state)
            {
                case 0:
                    _meterState.text = "Rest: the meter waits";
                    _meterState.color = NoteField.MeterWait;
                    break;
                case 1:
                    _meterState.text = "In tune: the meter fills";
                    _meterState.color = NoteField.MeterFlow;
                    break;
                case 2:
                    _meterState.text = "Hit a few notes to start";
                    _meterState.color = UiKit.TextDim;
                    break;
                default:
                    _meterState.text = "Hit more notes: the meter waits";
                    _meterState.color = new Color(0.92f, 0.45f, 0.35f, 1f);
                    break;
            }
        }

        // Encore banner.
        var encoreAge = clock - game.LastEncoreAt;
        var encoreAlpha = 0f;
        if (game.Encores > 0 && encoreAge >= 0f && encoreAge < EncoreTime)
        {
            var comfort = ServerRules.Current.ComfortBonus;
            if (comfort != _comfortShown)
            {
                _comfortShown = comfort;
                _encore.text = comfort > 0 ? "Encore! +" + comfort.ToString(CultureInfo.InvariantCulture) + " comfort" : "Encore!";
            }
            encoreAlpha = encoreAge > EncoreTime - 0.5f ? (EncoreTime - encoreAge) / 0.5f : 1f;
        }
        SetAlpha(_encore, ref _encoreAlpha, encoreAlpha);
    }

    private static string Clock(int seconds) =>
        (seconds / 60).ToString(CultureInfo.InvariantCulture) + ":" + (seconds % 60).ToString("00", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- build

    private static void Build()
    {
        // Old root may be gone by an outside destroy: old game and caches mean nothing for the new texts.
        _game = null;
        _autoTitle = null;
        _root = UiKit.MakeRootCanvas(RootName, WantedOrder, out _canvas);
        _root.AddComponent<HudWatchdog>();
        var rootRt = (RectTransform)_root.transform;
        var hud = UiKit.HudStyle();
        var big = UiKit.BigStyle();
        BuildMini(rootRt, hud, big);
        BuildAuto(rootRt, hud);
        BuildFree(rootRt, hud);
        _mini.SetActive(false);
        _auto.SetActive(false);
        _free.SetActive(false);
        _miniShown = false;
        _autoShown = false;
        _freeShown = false;
        _freeLayoutKnown = false;
        Log.Debug("Mini-game HUD built.");
    }

    // Layout in mini units: x 0 = meter left edge, lanes from NoteField.LanesX; texts left of the meter end ColumnGap
    // before it (right-aligned), so nothing reach further right than the lanes.
    private static void BuildMini(RectTransform rootRt, TMP_Text hud, TMP_Text big)
    {
        var w = NoteField.Width;
        var h = NoteField.LaneH;
        var mid = NoteField.LanesCentre;
        var mini = UiKit.Rect(rootRt, "MiniGame");
        mini.anchorMin = new Vector2(LaneAnchorX, 0f);
        mini.anchorMax = new Vector2(LaneAnchorX, 0f);
        mini.pivot = new Vector2(mid / w, 0f); // lane centre on the anchor
        mini.sizeDelta = new Vector2(w, h);
        mini.anchoredPosition = new Vector2(0f, BottomY);
        _mini = mini.gameObject;

        var fieldRt = UiKit.Rect(mini, "NoteField");
        UiKit.At(fieldRt, 0f, 0f, w, h);
        fieldRt.gameObject.AddComponent<Canvas>(); // nested canvas: per-frame mesh rebuild stays here
        _field = fieldRt.gameObject.AddComponent<NoteField>();
        _field.raycastTarget = false;
        _field.color = Color.white;

        for (var lane = 0; lane < Chart.Lanes; lane++)
        {
            var key = UiKit.MakeText(mini, "Key" + (lane + 1), hud, 22f, UiKit.TextLight, TextAlignmentOptions.Center);
            UiKit.At(key.rectTransform, NoteField.LaneX(lane), NoteField.KeyBottom, NoteField.LaneW, NoteField.KeyTop - NoteField.KeyBottom);
            key.enableAutoSizing = true; // long key names (LeftShift) shrink to the lane
            key.fontSizeMax = 22f;
            key.fontSizeMin = 10f;
            Keys[lane] = key;
            var name = UiKit.MakeText(mini, "Hit" + (lane + 1), hud, 14f, UiKit.TextDim, TextAlignmentOptions.Center);
            UiKit.At(name.rectTransform, NoteField.LaneCentre(lane) - 45f, h - 24f, 90f, 20f);
            name.text = TambourineHits[lane];
            name.gameObject.SetActive(false);
            HitNames[lane] = name;
        }

        _judgement = UiKit.MakeText(mini, "Judgement", big, 26f, UiKit.Gold, TextAlignmentOptions.Center);
        UiKit.At(_judgement.rectTransform, NoteField.LaneCentre(0) - 70f, NoteField.HitY + 16f, 140f, 36f);

        _countdown = UiKit.MakeText(mini, "Countdown", big, 84f, UiKit.TextLight, TextAlignmentOptions.Center);
        UiKit.At(_countdown.rectTransform, mid - 100f, h * 0.45f, 200f, 110f);
        _countdown.gameObject.SetActive(false);

        _title = UiKit.MakeText(mini, "Title", big, 26f, UiKit.TextLight, TextAlignmentOptions.Center);
        UiKit.At(_title.rectTransform, mid - 260f, h + 6f, 520f, 36f);

        _encore = UiKit.MakeText(mini, "Encore", big, 40f, UiKit.Gold, TextAlignmentOptions.Center);
        UiKit.At(_encore.rectTransform, mid - 320f, h + 44f, 640f, 54f);

        // Column left of the meter: meter caption at its top (the goal), then accuracy and streak.
        var colX = -ColumnGap - ColumnW;
        _meterLabel = UiKit.MakeText(mini, "MeterLabel", hud, 20f, UiKit.Gold, TextAlignmentOptions.MidlineRight);
        UiKit.At(_meterLabel.rectTransform, colX, h - 30f, ColumnW, 30f);
        _meterLabel.text = "Encore";
        _meterState = UiKit.MakeText(mini, "MeterState", hud, 16f, UiKit.TextDim, TextAlignmentOptions.MidlineRight);
        UiKit.At(_meterState.rectTransform, colX, h - 56f, ColumnW, 26f);
        _accuracy = UiKit.MakeText(mini, "Accuracy", hud, 21f, UiKit.TextLight, TextAlignmentOptions.MidlineRight);
        UiKit.At(_accuracy.rectTransform, colX, h - 110f, ColumnW, 30f);
        _streak = UiKit.MakeText(mini, "Streak", hud, 21f, UiKit.TextLight, TextAlignmentOptions.MidlineRight);
        UiKit.At(_streak.rectTransform, colX, h - 140f, ColumnW, 30f);

        _hint = UiKit.MakeText(mini, "Hint", hud, 16f, UiKit.TextDim, TextAlignmentOptions.Center);
        UiKit.At(_hint.rectTransform, mid - 220f, -30f, 440f, 24f);
        _hint.text = "Right click or Esc: stop";
        _hintPadShown = 0;
    }

    // Same place as the lanes (centre on LaneAnchorX, bottom at BottomY): character stay free here too.
    private static void BuildAuto(RectTransform rootRt, TMP_Text hud)
    {
        var panel = UiKit.MakeImage(rootRt, "Autoplay", new Color(0f, 0f, 0f, 0.5f));
        var rt = panel.rectTransform;
        rt.anchorMin = new Vector2(LaneAnchorX, 0f);
        rt.anchorMax = new Vector2(LaneAnchorX, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(AutoW, 66f);
        rt.anchoredPosition = new Vector2(0f, BottomY);
        _auto = panel.gameObject;

        _autoLine = UiKit.MakeText(rt, "Song", hud, 21f, UiKit.TextLight, TextAlignmentOptions.Center);
        UiKit.At(_autoLine.rectTransform, 12f, 30f, AutoW - 24f, 30f);
        _autoHint = UiKit.MakeText(rt, "Hint", hud, 16f, UiKit.TextDim, TextAlignmentOptions.Center);
        UiKit.At(_autoHint.rectTransform, 12f, 6f, AutoW - 24f, 24f);
        _autoHint.text = "Attack or Block: stop";
    }

    // Free play panel: title on top, the piano, hint under it. White keys first, black keys after (drawn on top), each
    // black key centred on the line after the white key it follows, like a piano.
    private static void BuildFree(RectTransform rootRt, TMP_Text hud)
    {
        var pianoW = FreePlayMap.WhiteCount * (WhiteW + WhiteGap) - WhiteGap;
        var w = pianoW + 2f * FreePad;
        var h = FreePad + 24f + 6f + WhiteH + 8f + 30f + FreePad;
        var panel = UiKit.MakeImage(rootRt, "FreePlay", new Color(0f, 0f, 0f, 0.45f));
        var rt = panel.rectTransform;
        rt.anchorMin = new Vector2(LaneAnchorX, 0f);
        rt.anchorMax = new Vector2(LaneAnchorX, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(0f, BottomY);
        _free = panel.gameObject;

        _freeTitle = UiKit.MakeText(rt, "Title", hud, 21f, UiKit.TextLight, TextAlignmentOptions.Center);
        UiKit.At(_freeTitle.rectTransform, FreePad, h - FreePad - 30f, w - 2f * FreePad, 30f);
        _freeHint = UiKit.MakeText(rt, "Hint", hud, 16f, UiKit.TextDim, TextAlignmentOptions.Center);
        UiKit.At(_freeHint.rectTransform, FreePad, FreePad - 4f, w - 2f * FreePad, 24f);
        _freeHint.text = SpaceHint;

        var keyBottom = FreePad + 24f + 6f;
        for (var pass = 0; pass < 2; pass++)
        {
            for (var key = 0; key < FreeKeys; key++)
            {
                var black = FreePlayMap.IsBlack(key);
                if (black != (pass == 1))
                {
                    continue;
                }
                var white = FreePlayMap.WhiteIndex(key);
                var box = UiKit.MakeImage(rt, "Key" + key, black ? BlackIdle : WhiteIdle);
                if (black)
                {
                    var x = FreePad + (white + 1) * (WhiteW + WhiteGap) - WhiteGap * 0.5f - BlackW * 0.5f;
                    UiKit.At(box.rectTransform, x, keyBottom + WhiteH - BlackH, BlackW, BlackH);
                    FreeKeyText[key] = UiKit.MakeText(box.rectTransform, "Key", hud, 14f, UiKit.TextLight, TextAlignmentOptions.Center);
                    UiKit.At(FreeKeyText[key].rectTransform, 0f, 4f, BlackW, 22f);
                    FreeNoteText[key] = null;
                }
                else
                {
                    UiKit.At(box.rectTransform, FreePad + white * (WhiteW + WhiteGap), keyBottom, WhiteW, WhiteH);
                    FreeKeyText[key] = UiKit.MakeText(box.rectTransform, "Key", hud, 17f, InkDark, TextAlignmentOptions.Center);
                    UiKit.At(FreeKeyText[key].rectTransform, 0f, 6f, WhiteW, 24f);
                    FreeNoteText[key] = UiKit.MakeText(box.rectTransform, "Note", hud, 11f, InkDim, TextAlignmentOptions.Center);
                    UiKit.At(FreeNoteText[key].rectTransform, 0f, 30f, WhiteW, 18f);
                }
                FreeBoxes[key] = box;
                FreePitch[key] = -1;
                FreeLit[key] = false;
            }
        }
    }

    // Me hide the HUD when Performance stopped feeding it (its tick failed before reaching me).
    private sealed class HudWatchdog : MonoBehaviour
    {
        private void Update()
        {
            try
            {
                if (Shown && Time.unscaledTime - _lastFeed > FeedTimeout)
                {
                    Hide();
                }
            }
            catch (Exception e)
            {
                PatchGuard.Report("MiniGameHud watchdog", e);
            }
        }
    }

#if DEBUG
    // ---------------------------------------------------------------- self-test helpers

    internal static bool MiniShown => _miniShown && _root != null && _root.activeSelf;

    internal static bool AutoShown => _autoShown && _root != null && _root.activeSelf;

    internal static bool FreeShown => _freeShown && _root != null && _root.activeSelf;

    internal static string FreeTitle => _freeTitle != null ? _freeTitle.text : null;

    // Lit key boxes now (free play).
    internal static int FreeLitCount
    {
        get
        {
            var n = 0;
            foreach (var lit in FreeLit)
            {
                if (lit)
                {
                    n++;
                }
            }
            return n;
        }
    }

    internal static string AutoText => _autoLine != null ? _autoLine.text : null;

    internal static string JudgementText => _judgement != null ? _judgement.text : null;

    // Short text dump: canvas, views, texts, note field size.
    internal static string DescribeLayout()
    {
        if (_root == null)
        {
            return "Mini-game HUD: not built.";
        }
        var sb = new System.Text.StringBuilder();
        sb.Append("Mini-game HUD: mini ").Append(_miniShown).Append(", autoplay ").Append(_autoShown).Append(", free ").Append(_freeShown)
            .Append(", screen ").Append(Screen.width).Append('x').Append(Screen.height)
            .Append(", game ").Append(_game != null ? _game.Title : "none")
            .Append(", visible notes ").Append(_game != null ? _game.Visible.Count : 0).Append('\n');
        if (_field != null)
        {
            var corners = new Vector3[4];
            _field.rectTransform.GetWorldCorners(corners);
            sb.Append("Note field on screen: (").Append(corners[0].x.ToString("F0")).Append(',').Append(corners[0].y.ToString("F0"))
                .Append(")-(").Append(corners[2].x.ToString("F0")).Append(',').Append(corners[2].y.ToString("F0")).Append(")\n");
        }
        // Our boxes next to the vanilla HUD parts they must not cover (screen pixels, bottom-left origin).
        UiKit.DescribeScreenBox(sb, "Mini-game (all texts) on screen", _mini != null ? _mini.transform : null);
        UiKit.DescribeScreenBox(sb, "Autoplay panel on screen", _auto != null ? _auto.transform : null);
        UiKit.DescribeScreenBox(sb, "Vanilla minimap", Minimap.instance != null && Minimap.instance.m_smallRoot != null
            ? Minimap.instance.m_smallRoot.transform : null);
        UiKit.DescribeScreenBox(sb, "Vanilla combat key hints", KeyHints.instance != null && KeyHints.instance.m_combatHints != null
            ? KeyHints.instance.m_combatHints.transform : null);
        UiKit.DescribeScreenBox(sb, "Vanilla stamina bar", Hud.instance != null ? Hud.instance.m_staminaBar2Root : null);
        UiKit.DescribeScreenBox(sb, "Vanilla eitr bar", Hud.instance != null ? Hud.instance.m_eitrBarRoot : null);
        UiKit.Describe(sb, _root.transform, 0, 2);
        return sb.ToString();
    }
#endif
}
