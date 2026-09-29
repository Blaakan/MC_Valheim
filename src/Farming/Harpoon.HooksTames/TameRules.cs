namespace MC.Farming.HarpoonHooksTamesMod;

// Me decide which creature = tame, and which tame may catch a harpoon vanilla let fly past.
internal static class TameRules
{
    // Why me let harpoon fly past. Const text: no new string on this path.
    private const string NotTame = "it is not a tame";
    private const string Ridden = "a player is riding it";

    // Tamed, not a player. Summons (skeleton, troll) count too: they are tamed Character with Tameable.
    // Unity null check (==), never ?.  IsTamed read ZDO at most once a second on games that not own it.
    internal static bool IsTame(Character character)
    {
        return character != null && !character.IsPlayer() && character.IsTamed();
    }

    // Extra targets me add (vanilla said no). False = fly past, refusal say why (for Debug log).
    // Rider on it = fly past (vanilla: no acting on other player without PvP, and hook pull the rider too).
    // Every other tame = hook, whatever its AI do (fight, chase prey, run, stare at wild thing behind fence):
    // player most want pull back busy tame. Price: tame standing in harpoon path catch it (hooked, no hurt), enemy behind
    // get nothing. Vanilla with PvP on stop harpoon at tame too.
    internal static bool CanHook(Character character, out string refusal)
    {
        if (!IsTame(character))
        {
            refusal = NotTame;
            return false;
        }

        var tameable = character.GetComponent<Tameable>();
        if (tameable != null && tameable.HaveRider())
        {
            refusal = Ridden;
            return false;
        }

        refusal = null;
        return true;
    }
}
