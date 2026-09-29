using System;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MC.UX.CraftingSearchSortMod;

// Me live on the search row and do all per-frame work: keep input guards up while field has keyboard, F key,
// gamepad B out of field, menu close on Esc/B/outside click, debounced refresh, scroll to top after a refresh.
// Inventory hidden = drop focus if any, then out (one bool check). No allocation per frame.
internal sealed class SearchController : MonoBehaviour
{
    private static readonly int VisibleParam = Animator.StringToHash("visible");

    private bool _wasFocused;
    private bool _focusKeyPressed;

    // Frame me first saw inventory shown (animator flag up); -1 while hidden. Focus key on that frame = the press that
    // opened it (Inventory rebound to F: vanilla reset "Inventory" button before Show, GetButtonDown no see it). Me skip it.
    private int _shownSinceFrame = -1;

    private void Update()
    {
        try
        {
            Tick();
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(SearchController)}.{nameof(Update)}", e);
        }
    }

    private void Tick()
    {
        var frame = Time.frameCount;

        // Animator flag, not IsVisible(): Hide drop flag at once, IsVisible() stay true one more frame. Reopen can come
        // 2 frames after close, so me might never see IsVisible() false in between; flag is false on frame after Hide.
        var gui = InventoryGui.instance;
        if (gui == null || gui.m_animator == null || !gui.m_animator.GetBool(VisibleParam))
        {
            _shownSinceFrame = -1;
        }
        else if (_shownSinceFrame < 0)
        {
            _shownSinceFrame = frame;
        }

        if (!InventoryGui.IsVisible())
        {
            if (_wasFocused)
            {
                // Focus slipped past Hide postfix: release it (navigation back), block keys this frame too.
                _wasFocused = false;
                FocusGuard.FieldUntilFrame = frame + 1;
                SearchUi.DropFocus();
            }
            _focusKeyPressed = false;
            if (SearchUi.MenuOpen)
            {
                SearchUi.CloseMenu();
            }
            return;
        }

        var field = SearchUi.Field;
        var focused = field != null && field.isFocused;
        if (focused)
        {
            // Gamepad cursor user: B leave field like chat does. Guard below keep this B away from InventoryGui.
            if (ZInput.GetButtonDown("JoyButtonB"))
            {
                SearchUi.DropFocus();
                focused = false;
            }
            FocusGuard.FieldUntilFrame = frame + 1;
        }
        else if (_wasFocused)
        {
            // Focus just ended (Esc, Enter, click elsewhere): guard one more frame, deselect, apply now.
            FocusGuard.FieldUntilFrame = frame + 1;
            SearchUi.ReleaseSelection();
            CraftSearch.RequestApplyNow();
        }
        _wasFocused = focused;
        SearchUi.FieldFocused = focused;

        if (!focused)
        {
            if (SearchUi.ActivateFieldAtFrame >= 0 && frame >= SearchUi.ActivateFieldAtFrame)
            {
                SearchUi.ActivateFieldAtFrame = -1;
                SearchUi.FocusField();
            }
            else if (_shownSinceFrame >= 0 && frame > _shownSinceFrame && CraftSearch.FocusKeyDown())
            {
                // Activate in LateUpdate, so this F is not typed into the field.
                _focusKeyPressed = true;
            }
        }

        if (SearchUi.MenuOpen)
        {
            // HasFocus postfix use it only on Esc/B frames: that key close menu, not inventory.
            FocusGuard.MenuUntilFrame = frame + 1;
            if (ZInput.GetKeyDown(KeyCode.Escape, logWarning: false) || ZInput.GetButtonDown("JoyButtonB"))
            {
                SearchUi.CloseMenu();
            }
            else if ((ZInput.GetKeyDown(KeyCode.Mouse0, logWarning: false) || ZInput.GetKeyDown(KeyCode.Mouse1, logWarning: false))
                     && !SearchUi.PointerOverMenuOrButton())
            {
                SearchUi.CloseMenu(); // click still go through to what is under it
            }
        }

        CraftSearch.TickRefresh();
        if (CraftSearch.ScrollTopRequested)
        {
            CraftSearch.ScrollTopRequested = false;
            SearchUi.ScrollToTop();
        }
    }

    private void LateUpdate()
    {
        if (!_focusKeyPressed)
        {
            return;
        }
        _focusKeyPressed = false;
        try
        {
            if (CanFocusNow())
            {
                SearchUi.FocusField();
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(SearchController)}.{nameof(LateUpdate)}", e);
        }
    }

    // Re-checked at end of frame: the same key press may have closed the inventory (Use/Inventory rebound to F),
    // or some other text field / console / dialog has the keyboard. "Inventory" check here only help gamepad or other
    // paths: vanilla reset that button before Show, so the press that OPEN inventory is skipped by _shownSinceFrame.
    private static bool CanFocusNow()
    {
        var gui = InventoryGui.instance;
        if (gui == null || gui.m_animator == null || !gui.m_animator.GetBool(VisibleParam))
        {
            return false; // IsVisible() stay true for the frame of a Hide; animator flag drop at once
        }
        if (ZInput.GetButtonDown("Use") || ZInput.GetButtonDown("Inventory")
            || ZInput.GetButtonDown("JoyButtonY") || ZInput.GetButtonDown("JoyButtonB"))
        {
            return false;
        }
        if (Console.IsVisible() || Menu.IsVisible() || GUIUtility.keyboardControl != 0)
        {
            return false; // GUIUtility: IMGUI text field (ConfigurationManager) has keyboard
        }
        var es = EventSystem.current;
        var selected = es != null ? es.currentSelectedGameObject : null;
        if (selected != null)
        {
            var other = selected.GetComponent<TMP_InputField>();
            if (other != null && other.isFocused)
            {
                return false;
            }
        }
        if (Chat.instance != null && Chat.instance.HasFocus())
        {
            return false; // other mods' fields that OR into HasFocus too
        }
        if ((gui.m_splitDialog != null && gui.m_splitDialog.IsActive)
            || (gui.m_variantDialog != null && gui.m_variantDialog.gameObject.activeSelf)
            || gui.IsSkillsPanelOpen || gui.IsTextPanelOpen || gui.IsTrophisPanelOpen || gui.IsAchievementsPanelOpen)
        {
            return false;
        }
        var field = SearchUi.Field;
        return field != null && field.isActiveAndEnabled && field.interactable && !field.isFocused;
    }

    // Row (or a parent) turned off or destroyed: never leave keyboard or EventSystem navigation locked.
    private void OnDisable()
    {
        try
        {
            if (_wasFocused)
            {
                FocusGuard.FieldUntilFrame = Time.frameCount + 1;
            }
            _wasFocused = false;
            _focusKeyPressed = false;
            _shownSinceFrame = -1;
            SearchUi.DropFocus();
            SearchUi.CloseMenu();
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(SearchController)}.{nameof(OnDisable)}", e);
        }
    }
}

// Right-click on search field = clear it. Left press = UI group fix when the crafting group block clicks.
internal sealed class SearchFieldPointer : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
{
    public void OnPointerDown(PointerEventData eventData)
    {
        try
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                SearchUi.OnControlPointerDown(isField: true);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(SearchFieldPointer)}.{nameof(OnPointerDown)}", e);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        try
        {
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                CraftSearch.ClearSearchNow();
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(SearchFieldPointer)}.{nameof(OnPointerClick)}", e);
        }
    }
}

// Right-click on Sort button = back to Default. Left click is the Button's own onClick (toggle menu).
internal sealed class SortButtonPointer : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
{
    public void OnPointerDown(PointerEventData eventData)
    {
        try
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                SearchUi.OnControlPointerDown(isField: false);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(SortButtonPointer)}.{nameof(OnPointerDown)}", e);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        try
        {
            if (eventData.button != PointerEventData.InputButton.Right)
            {
                return;
            }
            if (CraftSearch.Option == RecipeCategory.Default)
            {
                SearchUi.CloseMenu();
                return;
            }
            CraftSearch.ChooseOption(RecipeCategory.Default);
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(SortButtonPointer)}.{nameof(OnPointerClick)}", e);
        }
    }
}
