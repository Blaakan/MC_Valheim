using UnityEngine;

namespace MC.Combat.TrinketsOnDemandMod;

// Me = what the player sees (added beyond the request, personal settings):
//   - bar becomes full (and an equipped item has an effect): Hud.AdrenalineBarFlash + top-left message naming the
//     trigger (ShowFullMessage; at most once per MessageCooldown s, fights with hits taken refill often);
//   - while it stays full: flash again every FullFlashInterval s (0 = once);
//   - press answers: "not full yet", "no trinket", "effect still active" (center, like $hud_powernotready);
//   - trinket tooltip line naming the trigger (ItemDataPatches).
// Vanilla only flashes the bar on a tier change (Player.AddAdrenaline); whether the animator shows the Flash trigger is
// prefab data (self test notes it).
internal static class Feedback
{
    internal const float MessageCooldown = 20f;
    internal const string FlashTrigger = "Flash"; // Hud.AdrenalineBarFlash

    private static Hud _checkedHud;
    private static bool _hudHasFlash;
    private static bool _wasFull;
    private static float _nextFlashAt;
    private static float _lastMessageAt = float.NegativeInfinity;

    internal const string NoTrinketText = "No trinket to trigger";
    internal const string NotFullText = "Adrenaline not full yet";
    internal const string StillActiveText = "Trinket effect still active";

#if DEBUG
    // Self test count flashes and full messages without a screen.
    internal static int FlashCount;
    internal static int FullMessageCount;

    // Self test: next full bar may show its message at once.
    internal static void ClearMessageCooldown() => _lastMessageAt = float.NegativeInfinity;
#endif

    internal static void Reset()
    {
        _wasFull = false;
        _nextFlashAt = 0f;
    }

    // Player.Update postfix (local, rules not pending). Not full = two float reads.
    internal static void Tick(Player player)
    {
        var full = FullBar.IsFull(player) && !player.IsDead() && FullBar.HasEquippedEffect(player);
        if (!full)
        {
            _wasFull = false;
            return;
        }
        var now = Time.time;
        var interval = Plugin.FullFlashInterval != null ? Plugin.FullFlashInterval.Value : 4f;
        if (!_wasFull)
        {
            _wasFull = true;
            Flash();
            _nextFlashAt = now + interval;
            if ((Plugin.ShowFullMessage == null || Plugin.ShowFullMessage.Value) && now - _lastMessageAt >= MessageCooldown)
            {
                _lastMessageAt = now;
                ShowFullMessage(player);
            }
            return;
        }
        if (interval > 0f && now >= _nextFlashAt)
        {
            _nextFlashAt = now + interval;
            Flash();
        }
    }

    private static void Flash()
    {
        var hud = Hud.instance;
        if (hud == null || !CanFlash(hud))
        {
            return;
        }
        hud.AdrenalineBarFlash();
#if DEBUG
        FlashCount++;
#endif
    }

    // Animator of the bar has a "Flash" trigger? Unity warn on every SetTrigger of a missing parameter: ask once per
    // Hud (parameters list alloc). Empty list (controller not ready) = ask again next time.
    internal static bool CanFlash(Hud hud)
    {
        if (ReferenceEquals(hud, _checkedHud))
        {
            return _hudHasFlash;
        }
        var animator = hud.m_adrenalineAnimator;
        if (animator == null)
        {
            return false;
        }
        var parameters = animator.parameters;
        if (parameters == null || parameters.Length == 0)
        {
            return false;
        }
        _checkedHud = hud;
        _hudHasFlash = false;
        foreach (var p in parameters)
        {
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == FlashTrigger)
            {
                _hudHasFlash = true;
                break;
            }
        }
        return _hudHasFlash;
    }

    private static void ShowFullMessage(Player player)
    {
        var label = Controls.Label();
        var text = label != null
            ? "Adrenaline full: press " + label + " to trigger your trinket"
            : "Adrenaline full: set a trigger key for " + ModInfo.Name + " to trigger your trinket";
        player.Message(MessageHud.MessageType.TopLeft, text);
#if DEBUG
        FullMessageCount++;
#endif
    }

    // Answer to a press that did not fire.
    internal static void Answer(Player player, TriggerResult result)
    {
        string text;
        switch (result)
        {
            case TriggerResult.NoTrinket: text = NoTrinketText; break;
            case TriggerResult.NotFull: text = NotFullText; break;
            case TriggerResult.StillActive: text = StillActiveText; break;
            default: return;
        }
        player.Message(MessageHud.MessageType.Center, text);
    }

    // Tooltip line for an item with a full-adrenaline effect.
    internal static string TooltipLine()
    {
        var label = Controls.Label();
        return label != null
            ? "\nTrigger: <color=orange>" + label + "</color> when the adrenaline bar is full"
            : "\nTrigger: no key set (" + ModInfo.Name + " settings)";
    }
}
