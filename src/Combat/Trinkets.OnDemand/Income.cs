using MC.Shared;

namespace MC.Combat.TrinketsOnDemandMod;

// Me = passive income while fighting (G2), on top of every vanilla source, and the no-drain rule (G3).
// Income: Player.UpdateStats(float) postfix (owner, not dead, 50 Hz) add dt; once per TickSeconds, while in a fight,
// with capacity (max > 0) and bar below max: AddAdrenaline(IncomePerSecond x TickSeconds). Through the vanilla funnel,
// so world rate (Game.m_adrenalineRate), gain curve, status effect modifiers and the full-bar hold all apply, same as
// any vanilla source. One call per second keeps full-bar and tier scans rare.
// No drain: UpdateStats(float) prefix raise private m_adrenalineDegenTimer to at least 1 s while max > 0, before
// vanilla count it down (-dt, 0.02 s): it never reach 0, the drain never run. Event losses (melee miss, unblocked hit)
// are separate AddAdrenaline calls: vanilla. Floor 1 s (not huge): after a toggle off, vanilla drain back within 1 s.
internal static class Income
{
    internal const float TickSeconds = 1f;
    internal const float DegenFloor = 1f;

    private static float _accumulator;
    private static bool _wasFighting;

    internal static void Reset()
    {
        _accumulator = 0f;
        _wasFighting = false;
    }

    // Prefix of UpdateStats(float): keep the drain timer up while a trinket gives capacity. Max read here is one tick
    // old (UpdateModifiers run inside UpdateStats): harmless.
    internal static void HoldDrain(Player player)
    {
        if (player.m_adrenalineDegenTimer < DegenFloor && player.GetMaxAdrenaline() > 0f)
        {
            player.m_adrenalineDegenTimer = DegenFloor;
        }
    }

    // Postfix of UpdateStats(float) (max fresh now). Bar above max after a swap to a cheaper trinket: set it to max
    // (field write, no pop), so the HUD number tells the truth. Then the income tick.
    internal static void Tick(Player player, float dt, TrinketRules rules)
    {
        var max = player.GetMaxAdrenaline();
        if (max > 0f && player.m_adrenaline > max)
        {
            player.m_adrenaline = max;
        }
        _accumulator += dt;
        if (_accumulator < TickSeconds)
        {
            return;
        }
        // Long frame (loading, hitch): pay one tick, never a burst.
        _accumulator = _accumulator >= 2f * TickSeconds ? 0f : _accumulator - TickSeconds;
        var fighting = CombatState.InCombat(rules.CombatLingerSeconds);
        if (fighting != _wasFighting)
        {
            // Once per fight start and end (testers follow the income with it).
            _wasFighting = fighting;
            Log.Debug(fighting ? "In a fight: adrenaline income on." : "Fight over: adrenaline income off.");
        }
        if (!fighting || rules.IncomePerSecond <= 0f || max <= 0f || player.m_adrenaline >= max)
        {
            return;
        }
        player.AddAdrenaline(rules.IncomePerSecond * TickSeconds);
    }
}
