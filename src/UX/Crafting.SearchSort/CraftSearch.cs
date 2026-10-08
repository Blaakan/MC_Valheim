using System;
using System.Linq;
using BepInEx.Configuration;
using MC.Shared;
using UnityEngine;

namespace MC.UX.CraftingSearchSortMod;

// Me = mod state: on/off, search term, chosen sort, station we are at, pending refresh.
// A search or sort change just call vanilla UpdateCraftingPanel again; the UpdateRecipeList postfix (CraftList)
// do the filter + sort, vanilla keep or pick selection as usual. Me never touch Player.s_FilterCraft or the
// "sortcraft" key: console commands keep working and combine with us.
internal static class CraftSearch
{
    // Wait this long after last keystroke before rebuild (each rebuild re-create every row, like a tab switch).
    private const float Debounce = 0.1f;

    internal static bool Active;

    // Normalised search term ("" = no filter). Stored at each keystroke: any list build use the latest one.
    internal static string Term = "";

    // Field show some text (maybe only spaces). Hide postfix use it to know if clearing is needed.
    private static bool _hasText;

    internal static int Option = RecipeCategory.Default;

    internal static bool Pending;
    internal static bool ApplyNowRequested;
    internal static bool ScrollTopRequested;
    private static float _lastEdit;

    // Who the current sort and search belong to.
    private static Player _player;
    private static string _stationKey;
    internal static string StationKey => _stationKey ?? "none";

    // Focus key, read once per setting change.
    private static KeyCode _focusMain = KeyCode.None;
    private static KeyCode[] _focusMods = new KeyCode[0];

    private static bool _languageHooked;

#if DEBUG
    // Self test read only: focus key as me hold it now ("None" = off), modifiers after '+'.
    internal static string DebugFocusKey =>
        _focusMods.Length == 0 ? _focusMain.ToString() : _focusMain + "+" + string.Join("+", _focusMods.Select(m => m.ToString()).ToArray());
#endif

    internal static void ReadFocusKey(KeyboardShortcut shortcut)
    {
        _focusMain = shortcut.MainKey;
        _focusMods = shortcut.Modifiers?.ToArray() ?? new KeyCode[0];
        if (_focusMain == KeyCode.None)
        {
            return;
        }
        // ConfigurationManager (or a hand edit) can store any key: check once here, not every frame.
        var bad = IsUsableKey(_focusMain) ? _focusMods.FirstOrDefault(m => !IsUsableKey(m)) : _focusMain;
        if (bad != KeyCode.None)
        {
            FocusKeyUnusable(shortcut.ToString(), bad);
        }
    }

    // Focus key went down this frame (with its modifiers held).
    internal static bool FocusKeyDown()
    {
        if (_focusMain == KeyCode.None)
        {
            return false;
        }
#if DEBUG
        // Self test stand in for the keyboard: "focus key went down" on one frame. After the "no key set" check on
        // purpose: empty or unreadable key stay off for the test too.
        if (SelfTests.FocusKeyFrame == Time.frameCount)
        {
            return true;
        }
#endif
        try
        {
            if (!ZInput.GetKeyDown(_focusMain, logWarning: false))
            {
                return false;
            }
            for (var i = 0; i < _focusMods.Length; i++)
            {
                if (!ZInput.GetKey(_focusMods[i], logWarning: false))
                {
                    return false;
                }
            }
        }
        catch (ArgumentException)
        {
            // Key the check missed (Keyboard.current[Key.None] throw ArgumentOutOfRangeException): key off, once.
            FocusKeyUnusable(Plugin.FocusSearchKey.Value.ToString(), KeyCode.None);
            return false;
        }
        return true;
    }

    // Me same check as Loot Pickup Filter: ZInput throw every frame on a keyboard key it no can map (F13, Hash, At...),
    // and Mouse5/6 never fire. Static map, work before ZInput exist.
    private static bool IsUsableKey(KeyCode k)
    {
        if (!ZInput.IsKeyCodeValid(k))
        {
            return false; // None, Mouse5, Mouse6, Joystick1Button0 and up
        }
        if (k >= KeyCode.Mouse0 && k <= KeyCode.Mouse4)
        {
            return true;
        }
        if (k >= KeyCode.JoystickButton0)
        {
            return true; // ZInput fall back to South button, no throw
        }
        return ZInput.TryKeyCodeToKey(k, out _);
    }

