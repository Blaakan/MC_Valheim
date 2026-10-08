using System.Collections.Generic;

namespace MC.Crafting.ForgeIdolUpgradesMod;

// Me pick which idol level a Forge refinement spend. Default from config (most stars, or plain first). Click on the
// idol under the requirements = next level you hold; that pick last until the inventory window close.
internal static class IdolChoice
{
    private static readonly Dictionary<string, int> Picked = new Dictionary<string, int>();

#if DEBUG
    // Self test force the IdolChoice setting (never the config file). Null = normal.
    internal static IdolPick? TestPick;
#endif

    // The player's IdolChoice setting. Only place that read it.
    private static IdolPick Setting
    {
        get
        {
#if DEBUG
            if (TestPick.HasValue)
            {
                return TestPick.Value;
            }
#endif
            return Plugin.Pick.Value;
        }
    }

    internal static void Clear() => Picked.Clear();

    // Level (0..3) to spend for `amount` idols of this name, or -1 when no level has enough.
    internal static int LevelToSpend(Inventory inventory, string idolName, int amount)
    {
        if (Picked.TryGetValue(idolName, out var picked) && Has(inventory, idolName, picked, amount))
        {
            return picked;
        }
        return Default(inventory, idolName, amount);
    }

    internal static int Default(Inventory inventory, string idolName, int amount)
    {
        if (Setting == IdolPick.Lowest)
        {
            for (var level = 0; level <= IdolLevels.Max; level++)
            {
                if (Has(inventory, idolName, level, amount))
                {
                    return level;
                }
            }
            return -1;
        }
        for (var level = IdolLevels.Max; level >= 0; level--)
        {
            if (Has(inventory, idolName, level, amount))
            {
                return level;
            }
        }
        return -1;
    }

    // Next level you hold enough of, after the current one (wrap around). False = nothing else to pick.
    internal static bool Cycle(Inventory inventory, string idolName, int amount)
    {
        var current = LevelToSpend(inventory, idolName, amount);
        for (var step = 1; step <= IdolLevels.Max + 1; step++)
        {
            var level = (current + step + IdolLevels.Max + 1) % (IdolLevels.Max + 1);
            if (level != current && Has(inventory, idolName, level, amount))
            {
                Picked[idolName] = level;
                return true;
            }
        }
        return false;
    }

    // How many different levels of this idol you hold enough of (click hint only when more than one).
    internal static int LevelsHeld(Inventory inventory, string idolName, int amount)
    {
        var n = 0;
        for (var level = 0; level <= IdolLevels.Max; level++)
        {
            if (Has(inventory, idolName, level, amount))
            {
                n++;
            }
        }
        return n;
    }

    internal static bool Has(Inventory inventory, string idolName, int level, int amount) =>
        level >= 0 && level <= IdolLevels.Max && inventory.CountItems(idolName, IdolLevels.Quality(level)) >= amount;
}
