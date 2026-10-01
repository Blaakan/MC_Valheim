namespace MC.Exploration.SwimmingDiveMod;

// Me = the two dive keys, read held (not pressed) straight from ZInput by the vanilla names, so rebinds and every
// gamepad layout work: "Crouch" / "JoyCrouch" = down, "Jump" / "JoyJump" = up. Vanilla Crouch is a toggle that
// Player.UpdateCrouch cancel every tick while swimming, so the player state can never tell "held".
// Gate like vanilla movement input (PlayerController.FixedUpdate): Player.TakeInput (chat, console, menus, map,
// inventory, dead, teleporting), no radial menu, no inventory; JoyJump also not while the build menu is open.
// Debug: self test set TestDown / TestUp (null = real keys) so a test can drive the dive without a keyboard.
internal static class DiveInput
{
#if DEBUG
    internal static bool? TestDown;
    internal static bool? TestUp;
#endif

    internal static void Read(Player player, out bool down, out bool up)
    {
#if DEBUG
        if (TestDown.HasValue || TestUp.HasValue)
        {
            down = TestDown ?? false;
            up = TestUp ?? false;
            return;
        }
#endif
        down = false;
        up = false;
        if (!player.TakeInput() || Hud.InRadial() || InventoryGui.IsVisible())
        {
            return;
        }
        down = ZInput.GetButton("Crouch") || ZInput.GetButton("JoyCrouch");
        up = ZInput.GetButton("Jump") || (ZInput.GetButton("JoyJump") && !Hud.IsPieceSelectionVisible());
    }

#if DEBUG
    internal static void ClearTest()
    {
        TestDown = null;
        TestUp = null;
    }
#endif
}
