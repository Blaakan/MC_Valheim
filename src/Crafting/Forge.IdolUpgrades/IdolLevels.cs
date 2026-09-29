using UnityEngine;

namespace MC.Crafting.ForgeIdolUpgradesMod;

// Idol level = stars = vanilla item quality - 1 (quality 1 plain, 2/3/4 = 1/2/3 stars). Quality is saved everywhere
// (inventory, chest, drop), so the level need no custom data and survive uninstall.
internal static class IdolLevels
{
    internal const int Max = 3;

    internal static int Of(ItemDrop.ItemData item) => item == null ? 0 : Mathf.Clamp(item.m_quality - 1, 0, Max);

    internal static int Quality(int level) => Mathf.Clamp(level, 0, Max) + 1;

    // Chance (0..1) of a Forge refinement with an idol of this level.
    internal static float Chance(int level) => ServerRules.Current.Chance[Mathf.Clamp(level, 0, Max)] / 100f;

    internal static int ChancePercent(int level) => ServerRules.Current.Chance[Mathf.Clamp(level, 0, Max)];
}

// Which idol level the Forge spends when you carry several.
internal enum IdolPick
{
    Highest,
    Lowest,
}
