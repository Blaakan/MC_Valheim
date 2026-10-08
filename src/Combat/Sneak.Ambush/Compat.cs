using BepInEx.Bootstrap;
using MC.Shared;

namespace MC.Combat.SneakAmbushMod;

// Me = other mods me must know about (design 6.3). Two pay Sneak XP for sneak attacks too: our XP stand down on the
// attacker's game (unless rule PayAlongsideOtherSneakXpMods). Others change sneak numbers: they compose, me only say
// so in the log. GUIDs read in their sources (2026-09-29). Mods with unknown GUID (Goo's Combat Overhaul, SNEAKer,
// EliteCreaturesReborn...) not here: me cannot see them.
// Me look late (first use), not in OnActivated: first activation run inside our Awake, and BepInEx put a plugin in
// PluginInfos only when it load it (plugins after "MC." in load order not there yet). OnActivated reset me; next use
// look again, Info lines once per activation.
internal static class Compat
{
    internal const string SecondaryAttacksGuid = "sighsorry.SecondaryAttacks";
    internal const string SmartSkillsGuid = "org.bepinex.plugins.smartskills";
    internal const string SetUpSkillsGuid = "neocor.SetUpSkills";
    internal const string ImpactfulSkillsGuid = "MidnightsFX.ImpactfulSkills";
    internal const string SneakyVikingGuid = "Brutaliaa.SneakyViking";

    private static bool _detected;
    private static bool _secondaryAttacks;
    private static bool _smartSkills;

    // SecondaryAttacks: backstab XP, own UpdateStealth postfix (calls SEMan.ModifyStealth again), knife aggro reset.
    internal static bool SecondaryAttacks
    {
        get
        {
            Ensure();
            return _secondaryAttacks;
        }
    }

    // SmartSkills: Sneak XP per hit on unaware enemy.
    internal static bool SmartSkills
    {
        get
        {
            Ensure();
            return _smartSkills;
        }
    }

#if DEBUG
    // Self test play "SecondaryAttacks or SmartSkills installed" (stand-down of our XP) without them, or "none
    // installed" on a game that has one. Null = real look. Memory only, never in release.
    internal static bool? TestOtherModPays { get; set; }
#endif

    // Some other mod on this game pay sneak-attack XP.
    internal static bool OtherModPaysSneakXp
    {
        get
        {
#if DEBUG
            if (TestOtherModPays.HasValue)
            {
                return TestOtherModPays.Value;
            }
#endif
            Ensure();
            return _secondaryAttacks || _smartSkills;
        }
    }

    // Our XP handler stand down on this game? (other mod pays, and rules no say pay both)
    internal static bool StandDownXp(AmbushRules rules) =>
        OtherModPaysSneakXp && (rules == null || !rules.PayAlongsideOtherSneakXpMods);

    // OnActivated: look again at next use.
    internal static void Reset()
    {
        _detected = false;
        _secondaryAttacks = false;
        _smartSkills = false;
    }

    // First use in a session (XP event, stealth refresh, self test). Cheap after first time: one bool.
    internal static void Ensure()
    {
        if (!_detected)
        {
            Detect();
        }
    }

    private static void Detect()
    {
        _detected = true;
        _secondaryAttacks = Loaded(SecondaryAttacksGuid);
        _smartSkills = Loaded(SmartSkillsGuid);
        if (_secondaryAttacks)
        {
            Log.Info("SecondaryAttacks is installed: it pays Sneak XP for sneak attacks, so " + ModInfo.Name
                     + " pays none on this game unless PayAlongsideOtherSneakXpMods is on. Its sneak visibility change "
                     + "stacks with " + ModInfo.Name + "'s stealth bonuses; set its Sneak Visibility Skill Effect Factor "
                     + "to 1 to keep " + ModInfo.Name + "'s early-game curve.");
        }
        if (_smartSkills)
        {
            Log.Info("SmartSkills is installed: it pays Sneak XP for hits on unaware enemies, so " + ModInfo.Name
                     + " pays no sneak-attack XP on this game unless PayAlongsideOtherSneakXpMods is on.");
        }
        LogComposes(SetUpSkillsGuid, "SetUpSkills");
        LogComposes(ImpactfulSkillsGuid, "ImpactfulSkills");
        LogComposes(SneakyVikingGuid, "Sneaky Viking");
    }

    private static void LogComposes(string guid, string name)
    {
        if (Loaded(guid))
        {
            Log.Info($"{name} is installed: it changes sneaking numbers (skill curve, noise, crouch speed or XP). Sneak "
                     + "Ambush's stealth bonuses apply on top of them.");
        }
    }

    private static bool Loaded(string guid) => Chainloader.PluginInfos.ContainsKey(guid);
}
