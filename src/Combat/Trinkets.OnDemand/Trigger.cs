using MC.Shared;

namespace MC.Combat.TrinketsOnDemandMod;

// Me = one frame of the local player (Player.Update postfix): feedback, then the trigger press (G5).
// Gates like a vanilla gameplay key: TakeInput (menu, chat, console, inventory, map, dead, cutscene, teleport), radial
// menu and build mode (alt + RS is a build input). No animation: works mid-swing and mid-roll, as the vanilla pop does.
// Input read first every frame (a Debug test press is consumed even while pending).
internal static class Trigger
{
    internal static void Tick(Player player)
    {
        Compat.Ensure();
        var pressed = Controls.Pressed();
        var rules = ServerRules.Current;
        if (rules.IsPending)
        {
            Feedback.Reset();
            if (pressed)
            {
                SetLast(TriggerResult.Pending);
            }
            return;
        }
        Feedback.Tick(player);
        if (!pressed)
        {
            return;
        }
        if (!player.TakeInput() || Hud.InRadial() || player.InPlaceMode() || player.IsDead())
        {
            SetLast(TriggerResult.Blocked);
            return;
        }
        var padPress = Controls.PadPressed();
        var result = FullBar.Fire(player, rules);
        SetLast(result);
        if (result == TriggerResult.Fired)
        {
            Log.Debug("Trinket triggered by the player: bar emptied, effects added or refreshed.");
            if (padPress)
            {
                Controls.SwallowPadButton();
            }
            return;
        }
        if (result == TriggerResult.Failed)
        {
            Log.Debug("Trinket trigger pressed with a full bar, but the bar did not empty (another mod may have "
                      + "skipped the normal game's adrenaline code).");
            return;
        }
        Feedback.Answer(player, result);
        if (padPress)
        {
            Controls.SwallowPadButton();
        }
    }

    private static void SetLast(TriggerResult result)
    {
#if DEBUG
        FullBar.LastResult = result;
#endif
    }
}