    // Game cannot read the key: focus key off (search field still work with the mouse). Say so once per setting.
    private static void FocusKeyUnusable(string shortcut, KeyCode bad)
    {
        _focusMain = KeyCode.None;
        _focusMods = new KeyCode[0];
        var which = bad != KeyCode.None ? $"the key {bad}" : "this key";
        Log.Warning($"General.FocusSearchKey = {shortcut}: the game cannot read {which}, so the key that jumps to the "
                    + "search field is off. Pick another key (for example a letter).");
    }

    internal static void Activate()
    {
        Active = true;
        if (!_languageHooked)
        {
            Localization.OnLanguageChange = (Action)Delegate.Combine(Localization.OnLanguageChange, new Action(OnLanguageChange));
            _languageHooked = true;
        }

        var gui = InventoryGui.instance;
        if (gui == null)
        {
            return; // normal at plugin start: postfix make the row on first list build
        }
        SearchUi.Ensure(gui);
        if (InventoryGui.IsVisible() && Player.m_localPlayer != null)
        {
            // Turned on with station open: remembered sort now.
            gui.UpdateCraftingPanel();
        }
    }

    internal static void Deactivate()
    {
        // First: postfix do nothing from here, even though patches still on during this call.
        Active = false;

        // Destroy row + menu, give layout back (only onto same live InventoryGui), release keyboard.
        // Guards reset after: row OnDisable would raise them again.
        SearchUi.Destroy();
        FocusGuard.Reset();

        if (_languageHooked)
        {
            Localization.OnLanguageChange = (Action)Delegate.Remove(Localization.OnLanguageChange, new Action(OnLanguageChange));
            _languageHooked = false;
        }

        RecipeTerms.Clear();
        RecipeCategory.ClearLabels();
        CraftList.Clear();
        Term = "";
        _hasText = false;
        Option = RecipeCategory.Default;
        Pending = false;
        ApplyNowRequested = false;
        ScrollTopRequested = false;
        _player = null;
        _stationKey = null;

        // Vanilla list again this frame.
        var gui = InventoryGui.instance;
        if (gui != null && InventoryGui.IsVisible() && Player.m_localPlayer != null)
        {
            gui.UpdateCraftingPanel();
        }
    }

    // New InventoryGui (logout/login): old objects died with scene. Forget who we were at.
    internal static void OnNewGui()
    {
        RecipeTerms.Clear();
        CraftList.Clear();
        _player = null;
        _stationKey = null;
        Term = "";
        _hasText = false;
        Pending = false;
        ApplyNowRequested = false;
        ScrollTopRequested = false;
    }

    // Postfix call me before filter: new station type or new player = its remembered sort, empty search, menu shut.
    // Done before filtering, so the build that see the change is already right.
    internal static void UpdateStation(Player player, CraftingStation station)
    {
        var key = SortMemory.StationKey(station);
        if (ReferenceEquals(player, _player) && key == _stationKey)
        {
            return;
        }
        _player = player;
        _stationKey = key;
        Option = Plugin.ReadRememberSort() ? SortMemory.Load(player, key) : RecipeCategory.Default;
        ClearText();
        SearchUi.CloseMenu();
    }

    // RememberSort changed. UpdateStation skip same station, so me redo its load here for station we at.
    // Search text stay (only sort change). Inventory open = list rebuilt now; closed = next open use new Option.
    internal static void OnRememberSortChanged()
    {
        try
        {
            if (!Active || _stationKey == null)
            {
                return; // no station yet: next list build load right sort anyway
            }
            var option = Plugin.ReadRememberSort() ? SortMemory.Load(_player, _stationKey) : RecipeCategory.Default;
            if (option == Option)
            {
                return;
            }
            Option = option;
            SearchUi.RefreshButtonLabel();
            if (InventoryGui.IsVisible())
            {
                Pending = true;
                ApplyNowRequested = true;
                ApplyNow(); // while crafting: stay Pending, TickRefresh retry
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(CraftSearch)}.{nameof(OnRememberSortChanged)}", e);
        }
    }

