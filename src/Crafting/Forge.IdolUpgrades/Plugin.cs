using System;
using BepInEx.Configuration;
using MC.Shared;

namespace MC.Crafting.ForgeIdolUpgradesMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated.
// Me give Forge of Potential idols 3 star levels (idol quality 1..4), an Idols tab at the Forge to upgrade them with
// biome metal + trophies, and new refinement odds: idol level set the chance, a failure cost levels (or the item).
// Both side: server refuse players without me (PlayerCheck) and send its rules to everyone (ServerRules). Game code
// read rules only through ServerRules.Current (server's when client of a server with me, else own config).
internal sealed partial class Plugin : ModPlugin
{
    internal static readonly ConfigEntry<int>[] Chance = new ConfigEntry<int>[IdolLevels.Max + 1];
    internal static readonly ConfigEntry<int>[] MaterialCost = new ConfigEntry<int>[IdolLevels.Max + 1];
    internal static readonly ConfigEntry<int>[] TrophyCost = new ConfigEntry<int>[IdolLevels.Max + 1];
    internal static ConfigEntry<FailureMode> Failure;
    internal static ConfigEntry<int> LevelsLost;
    internal static ConfigEntry<bool> TierByLevel;
    internal static ConfigEntry<int> BaseLevels;
    internal static ConfigEntry<int> LevelsPerTier;
    internal static ConfigEntry<IdolPick> Pick;
    internal static ConfigEntry<bool> AllowPlayersWithoutMod;
    internal static readonly TierConfig[] Tiers = new TierConfig[IdolTierDefaults.Count];

    private const string ServerWins = " In multiplayer the setting of the server (or host) is used for everyone.";

    internal sealed class TierConfig
    {
        internal ConfigEntry<string> Material;
        internal ConfigEntry<string> Base;
        internal ConfigEntry<string> Elite;
        internal ConfigEntry<string> Boss;
    }

