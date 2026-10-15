#if DEBUG
using System.Collections;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Crafting.ForgeIdolUpgradesMod;

// Debug build only. Me = self tests of the refinement rules Valheim 1.0.20 brought (0.2.0), one small test per item:
//   forge.refine.seeded       T30: refinement roll = the game's own seeded roll for that item and target level
//                             (Player.GetItemUpgradeChance): same value again with the seeds put back as saved (= a
//                             reload of the character), the seed moves on after the refinement, outcome follows roll
//                             and chance. Seeds of the character put back after.
//   forge.refine.station-tag  T31: Forge with the tag of an older game (bool, stored 1) mark nothing; Forge tagged by
//                             1.0.20 (value 2) mark the refined item cheated, like vanilla
internal static partial class SelfTests
{
    private const string SeededName = "forge.refine.seeded";
    private const string StationTagName = "forge.refine.station-tag";

    // Seeds of the character back as they were (same dictionary object: the game keep it).
    private static void PutSeedsBack(Player player, Dictionary<int, int> saved)
    {
        player.m_itemUpgradeSeeds.Clear();
        foreach (var kv in saved)
        {
            player.m_itemUpgradeSeeds[kv.Key] = kv.Value;
        }
    }

    // ---------- forge.refine.seeded (T30) ----------

    private static IEnumerator RunSeeded()
    {
        var rig = new Rig(SeededName);
        var c = rig.C;
        Dictionary<int, int> saved = null;
        try
        {
            ServerRules.TestRules = DefaultRules();
            if (!StoneAxeReady(rig))
            {
                c.Report();
                yield break;
            }
            var player = rig.P;
            saved = new Dictionary<int, int>(player.m_itemUpgradeSeeds);
            var axe = rig.Give("AxeStone", 1, 3);
            rig.Give(WoodIdol, 3, 1);
            c.Check(axe != null, "a level 3 Stone axe is given");
            if (axe == null)
            {
                c.Report();
                yield break;
            }

            // What the game's own roll gives for this item and target level (vanilla InventoryGui.DoCrafting: item
            // prefab name hash + target level), then the seeds put back as saved: a reload of the character.
            var hash = "AxeStone".GetStableHashCode() + 4;
            var expected = player.GetItemUpgradeChance(hash);
            PutSeedsBack(player, saved);
            var again = player.GetItemUpgradeChance(hash);
            PutSeedsBack(player, saved);
            c.Check(Mathf.Approximately(expected, again),
                $"the game's own roll for this item and level comes again when the seeds are as saved ({expected:0.0000}, then {again:0.0000}): a reload gives the same result");

            ForgeRefine.TestRoll = null;
            ForgeRefine.LastGameRoll = null;
            yield return rig.OpenForge();
            yield return rig.Select(axe);
            c.Check(rig.Row >= 0 && rig.ButtonOn, "the level 3 Stone axe is listed and can be refined");
            var chance = IdolLevels.Chance(0);
            yield return rig.Press();
            var got = ForgeRefine.LastGameRoll;
            c.Check(got.HasValue && Mathf.Approximately(got.Value, expected),
                $"the refinement used the game's own roll for this item and level ({(got.HasValue ? got.Value.ToString("0.0000") : "no roll from the game")}, the game's {expected:0.0000})");
            var moved = player.m_itemUpgradeSeeds.TryGetValue(hash, out var next) && (!saved.TryGetValue(hash, out var before) || next != before);
            c.Check(moved, "the character's seed for this item and level moved on, as after a refinement in the normal game (it is saved with the character)");
            var success = chance >= expected;
            var level = rig.Inv.ContainsItem(axe) ? axe.m_quality : 0;
            c.Check(success ? level == 4 : level == 2,
                $"the result follows the roll: {(success ? "success, level 4" : "failure, down to level 2")} with a {chance * 100f:0}% chance and a roll of {expected * 100f:0.0} (level now {level})");
            c.Report();
        }
        finally
        {
            // The probe's character keep no seed this test made.
            if (saved != null && Player.m_localPlayer != null)
            {
                PutSeedsBack(Player.m_localPlayer, saved);
            }
            rig.Dispose();
        }
    }

    // ---------- forge.refine.station-tag (T31) ----------

    private static IEnumerator RunStationTag()
    {
        var rig = new Rig(StationTagName);
        var c = rig.C;
        try
        {
            ServerRules.TestRules = DefaultRules();
            if (!StoneAxeReady(rig))
            {
                c.Report();
                yield break;
            }
            var axe = rig.Give("AxeStone", 1, 3);
            rig.Give(WoodIdol, 3, 1);
            c.Check(axe != null && !axe.m_cheated, "an untagged level 3 Stone axe is given");
            if (axe == null || axe.m_cheated)
            {
                c.Report();
                yield break;
            }
            yield return rig.OpenForge();
            var station = rig.Forge.Station;
            var view = station != null ? station.GetComponent<ZNetView>() : null;
            var zdo = view != null ? view.GetZDO() : null;
            c.Check(zdo != null && !PlayerProfile.s_bypassCheatChecks, "the Forge has its network object and this character does not ignore tags");
            if (zdo == null || PlayerProfile.s_bypassCheatChecks)
            {
                c.Report();
                yield break;
            }

            // Tag of a game before 1.0.20: a bool, stored as 1. The game no longer counts it.
            zdo.Set(ZDOVars.s_cheated, true);
            c.Check(!zdo.IsCheated() && !ForgeRefine.StationCheated(station),
                $"a Forge with the tag of an older game (value {zdo.GetInt(ZDOVars.s_cheated)}) is not tagged, for the game ({zdo.IsCheated()}) and for the mod");
            yield return rig.Select(axe);
            ForgeRefine.TestRoll = Win;
            yield return rig.Press();
            c.Check(axe.m_quality == 4 && !axe.m_cheated, $"refined at that Forge: level 4, not marked cheated (level {axe.m_quality}, marked {axe.m_cheated})");

            // Tag of 1.0.20: value 2.
            zdo.SetCheated();
            c.Check(zdo.IsCheated() && ForgeRefine.StationCheated(station),
                $"a Forge tagged by today's game (value {zdo.GetInt(ZDOVars.s_cheated)}) is tagged, for the game and for the mod");
            yield return rig.Select(axe);
            ForgeRefine.TestRoll = Win;
            yield return rig.Press();
            c.Check(axe.m_quality == 5 && axe.m_cheated,
                $"refined at that Forge: level 5 and marked cheated, as the game marks what is made at a tagged station (level {axe.m_quality}, marked {axe.m_cheated})");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }
}
#endif
