#if DEBUG
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MC.Shared;
using UnityEngine;
using ItemType = ItemDrop.ItemData.ItemType;

namespace MC.Combat.ShieldsTowerWallMod;

// tower.thirdparty (TESTING T50-T56): tower shields of other mods, design 2.0.2. Stand-in content
// (tests/Probes/Core.Probe.Content, names in src/Shared/SelfTestContent.cs), each the way a real library adds it:
//   MCProbeTP_ShieldTower      ItemManager way (Awake prefix), clone of ShieldIronTower, parry 0, block 52 -> bash 12
//   MCProbeTP_ShieldLateEdit   Jotunn way: round ShieldBanded clone made parry 0 in the ObjectDB.Awake postfix at
//                              priority 0; block 42 -> bash 10; its snapshot must hold the edited data (parry 0)
//   MCProbeTP_ShieldAfterLoad  put in ObjectDB a few seconds after spawn (server-synced editor way), ShieldBanded
//                              clone with tower data (parry 0, block 32) -> bash 8: a tower without restart
//   MCProbeTP_ShieldRound      round clone, parry 1.5: never a tower, never written (no snapshot)
// Plus: creature-only copies FW_/SP_ShieldBlackmetalTower and ShieldIronSquare stay normal, the stand-in tower takes
// both hands, ExcludedTowers wins, AutoTowers off = list only, listed without ":n" = scaled damage. Warfare installed:
// its three towers are towers (bash 8.4 / 10.2 / 24.44), its round shields and bucklers are not.
internal static partial class SelfTests
{
    private const string ThirdPartyName = "tower.thirdparty";
    private const float AfterLoadWait = 25f;

    private static readonly string[] WarfareTowers = { "ShieldBronzeTower_TW", "ShieldChitinTower_TW", "ShieldCarapaceTower_TW" };
    private static readonly float[] WarfareBash = { 8.4f, 10.2f, 24.44f };
    private static readonly string[] WarfareNotTowers =
        { "ShieldBronzeBanded_TW", "ShieldChitin_TW", "ShieldChitinBuckler_TW", "ShieldSilverBuckler_TW", "ShieldBlackmetalBuckler_TW" };

    private static void RegisterThirdParty() => SelfTest.Register(ThirdPartyName, RunThirdParty);

    private static void UnregisterThirdParty() => SelfTest.Unregister(ThirdPartyName);

    // Every tower of the default list is in the catalog (more may be: towers found automatically).
    private static bool DefaultTowersKnown()
    {
        var db = ObjectDB.instance;
        return db != null && TowerRules.Defaults().Entries.All(e => TowerCatalog.TowerOfPrefab(db.GetItemPrefab(e.Prefab)) != null);
    }

