#if DEBUG
using System;
using UnityEngine;

namespace MC.Shared;

// Me = names of stand-in third-party content. tests/Probes/Core.Probe.Content put these clones in the game during
// test runs (Test-InWorld, Test-Multiplayer), each the way a real content library does it (ItemManager, Jotunn, late
// server sync). A mod's self test check it pick them up like the vanilla thing they copy (docs/modding/framework.md,
// "Third-party content"). Prefix MCProbeTP_ = "third party". Debug only: shipped builds never see this.
internal static class SelfTestContent
{
    internal const string Prefix = "MCProbeTP_";

    // Shields. Tower data like Warfare's towers (parry bonus 0, movement -0.2); round = control, never a tower.
    internal const string TowerShield = "MCProbeTP_ShieldTower";            // ItemManager way, clone of ShieldIronTower
    internal const string LateEditTower = "MCProbeTP_ShieldLateEdit";       // Jotunn way: clone of ShieldBanded (round), made tower in late postfix
    internal const string AfterLoadTower = "MCProbeTP_ShieldAfterLoad";     // after world load, ShieldBanded clone made tower (block 32)
    internal const string RoundShield = "MCProbeTP_ShieldRound";            // ItemManager way, clone of ShieldBanded, unchanged

    // Weapons (ItemManager way).
    internal const string SwordPlain = "MCProbeTP_SwordBronze";             // one-handed sword, vanilla swing (clone of SwordBronze)
    internal const string SwordCustomSwing = "MCProbeTP_SwordCustomSwing";  // attack animation renamed CustomSwingName (KG-style, Warfare)
    internal const string ReloadGun = "MCProbeTP_ReloadGun";                // reload weapon on CustomSkill, not Crossbows (clone of CrossbowArbalest)
    internal const string CustomSkillAxe = "MCProbeTP_ThrowAxe";            // one-handed axe on CustomSkill (clone of AxeBronze)
    internal const string CustomSwingName = "MCProbeTP_Swing0";

    // Skill the way blaxxun / Jotunn SkillManager make one: |hash of the name| as SkillType (not a defined enum value).
    internal const string CustomSkillName = "MCProbeTP_Guns";
    internal static Skills.SkillType CustomSkill => (Skills.SkillType)Math.Abs(CustomSkillName.GetStableHashCode());

    // Creature content: trophy item (ItemManager way), creature prefab in ZNetScene that drops it, and a spawn entry put
    // in every SpawnSystem the Jotunn CreatureManager way (SpawnSystem.Awake prefix, list on an inactive object):
    // Meadows, never spawns (needs global key NeverKey).
    internal const string Trophy = "MCProbeTP_Trophy";                      // clone of TrophyBoar
    internal const string Creature = "MCProbeTP_Creature";                  // clone of Boar, drops Trophy
    internal const string NeverKey = "MCProbeTP_NeverSet";

    // Piece, Jotunn PieceManager way (ObjectDB.Awake postfix priority 200): torch in the Hammer's table.
    internal const string Torch = "MCProbeTP_Torch";                        // clone of piece_groundtorch_wood

    // True when the stand-in content mod put its start-up items in this game's item database.
    internal static bool Loaded =>
        ObjectDB.instance != null && ObjectDB.instance.GetItemPrefab(TowerShield) != null;

    // After-load content is in (it comes a few seconds after the player spawned).
    internal static bool AfterLoadIn =>
        ObjectDB.instance != null && ObjectDB.instance.GetItemPrefab(AfterLoadTower) != null;

    internal static GameObject Item(string name) => ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(name) : null;
}
#endif