    protected override void BindConfig()
    {
        IconInlineGuard.Apply();

        AllowPlayersWithoutMod = Config.Bind("General", "AllowPlayersWithoutMod", false, new ConfigDescription(
            "Used only by the server (or the host). Off (default): a player whose game does not have this mod, has a "
            + "version that cannot talk to this one, or has it turned off, is refused about a second after joining or "
            + "after turning it off (their game shows \"Incompatible version\"), so every player refines with the same "
            + "rules. On: such players may play; their Forge works the normal game way (no idol levels, the recipe's "
            + "own idol at every level) and does not accept starred idols on their own.",
            null, new ConfigurationManagerAttributes { Order = 90 }));

        int[] chances = { 35, 55, 75, 95 };
        string[] levelText = { "no star (a plain idol)", "1 star", "2 stars", "3 stars" };
        for (var level = 0; level <= IdolLevels.Max; level++)
        {
            Chance[level] = Config.Bind("Refinement", "ChanceLevel" + level, chances[level], new ConfigDescription(
                $"Chance in percent that a refinement at the Forge of Potential succeeds when it uses an idol with {levelText[level]}."
                + ServerWins,
                new AcceptableValueRange<int>(0, 100),
                new ConfigurationManagerAttributes { Order = 100 - level }));
        }
        Failure = Config.Bind("Refinement", "OnFailure", FailureMode.LoseLevels, new ConfigDescription(
            "What a failed refinement does to the item (the idol is always used up). LoseLevels (default): the item "
            + "loses LevelsLost levels, never going below level 1. Destroy: the item is destroyed and part of its "
            + "materials come back, like in the normal game." + ServerWins,
            null, new ConfigurationManagerAttributes { Order = 92 }));
        LevelsLost = Config.Bind("Refinement", "LevelsLost", 1, new ConfigDescription(
            "With OnFailure = LoseLevels: how many levels the item loses when a refinement fails (it never goes below "
            + "level 1)." + ServerWins,
            new AcceptableValueRange<int>(1, ForgeRules.MaxLevelsLost),
            new ConfigurationManagerAttributes { Order = 91 }));
        TierByLevel = Config.Bind("Refinement", "HigherIdolAtHighLevels", true, new ConfigDescription(
            "On (default): past a certain level, refining an item needs an idol of a higher tier than the one the game "
            + "asks for, so an early item cannot climb forever on cheap idols (see LevelsOnOwnIdol and "
            + "LevelsPerIdolTier). The idol stays of the same kind as the one the game asks for (battle or protection). "
            + "Off: every level uses the idol the game asks for." + ServerWins,
            null, new ConfigurationManagerAttributes { Order = 89 }));
        BaseLevels = Config.Bind("Refinement", "LevelsOnOwnIdol", 5, new ConfigDescription(
            "With HigherIdolAtHighLevels on: an item is refined with the idol the game asks for (its own tier) while its "
            + "level is at most this value. Default 5: an item fully upgraded at the workbench (level 4) can take two "
            + "refinements with its own idol, up to level 6." + ServerWins,
            new AcceptableValueRange<int>(1, ForgeRules.MaxLevel),
            new ConfigurationManagerAttributes { Order = 88 }));
        LevelsPerTier = Config.Bind("Refinement", "LevelsPerIdolTier", 4, new ConfigDescription(
            "With HigherIdolAtHighLevels on: after LevelsOnOwnIdol, the idol needed goes one tier up every this many "
            + "levels, up to Bloodgold. Default 4: an item whose own idol is Wooden (e.g. the Stone axe or the Club) needs "
            + "Bronze idols from level 6, Iron from level 10, Silver from level 14, and so on. An item whose own idol is "
            + "Bronze (e.g. the Flint axe) starts one tier higher." + ServerWins,
            new AcceptableValueRange<int>(1, ForgeRules.MaxLevel),
            new ConfigurationManagerAttributes { Order = 87 }));
        Pick = Config.Bind("Refinement", "IdolChoice", IdolPick.Highest, new ConfigDescription(
            "Which idol the Forge uses when you carry the needed idol at several levels. Highest = the one with the most "
            + "stars (best chance); Lowest = the plain one first (keep your starred idols). You can still pick another "
            + "level by clicking the idol under the requirements (mouse). Each player's own choice.",
            null, new ConfigurationManagerAttributes { Order = 90 }));

        int[] materials = { 0, 5, 10, 15 };
        int[] trophies = { 0, 5, 3, 1 };
        string[] trophyClass = { "", "common", "elite", "boss" };
        for (var level = 1; level <= IdolLevels.Max; level++)
        {
            MaterialCost[level] = Config.Bind("Upgrade costs", "Level" + level + "Material", materials[level], new ConfigDescription(
                $"Metal (or wood for wooden idols) needed to upgrade an idol to {levelText[level]}, on top of the idol itself."
                + ServerWins,
                new AcceptableValueRange<int>(0, ForgeRules.MaxCost),
                new ConfigurationManagerAttributes { Order = 100 - level * 2 }));
            TrophyCost[level] = Config.Bind("Upgrade costs", "Level" + level + "Trophies", trophies[level], new ConfigDescription(
                $"Trophies needed to upgrade an idol to {levelText[level]}: any mix of the {trophyClass[level]} trophies of the idol's tier."
                + ServerWins,
                new AcceptableValueRange<int>(0, ForgeRules.MaxCost),
                new ConfigurationManagerAttributes { Order = 99 - level * 2 }));
        }

        foreach (var d in IdolTierDefaults.All)
        {
            var section = $"Tier {d.Tier} - {d.Title} idols";
            Tiers[d.Tier] = new TierConfig
            {
                Material = Config.Bind(section, "Material", d.Material, new ConfigDescription(
                    $"Item used as metal to upgrade {d.Title} idols (prefab name, as used by the spawn command)." + ServerWins,
                    null, new ConfigurationManagerAttributes { Order = 100 })),
                Base = Config.Bind(section, "CommonTrophies", d.Base, new ConfigDescription(
                    $"Trophies that upgrade {d.Title} idols to 1 star: comma-separated prefab names." + ServerWins,
                    null, new ConfigurationManagerAttributes { Order = 90 })),
                Elite = Config.Bind(section, "EliteTrophies", d.Elite, new ConfigDescription(
                    $"Trophies that upgrade {d.Title} idols from 1 to 2 stars: comma-separated prefab names." + ServerWins,
                    null, new ConfigurationManagerAttributes { Order = 80 })),
                Boss = Config.Bind(section, "BossTrophies", d.Boss, new ConfigDescription(
                    $"Trophies (or boss drops) that upgrade {d.Title} idols from 2 to 3 stars: comma-separated prefab names."
                    + ServerWins,
                    null, new ConfigurationManagerAttributes { Order = 70 })),
            };
        }

        // Every rule setting (all sections but General, IdolChoice aside: it is personal): new own snapshot, server
        // send it again.
        Config.SettingChanged += (_, e) =>
        {
            var setting = e.ChangedSetting;
            if (setting != null && setting.Definition.Section != "General" && !ReferenceEquals(setting, Pick))
            {
                ServerRules.OwnChanged();
            }
        };
        AllowPlayersWithoutMod.SettingChanged += OnAllowChanged;
    }

    protected override void OnActivated()
    {
        IdolCatalog.RaiseIdolLevels();
        ForgeGuard.Reset();
        ServerRules.Start();
        PlayerCheck.Start();
        SelfTests.Register();
    }

    protected override void OnDeactivated()
    {
        SelfTests.Unregister();
        PlayerCheck.Stop();
        ServerRules.Stop();
        try
        {
            IdolsTab.BeforeOff();
        }
        catch (Exception e)
        {
            Log.Warning($"Could not reset the Forge window while turning off: {e.Message}");
        }
        IdolsTab.Restore();
        IdolChoice.Clear();
        ForgePanel.Forget();
        if (InventoryGui.instance != null)
        {
            ForgeUi.RemoveClickable(InventoryGui.instance);
        }
        // Star sprites stay alive (StarIcons.Clear comment): a popup still queued may show one later.
    }

    // Switched back to refuse: players without me already in get checked (after the grace) too.
    private static void OnAllowChanged(object sender, EventArgs e)
    {
        try
        {
            if (!AllowPlayersWithoutMod.Value)
            {
                PlayerCheck.ScheduleAllConnected();
            }
        }
        catch (Exception ex)
        {
            PatchGuard.Report("Plugin.OnAllowChanged", ex);
        }
    }
}
