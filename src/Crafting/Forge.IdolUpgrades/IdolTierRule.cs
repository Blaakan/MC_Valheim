namespace MC.Crafting.ForgeIdolUpgradesMod;

// Me say which idol a refinement at the Forge of Potential need. Vanilla: every Forge recipe ask for one idol, the same
// at every level, so a Meadows axe climb forever on wooden idols. Me: while the item level is at most BaseLevels the
// recipe's own idol (its base tier); after that one tier higher every LevelsPerTier levels, up to Bloodgold. Same
// family (battle idol stay battle idol). Rules from ServerRules.Current: same for everybody on a server.
internal static class IdolTierRule
{
    // Tiers above the base tier for an item now at `level` (1 = fresh item).
    internal static int Offset(ForgeRules rules, int level)
    {
        if (rules == null || !rules.TierByLevel || rules.LevelsPerTier < 1 || level <= rules.BaseLevels)
        {
            return 0;
        }
        return 1 + (level - rules.BaseLevels - 1) / rules.LevelsPerTier;
    }

    // Idol tier an item of this base tier at this level need (Bloodgold at most).
    internal static int TierFor(ForgeRules rules, int baseTier, int level)
    {
        var tier = baseTier + Offset(rules, level);
        return tier < IdolTierDefaults.Count ? tier : IdolTierDefaults.Count - 1;
    }

    // First item level that need `offset` tiers above base (offset >= 1).
    internal static int FirstLevel(ForgeRules rules, int offset) =>
        rules.BaseLevels + 1 + (offset - 1) * rules.LevelsPerTier;

    // Idol the requirement really ask for when refining to targetQuality. Null = the requirement's own idol stay (not
    // an idol requirement, a modded idol, base level, or the higher idol missing in this game).
    internal static IdolCatalog.Idol Effective(Piece.Requirement req, int targetQuality)
    {
        if (req == null || !req.m_upgraderResource || req.m_resItem == null)
        {
            return null;
        }
        // Another Forge mod spend idols its own way: raising the check only would let a higher idol refine for free.
        if (ForgeGuard.ForeignTakeover)
        {
            return null;
        }
        var own = IdolCatalog.IdolOfPrefab(req.m_resItem);
        if (own == null)
        {
            return null;
        }
        var tier = TierFor(ServerRules.Current, own.Tier, targetQuality - 1);
        return tier == own.Tier ? null : IdolCatalog.IdolAt(tier, own.Weapon);
    }

    internal static string TierTitle(int tier) =>
        tier >= 0 && tier < IdolTierDefaults.Count ? IdolTierDefaults.All[tier].Title : "?";
}
