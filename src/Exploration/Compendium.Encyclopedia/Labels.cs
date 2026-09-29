namespace MC.Exploration.CompendiumEncyclopediaMod;

/// <summary>
/// Me hold every English word the mod show (user-facing text stay normal English). Vanilla tokens (start with $) where
/// game have one; Localize turn them into game language, plain English pass through same.
/// UI take title, tooltip, search and status words from here too, so one place change them.
/// </summary>
internal static class Labels
{
    // ---------------------------------------------------------------- window and button (UI side use these)

    /// <summary>Window title. Display name = "Encyclopedia" (user choice: the game's dialog is "Valheim Compendium").</summary>
    internal const string WindowTitle = "Encyclopedia";

    /// <summary>Opt-in side button tooltip topic.</summary>
    internal const string ButtonTooltipTopic = "Encyclopedia";

    /// <summary>
    /// Top-level tab of the game's own content (message log, active effects, lore texts, Player Statistics). No
    /// vanilla token fits: $inventory_texts reads "Valheim Compendium" (the dialog title), $inventory_logs "Message log"
    /// (one of its entries).
    /// </summary>
    internal const string TabTexts = "Texts";

    /// <summary>Top-level tab of our window.</summary>
    internal const string TabEncyclopedia = "Encyclopedia";

    /// <summary>Button tooltip text.</summary>
    internal const string ButtonTooltipText =
        "Every item, building piece and creature you have discovered, with recipes, drops and where to find them.";

    /// <summary>List row while catalog build.</summary>
    internal const string Preparing = "Preparing entries...";

    /// <summary>Search field placeholder.</summary>
    internal const string SearchPlaceholder = "Search...";

    /// <summary>Header counter: {0} discovered, {1} listed.</summary>
    internal const string DiscoveredFormat = "Discovered {0} / {1}";

    /// <summary>Name and mark of anything not discovered. Vanilla secret achievements use same "???".</summary>
    internal const string Unknown = "???";

    /// <summary>Text of the "?" mark UI draw in icon slot of undiscovered thing.</summary>
    internal const string UnknownMark = "?";

    // ---------------------------------------------------------------- tabs

    internal const string TabWeapons = "Weapons";
    internal const string TabArmor = "Armor";
    internal const string TabTools = "Tools and misc";
    internal const string TabFood = "Food and potions";
    internal const string TabMaterials = "$hud_materials";
    internal const string TabTrophies = "$inventory_trophies";
    internal const string TabBuilding = "$hud_building";
    internal const string TabCreatures = "Creatures";

    // ---------------------------------------------------------------- sub-groups

    internal const string GroupOtherWeapons = "Other weapons";
    internal const string GroupAmmo = "Ammo";
    internal const string GroupShields = "Shields";
    internal const string GroupHelmets = "Helmets";
    internal const string GroupChest = "Chest armor";
    internal const string GroupLegs = "Leg armor";
    internal const string GroupHands = "Hand armor";
    internal const string GroupCapes = "Capes";
    internal const string GroupUtility = "Utility items";
    internal const string GroupTrinkets = "Trinkets";
    internal const string GroupBuildTools = "Building tools";
    internal const string GroupGatherTools = "Gathering tools";
    internal const string GroupTorches = "Torches";
    internal const string GroupMisc = "Misc";
    internal const string GroupOther = "Other";
    internal const string GroupFood = "$hud_food";
    internal const string GroupPotions = "Meads and potions";
    internal const string GroupMaterials = "$hud_materials";
    internal const string GroupFish = "Fish";
    internal const string GroupHabitatUnknown = "Habitat unknown";

    // ---------------------------------------------------------------- details

    internal const string Seasonal = "Seasonal";
    internal const string Boss = "Boss";
    internal const string NotDiscovered = "Not discovered yet.";
    internal const string HintItem = "Hold one, or learn a recipe that makes it.";
    internal const string HintPiece = "Learn to build it: carry the right tool and its materials.";
    internal const string HintCreature = "Aim at one up close to see its name, kill one or tame one.";
    internal const string DetailsUnavailable = "Details unavailable (see the log).";

    internal const string Crafting = "$hud_crafting";
    internal const string At = "At ";
    internal const string LevelFormat = " (level {0})";
    internal const string ByHand = "By hand";
    internal const string MakesFormat = "Makes {0}";
    internal const string AnyOneOf = "Any one of:";
    internal const string AtUpgradeStation = "At an upgrade station:";
    internal const string BeyondMaxFormat = "Beyond quality {0}: at an upgrade station only";
    internal const string UpgradeOnly = "Cannot be crafted, only upgraded.";
    internal const string Upgrades = "Upgrades";
    internal const string QualityFormat = "Quality {0}";
    internal const string WhereToGet = "Where to get it";
    internal const string DroppedBy = "Dropped by:";
    internal const string Picked = "Picked";
    internal const string Mined = "Mined";
    internal const string Chopped = "Chopped from trees";
    internal const string Broken = "Found by breaking objects";
    internal const string In = " in ";
    internal const string AtStation = " at ";
    internal const string StationLevelFormat = " level {0})";
    internal const string ProducedByBuilding = "Produced by a building";
    internal const string MadeFrom = "Made from ";
    internal const string ProducedBy = "Produced by ";
    internal const string LaidBy = "Laid by a tamed ";
    internal const string FoundInChests = "Found in chests";
    internal const string CaughtIn = "Caught in ";
    internal const string Caught = "Caught by fishing";
    internal const string SoldByTrader = "Sold by a trader";
    internal const string NoSource = "Not found in the world data: it may come from a trader, a location or a dungeon.";
    internal const string UsedIn = "Used in";
    internal const string TamesPrefix = "Tames ";
    internal const string YourRecords = "Your records";
    internal const string PickedUpFormat = "Picked up {0}";
    internal const string CraftedFormat = "Crafted {0}";
    internal const string EatenFormat = "Eaten {0}";
    internal const string PlacedFormat = "Placed {0}";
    internal const string PlaceableWith = "Can be placed with the ";
    internal const string PlaceableSelf = "Can be placed like a building piece.";

    internal const string BuiltWith = "Built with ";
    internal const string ComfortFormat = "Comfort {0}";
    internal const string BuildCost = "Build cost";
    internal const string NeedsA = "Needs a ";
    internal const string Nearby = " nearby";
    internal const string HighestLevelFormat = "Highest level you have seen: {0}";
    internal const string StationUpgrades = "Upgrades";
    internal const string UpgradeFor = "Upgrade for the ";
    internal const string RecipesHere = "Recipes at this station";
    internal const string Conversions = "Conversions";
    internal const string Turns = "Turns ";
    internal const string Into = " into ";
    internal const string Produces = "Produces ";

    internal const string KilledFormat = "Killed: {0}";
    internal const string TamedFormat = "Tamed: {0}";
    internal const string Habitat = "Habitat";
    internal const string HabitatUnknown = "Habitat unknown";
    internal const string AppearsAfter = "Appears after defeating ";
    internal const string Drops = "Drops";
    internal const string Taming = "Taming";
    internal const string CanBeTamed = "Can be tamed. Eats:";
    internal const string CanWear = "Can wear: ";
    internal const string AfterFirstKill = "After your first kill";
    internal const string HealthFormat = "Health {0}";
    internal const string DamageModifiers = "$inventory_dmgmod";

    internal const string OnePerPlayer = "1 per player";
    internal const string MoreWithStars = ", more with stars";
    internal const string CollapsedFormat = "??? ×{0} not discovered yet";
    internal const string TimesFormat = " ×{0}";
    internal const string Separator = " · ";
    internal const string ListSeparator = ", ";
}
