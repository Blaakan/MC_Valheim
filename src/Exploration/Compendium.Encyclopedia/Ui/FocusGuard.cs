namespace MC.Exploration.CompendiumEncyclopediaMod;

// Me = the frame number the input guards read (Chat.HasFocus postfix, console bind prefix, UIGamePad prefix), same
// idea as Crafting Search and Sort's guard but our own counter. WindowController set it to "this frame + 1" every
// frame our search field hold the keyboard, and on the frame it let go. The +1 cover both script orders against
// InventoryGui.Update: the Esc that make the field let go is still hidden from InventoryGui in that frame, so the
// first Esc only leave the field.
internal static class FocusGuard
{
    internal static int FieldUntilFrame = -1;

    internal static bool Active => UnityEngine.Time.frameCount <= FieldUntilFrame;

    internal static void Reset() => FieldUntilFrame = -1;
}