    // Field text changed (typing, Ctrl+Backspace, paste).
    internal static void OnTextChanged(string text)
    {
        try
        {
            Term = RecipeTerms.Normalise(text);
            _hasText = !string.IsNullOrEmpty(text);
            Pending = true;
            _lastEdit = Time.unscaledTime;
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(CraftSearch)}.{nameof(OnTextChanged)}", e);
        }
    }

    // Enter, focus lost, right-click clear: no debounce wait.
    internal static void RequestApplyNow()
    {
        if (Pending)
        {
            ApplyNowRequested = true;
        }
    }

    // Controller call me each frame while inventory shown.
    internal static void TickRefresh()
    {
        if (Pending && (ApplyNowRequested || Time.unscaledTime - _lastEdit >= Debounce))
        {
            ApplyNow();
        }
    }

    // Rebuild list with vanilla code. Not while crafting (the craft keep its own snapshot, but the progress bar
    // length follow the selection): still Pending, retried next frame, or done by vanilla rebuild at craft end.
    // One try per request: me take the request BEFORE rebuild, so if rebuild throw (me, other mod, vanilla) or other
    // mod skip it, me no try again every frame.
    internal static void ApplyNow()
    {
        var gui = InventoryGui.instance;
        if (gui == null || !InventoryGui.IsVisible() || gui.m_craftTimer >= 0f || Player.m_localPlayer == null)
        {
            return;
        }
        Pending = false;
        ApplyNowRequested = false;
        ScrollTopRequested = true; // controller read it after this call (or next frame if call throw): harmless
        try
        {
            gui.UpdateCraftingPanel(focusView: false); // postfix use latest Term and Option
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(CraftSearch)}.{nameof(ApplyNow)}", e);
        }
    }

    // Postfix call me after each list build: that build already used latest term and sort, whoever started it.
    internal static void OnListBuilt()
    {
        if (Pending)
        {
            Pending = false;
            ApplyNowRequested = false;
            ScrollTopRequested = true;
        }
    }

    // Menu entry clicked.
    internal static void ChooseOption(int option)
    {
        try
        {
            Option = option;
            if (Plugin.ReadRememberSort())
            {
                SortMemory.Save(Player.m_localPlayer, StationKey, option);
            }
            SearchUi.CloseMenu();
            SearchUi.RefreshButtonLabel();
            Pending = true;
            ApplyNowRequested = true;
            ApplyNow();
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(CraftSearch)}.{nameof(ChooseOption)}", e);
        }
    }

    // Right-click on field: empty search, full list at once.
    internal static void ClearSearchNow()
    {
        if (!_hasText && Term.Length == 0)
        {
            return;
        }
        ClearText();
        Pending = true;
        ApplyNowRequested = true;
        ApplyNow();
    }

    private static void ClearText()
    {
        Term = "";
        _hasText = false;
        SearchUi.SetFieldTextSilently("");
    }

    // Row rebuilt (field new and empty): term must match what player see.
    internal static void OnFieldCreated()
    {
        Term = "";
        _hasText = false;
    }

    // InventoryGui.Hide postfix. Vanilla call Hide every frame while dead/teleporting: idle path = few bool checks.
    internal static void OnInventoryHidden()
    {
        if (!Active)
        {
            return;
        }
        var clearText = _hasText && !Plugin.ReadKeepSearchText();
        var resetSort = Option != RecipeCategory.Default && !Plugin.ReadRememberSort();
        if (!SearchUi.MenuOpen && !SearchUi.FieldFocused && !Pending && !ScrollTopRequested && !ApplyNowRequested
            && !clearText && !resetSort)
        {
            return;
        }

        SearchUi.CloseMenu();
        SearchUi.DropFocus();
        Pending = false;
        ApplyNowRequested = false;
        ScrollTopRequested = false;
        if (clearText)
        {
            ClearText();
        }
        if (resetSort)
        {
            Option = RecipeCategory.Default;
            SearchUi.RefreshButtonLabel();
        }
    }

    private static void OnLanguageChange()
    {
        try
        {
            RecipeTerms.Clear();
            RecipeCategory.ClearLabels();
            // Language change: same option, new words. Force label rebuild (vanilla cache already new language here).
            SearchUi.RefreshButtonLabel(force: true);
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(CraftSearch)}.{nameof(OnLanguageChange)}", e);
        }
    }
}
