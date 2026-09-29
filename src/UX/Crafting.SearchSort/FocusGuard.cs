namespace MC.UX.CraftingSearchSortMod;

// Me = two frame numbers the input guards read (Chat.HasFocus postfix, bind prefix, UIGamePad prefix).
// Controller set them to "this frame + 1" every frame the thing hold, and on the frame it end. The +1 cover both
// script orders against EventSystem and InventoryGui.Update: the Esc that make field lose focus is still blocked
// for InventoryGui in same frame, so first Esc leave field and only next one close inventory.
internal static class FocusGuard
{
    // Our search field has keyboard. All three guards read it.
    internal static int FieldUntilFrame = -1;

    // Sort menu open. Only HasFocus postfix read it, and only together with Esc / B key test.
    internal static int MenuUntilFrame = -1;

    internal static void Reset()
    {
        FieldUntilFrame = -1;
        MenuUntilFrame = -1;
    }
}