    private static IEnumerator RunThirdParty()
    {
        var c = new Checks(ThirdPartyName);
        if (!SelfTestContent.Loaded)
        {
            c.Check(false, "the stand-in content mod is not loaded (run through tools/Test-InWorld.ps1 or Test-Multiplayer.ps1)");
            c.Report();
            yield break;
        }
        var rig = new Rig(ThirdPartyName);
        try
        {
            var defaults = TowerRules.Defaults();
            yield return UseRules(defaults);
            yield return Until(() => SelfTestContent.AfterLoadIn && !TowerSync.HasWork, AfterLoadWait);
            var db = ObjectDB.instance;
            TowerInfo Info(string name) => TowerCatalog.TowerOfPrefab(db.GetItemPrefab(name));
            string Bash(TowerInfo i) => i == null ? "not a tower" : F(i.BashDamage);

            // Each way a library adds a shield (T50-T52).
            var tower = Info(SelfTestContent.TowerShield);
            c.Check(tower != null && tower.Automatic && Near(tower.BashDamage, 12f) && IsTower(SharedOf(SelfTestContent.TowerShield), tower.Snapshot, defaults),
                $"ItemManager-way stand-in (parry 0, block 52) is a tower found automatically, tower data written, bash 12 ({Bash(tower)})");
            var late = Info(SelfTestContent.LateEditTower);
            c.Check(late != null && late.Automatic && Near(late.Snapshot.TimedBlockBonus, 0f) && Near(late.BashDamage, 10f),
                $"Jotunn-way stand-in (made parry 0 in the late ObjectDB postfix) is a tower; its vanilla snapshot holds the edited data "
                + $"(parry {(late != null ? F(late.Snapshot.TimedBlockBonus) : "-")}), bash 10 ({Bash(late)})");
            var after = Info(SelfTestContent.AfterLoadTower);
            c.Check(SelfTestContent.AfterLoadIn && after != null && after.Automatic && Near(after.BashDamage, 8f),
                $"stand-in added after the world loaded (block 32) became a tower without a restart, bash 8 ({Bash(after)}, in the database {SelfTestContent.AfterLoadIn})");
            c.Check(Info(SelfTestContent.RoundShield) == null && TowerCatalog.SnapshotOfPrefab(db.GetItemPrefab(SelfTestContent.RoundShield)) == null,
                "the round stand-in (parry 1.5) is no tower and was never written");

            // Vanilla parity (T53): the automatic pass adds no vanilla shield.
            SelfTest.Note(ThirdPartyName, $"automatic towers: {string.Join(", ", TowerCatalog.Automatic.ToArray())}; left out as creature-only: "
                   + $"{string.Join(", ", TowerCatalog.CreatureOnly.ToArray())}");
            c.Check(TowerCatalog.CreatureOnly.Contains("FW_ShieldBlackmetalTower") && TowerCatalog.CreatureOnly.Contains("SP_ShieldBlackmetalTower")
                    && Info("FW_ShieldBlackmetalTower") == null && Info("SP_ShieldBlackmetalTower") == null,
                "the creature copies FW_ and SP_ShieldBlackmetalTower are left out as creature-only (no recipe, carried by a creature)");
            c.Check(Info("ShieldIronSquare") == null && Info("ShieldBanded") == null, "ShieldIronSquare (parry 1) and ShieldBanded stay normal");
            var vanillaAuto = TowerCatalog.Automatic.Where(n => !n.StartsWith(SelfTestContent.Prefix, System.StringComparison.Ordinal) && !n.EndsWith("_TW", System.StringComparison.Ordinal)).ToList();
            c.Check(vanillaAuto.Count == 0, $"no vanilla shield is found automatically beyond the default list ({string.Join(", ", vanillaAuto.ToArray())})");

            // The stand-in tower in the hands (T54).
            var p = rig.P;
            var sword = rig.Give("SwordIron");
            var item = rig.Give(SelfTestContent.TowerShield);
            p.EquipItem(sword);
            p.EquipItem(item);
            yield return Frames(3);
            c.Check(item != null && item.m_shared.m_itemType == ItemType.TwoHandedWeaponLeft && Only(p, item, left: true) && !sword.m_equipped,
                $"the stand-in tower takes both hands, the sword is put away ({Hands(p)})");
            rig.EmptyHands();

            // Settings (T55): ExcludedTowers wins, AutoTowers off, listed without a number.
            var excluded = defaults.With(r => r.ExcludedTowers = SelfTestContent.TowerShield);
            yield return UseRules(excluded);
            var snap = TowerCatalog.SnapshotOfPrefab(db.GetItemPrefab(SelfTestContent.TowerShield));
            c.Check(Info(SelfTestContent.TowerShield) == null && IsVanilla(SharedOf(SelfTestContent.TowerShield), snap)
                    && item.m_shared.m_itemType == ItemType.Shield,
                "ExcludedTowers: the stand-in tower is a normal shield again (prefab and the copy in the inventory)");
            yield return UseRules(defaults.With(r => r.AutoTowers = false));
            c.Check(Info(SelfTestContent.TowerShield) == null && Info(SelfTestContent.LateEditTower) == null && Info(SelfTestContent.AfterLoadTower) == null
                    && DefaultTowersKnown() && TowerCatalog.Count == defaults.Entries.Count && TowerCatalog.Automatic.Count == 0,
                $"AutoTowers off: only the listed towers ({TowerCatalog.Count})");
            yield return UseRules(defaults.With(r =>
            {
                r.AutoTowers = false;
                r.Towers = TowerRules.DefaultTowers + ", " + SelfTestContent.TowerShield + ", " + SelfTestContent.AfterLoadTower + ":5";
            }));
            var listed = Info(SelfTestContent.TowerShield);
            var fixedOne = Info(SelfTestContent.AfterLoadTower);
            c.Check(listed != null && !listed.Automatic && Near(listed.BashDamage, 12f) && fixedOne != null && Near(fixedOne.BashDamage, 5f),
                $"listed without a number: bash from block armor ({Bash(listed)}); listed with ':5': 5 ({Bash(fixedOne)})");
            yield return UseRules(defaults);

            // Real content mod (T56): Warfare's towers when Warfare is installed.
            if (db.GetItemPrefab(WarfareTowers[0]) == null)
            {
                SelfTest.Note(ThirdPartyName, "Warfare is not installed: its real-tower checks did not run");
            }
            else
            {
                var got = new List<string>();
                var ok = true;
                for (var i = 0; i < WarfareTowers.Length; i++)
                {
                    var w = Info(WarfareTowers[i]);
                    got.Add($"{WarfareTowers[i]} {Bash(w)}");
                    ok &= w != null && w.Automatic && Near(w.BashDamage, WarfareBash[i], 0.05f);
                }
                c.Check(ok, $"Warfare's tower shields are towers found automatically with bash 8.4 / 10.2 / 24.44 ({string.Join(", ", got.ToArray())})");
                var wrong = WarfareNotTowers.Where(n => db.GetItemPrefab(n) != null && Info(n) != null).ToList();
                c.Check(wrong.Count == 0, $"Warfare's round shields and bucklers stay normal ({string.Join(", ", wrong.ToArray())})");
            }
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }
}
#endif
