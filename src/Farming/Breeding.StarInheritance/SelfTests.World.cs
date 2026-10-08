#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MC.Farming.BreedingStarInheritanceMod.Patches;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Farming.BreedingStarInheritanceMod;

// Debug build only. More in-world self tests (single player, world probe). Each test = own pen, own clean-up:
//   breeding.lines        what every birth decided + exact Debug line: lower parent from both parents (recorded
//                         partner), forced roll both ways at default numbers, extra star, cap (also MaxStars 1), level
//                         5 never lowered, partner moved away or destroyed after conception
//   breeding.ticks        the game's own repeating breeding tick (not my direct calls): births from both parents at
//                         chance 0, then default numbers: never the higher parent level; parent level never left swapped
//   breeding.farming      real Farming skill of the character (reset / raised, put back after): no Farming entry added
//                         by any kind of tick, publish after a raise, chance line 0 / 50 / 100, pen 80 m away, own
//                         settings (also with numbers of an older server session in memory)
//   breeding.hatch        hen egg quality 2 -> hatch (game code) -> chick level 2 -> grow (game code) -> hen level 2
//   breeding.asksvin      same chain for Asksvin (prefab chain read from the prefabs, NOTE line)
//   breeding.scope        piglet grow into tamed boar of same level; untamed boars untouched; mod patch only its five
//                         game methods (nothing that set levels)
//   breeding.toggle       feature off and on live (LocalBlocker switch): withdraw, patches gone, vanilla births, back
//                         on: publish, found at birth, recorded again, no partner at birth
//   breeding.respawn      real respawn: Farming published on the new character at once (breeding ticks held)
//   breeding.eggstack     egg and egg[2] picked up = two stacks; slot number, tooltip line, drag merge (NOTE)
//   breeding.stars        star marks of the creature HUD follow piglet level (screenshot)
//   breeding.farmer-range other player's published Farming: range, withdrawn, best wins, tie; birth line name them
//                         (stand-in player object, one frame, never simulated)
//   breeding.compat       stand aside for Star Level System (switch) and for a mod that skip the game's breeding code
//   breeding.setup        breeding values of Boar and Hen prefabs = what the TESTING.md Setup tells the tester
//   breeding.log          no warning or error line from this mod in the whole session (run last)
internal static partial class SelfTests
{
    private const string LinesName = "breeding.lines";
    private const string TicksName = "breeding.ticks";
    private const string FarmingName = "breeding.farming";
    private const string HatchName = "breeding.hatch";
    private const string AsksvinName = "breeding.asksvin";
    private const string ScopeName = "breeding.scope";
    private const string ToggleName = "breeding.toggle";
    private const string RespawnName = "breeding.respawn";
    private const string EggStackName = "breeding.eggstack";
    private const string StarsName = "breeding.stars";
    private const string FarmerRangeName = "breeding.farmer-range";
    private const string CompatName = "breeding.compat";
    private const string SetupName = "breeding.setup";
    private const string LogName = "breeding.log";

    private static void RegisterWorld()
    {
        SelfTest.Register(LinesName, RunLines);
        SelfTest.Register(TicksName, RunTicks);
        SelfTest.Register(FarmingName, RunFarming);
        SelfTest.Register(HatchName, () => RunChain(HatchName, "Hen"));
        SelfTest.Register(AsksvinName, () => RunChain(AsksvinName, "Asksvin"));
        SelfTest.Register(ScopeName, RunScope);
        SelfTest.Register(ToggleName, RunToggle);
        SelfTest.Register(RespawnName, RunRespawn);
        SelfTest.Register(EggStackName, RunEggStack);
        SelfTest.Register(StarsName, RunStars);
        SelfTest.Register(FarmerRangeName, RunFarmerRange);
        SelfTest.Register(CompatName, RunCompat);
        SelfTest.Register(SetupName, RunSetup);
    }

    // Me last of all single-player tests of this mod (it look back at the whole session).
    private static void RegisterWorldLast()
    {
        SelfTest.Register(LogName, RunLog);
    }

    private static void UnregisterWorld()
    {
        SelfTest.Unregister(LinesName);
        SelfTest.Unregister(TicksName);
        SelfTest.Unregister(FarmingName);
        SelfTest.Unregister(HatchName);
        SelfTest.Unregister(AsksvinName);
        SelfTest.Unregister(ScopeName);
        SelfTest.Unregister(ToggleName);
        SelfTest.Unregister(RespawnName);
        SelfTest.Unregister(EggStackName);
        SelfTest.Unregister(StarsName);
        SelfTest.Unregister(FarmerRangeName);
        SelfTest.Unregister(CompatName);
        SelfTest.Unregister(SetupName);
        SelfTest.Unregister(LogName);
    }

    // ---------- pen helpers ----------

    // New tests fail (not skip) when Star Level System decides births: a skip would look like a checked item.
    private static bool PenBlocked(string test)
    {
        if (!Compat.StarLevelSystemLoaded)
        {
            return false;
        }
        SelfTest.Fail(test, "Star Level System is installed: Breeding Star Inheritance leaves births to it, so this test "
                            + "cannot check anything. Run it without Star Level System.");
        return true;
    }

    // Animal senses nothing (sees and hears no one): it never picks a fight, so tamed and untamed test animals side
    // by side do not hurt each other while the test runs. Instance values only; the animal is destroyed after.
    private static void Calm(Character character)
    {
        var ai = character != null ? character.GetComponent<BaseAI>() : null;
        if (ai != null)
        {
            ai.m_viewRange = 0f;
            ai.m_hearRange = 0f;
        }
    }

    // Untamed animal (level as given). Me drive its Procreate myself like the tamed ones.
    private static Character SpawnWild(Pen pen, string prefabName, Vector3 position, int level)
    {
        var prefab = ZNetScene.instance.GetPrefab(prefabName);
        if (prefab == null)
        {
            return null;
        }
        var go = Object.Instantiate(prefab, position, Quaternion.LookRotation(pen.Forward));
        pen.Spawned.Add(go);
        var character = go.GetComponent<Character>();
        if (character == null)
        {
            return null;
        }
        if (level > 1)
        {
            character.SetLevel(level);
        }
        Calm(character);
        var proc = go.GetComponent<Procreation>();
        if (proc != null)
        {
            proc.CancelInvoke(nameof(Procreation.Procreate));
            proc.m_pregnancyChance = -1f;
            proc.m_pregnancyDuration = -1f;
            proc.m_maxCreatures = 1000;
            proc.m_partnerCheckRange = PartnerRange;
            proc.m_totalCheckRange = PenRange;
        }
        return character;
    }

    // Conception through the game's own Procreate (patched or not). 0 = the game did not make her pregnant.
    private static long Conceive(Pen pen, Character mother, Character mate)
    {
        var proc = mother.GetComponent<Procreation>();
        var zdo = mother.m_nview.GetZDO();
        Place(mother, pen.Center, pen.Forward);
        Ready(mother);
        if (mate != null)
        {
            Place(mate, pen.Center + pen.Side * MateGap, pen.Forward);
            Ready(mate);
            mate.m_nview.GetZDO().Set(ZDOVars.s_pregnant, 0L);
        }
        zdo.Set(ZDOVars.s_pregnant, 0L);
        zdo.Set(ZDOVars.s_lovePoints, Mathf.Max(0, proc.m_requiredLovePoints - 1));
        proc.Procreate();
        return zdo.GetLong(ZDOVars.s_pregnant, 0L);
    }

    // Due call through the game's own Procreate. Offspring kept (cleanup destroy it), its level or quality in level.
    // Null = still pregnant (no birth) or no new offspring found.
    private static GameObject Deliver(Pen pen, Character mother, out int level, out bool born)
    {
        level = -1;
        var proc = mother.GetComponent<Procreation>();
        var zdo = mother.m_nview.GetZDO();
        Ready(mother);
        proc.Procreate();
        born = zdo.GetLong(ZDOVars.s_pregnant, 0L) == 0L;
        if (!born)
        {
            return null;
        }
        pen.Births++;
        return TakeNew(pen, mother.transform.position, out level);
    }

    // Nearest new offspring by the mother. Other new ones in the pen (twins of other mods) destroyed: no leak.
    private static GameObject TakeNew(Pen pen, Vector3 near, out int level)
    {
        level = -1;
        var wanted = pen.Offspring + "(Clone)";
        var fresh = new List<GameObject>();
        GameObject found = null;
        var best = float.MaxValue;
        if (pen.Egg)
        {
            foreach (var item in ItemDrop.s_instances)
            {
                if (item == null || item.gameObject.name != wanted || !pen.Seen.Add(item.gameObject.GetInstanceID()))
                {
                    continue;
                }
                fresh.Add(item.gameObject);
                var d = Vector3.Distance(near, item.transform.position);
                if (d < best)
                {
                    best = d;
                    found = item.gameObject;
                    level = item.m_itemData.m_quality;
                }
            }
        }
        else
        {
            foreach (var ai in BaseAI.BaseAIInstances)
            {
                if (ai == null || ai.m_character == null || ai.gameObject.name != wanted
                    || !pen.Seen.Add(ai.gameObject.GetInstanceID()))
                {
                    continue;
                }
                fresh.Add(ai.gameObject);
                var d = Vector3.Distance(near, ai.transform.position);
                if (d < best)
                {
                    best = d;
                    found = ai.gameObject;
                    level = ai.m_character.GetLevel();
                }
            }
        }
        foreach (var extra in fresh)
        {
            if (extra != null && !ReferenceEquals(extra, found) && Vector3.Distance(near, extra.transform.position) <= PenRange)
            {
                ZNetScene.instance.Destroy(extra);
            }
        }
        if (found != null)
        {
            pen.Spawned.Add(found);
        }
        return found;
    }

    // One birth being watched: log mark and counters from before the conception.
    private sealed class Watch
    {
        internal int Mark;
        internal int Births0;
        internal int Own;
        internal long Stamp;
    }

    // Conception. noted = the mod must have written the partner note (feature on); false = vanilla conception.
    private static Watch Begin(Checks c, Pen pen, Character mother, Character mate, string label, bool noted = true)
    {
        var w = new Watch { Mark = LogMark(), Births0 = BirthCount, Own = mother.GetLevel() };
        var conceptions = ConceptionCount;
        w.Stamp = Conceive(pen, mother, mate);
        if (!c.Check(w.Stamp != 0L, $"{label}: no conception (the game did not make the level {w.Own} parent pregnant)"))
        {
            return null;
        }
        var zdo = mother.m_nview.GetZDO();
        if (!noted)
        {
            c.Check(BirthRecord.StoredStamp(zdo) != w.Stamp, $"{label}: a partner note was written although the mod is not acting");
            c.Check(ConceptionCount == conceptions, $"{label}: the patch handled a conception although the mod is not acting");
            return w;
        }
        var mateLevel = mate.GetLevel();
        c.Check(BirthRecord.TryRead(zdo, w.Stamp, out var recorded) && recorded == mateLevel,
            $"{label}: the partner note after conception must say level {mateLevel} (found {recorded})");
        c.Check(ConceptionCount == conceptions + 1 && LastConception.Kind == ConceptionKind.Partner
                && LastConception.Partner == mateLevel && LastConception.Own == w.Own,
            $"{label}: the patch must note a conception of level {w.Own} with partner level {mateLevel}");
        var name = Utils.GetPrefabName(mother.gameObject);
        c.Check(CountLines(w.Mark, $"Conceived: {name} (level {w.Own}) with partner level {mateLevel} at ") == 1,
            $"{label}: one Debug line 'Conceived: {name} (level {w.Own}) with partner level {mateLevel} at ...' expected");
        return w;
    }

    // Birth by the mod: baby level, what the patch decided, the Debug line (every part given must be in it), parent
    // level untouched. Baby returned (kept until cleanup) or destroyed. Returns baby level, -1 = no baby.
    private static int End(Checks c, Pen pen, Character mother, Watch w, string label, int expectLevel,
        PartnerSource source, int expectPartner, out GameObject baby, bool keep, string endsWith, params string[] parts)
    {
        baby = null;
        if (w == null)
        {
            return -1;
        }
        var zdo = mother.m_nview.GetZDO();
        var storedBefore = BirthRecord.StoredStamp(zdo);
        baby = Deliver(pen, mother, out var level, out var born);
        if (!c.Check(born, $"{label}: no birth (still pregnant after the due call)"))
        {
            return -1;
        }
        if (!c.Check(baby != null, $"{label}: no new {pen.Offspring} after the birth"))
        {
            return -1;
        }
        c.Check(level == expectLevel, $"{label}: baby level {level}, expected {expectLevel}");
        c.Check(BirthCount == w.Births0 + 1, $"{label}: the patch handled {BirthCount - w.Births0} births, expected 1");
        var b = LastBirth;
        var partnerOk = source == PartnerSource.None || source == PartnerSource.BredAlone || b.Partner == expectPartner;
        c.Check(b.Decided && b.Own == w.Own && b.Source == source && partnerOk && b.Decision.Level == expectLevel,
            $"{label}: the patch decided own {b.Own}, partner {b.Partner} ({b.Source}), level {b.Decision.Level}; "
            + $"expected own {w.Own}, partner {expectPartner} ({source}), level {expectLevel}");
        var line = LastLineStarting(w.Mark, BirthLineStart);
        if (c.Check(line != null, $"{label}: no Debug birth line"))
        {
            foreach (var part in parts)
            {
                c.Check(line.IndexOf(part, StringComparison.Ordinal) >= 0, $"{label}: the birth line must contain '{part}', got: {line}");
            }
            if (endsWith != null)
            {
                c.Check(line.EndsWith(endsWith, StringComparison.Ordinal), $"{label}: the birth line must end with '{endsWith}', got: {line}");
            }
        }
        c.Check(mother.GetLevel() == w.Own && zdo.GetInt(ZDOVars.s_level, 1) == w.Own,
            $"{label}: parent level {mother.GetLevel()} (saved {zdo.GetInt(ZDOVars.s_level, 1)}) after the birth, was {w.Own}");
        // A note that existed is cleared by overwriting its stamp with 0 (a removed key never reaches other games).
        if (storedBefore != -1L)
        {
            c.Check(BirthRecord.StoredStamp(zdo) == 0L, $"{label}: partner note not cleared after the birth (stamp {BirthRecord.StoredStamp(zdo)})");
        }
        if (!keep)
        {
            ZNetScene.instance.Destroy(baby);
            baby = null;
        }
        return level;
    }

    private static int End(Checks c, Pen pen, Character mother, Watch w, string label, int expectLevel,
        PartnerSource source, int expectPartner, string endsWith, params string[] parts)
    {
        return End(c, pen, mother, w, label, expectLevel, source, expectPartner, out _, false, endsWith, parts);
    }

    // Birth the mod must NOT touch (feature off, or it stands aside): vanilla level, no patch note, no Debug line.
    private static void EndVanilla(Checks c, Pen pen, Character mother, Watch w, string label, int expectLevel)
    {
        if (w == null)
        {
            return;
        }
        var baby = Deliver(pen, mother, out var level, out var born);
        if (!c.Check(born, $"{label}: no birth (still pregnant after the due call)"))
        {
            return;
        }
        if (c.Check(baby != null, $"{label}: no new {pen.Offspring} after the birth"))
        {
            c.Check(level == expectLevel, $"{label}: baby level {level}, expected {expectLevel} (the game's own rule: the level of the parent giving birth)");
            ZNetScene.instance.Destroy(baby);
        }
        c.Check(BirthCount == w.Births0, $"{label}: the patch handled a birth although the mod is not acting");
        c.Check(CountLines(w.Mark, BirthLineStart) == 0, $"{label}: a Debug birth line appeared although the mod is not acting");
        c.Check(mother.GetLevel() == w.Own, $"{label}: parent level {mother.GetLevel()} after the birth, was {w.Own}");
    }

    private static HashSet<int> InstanceIds(ICollection<string> prefabNames)
    {
        var ids = new HashSet<int>();
        foreach (var ai in BaseAI.BaseAIInstances)
        {
            if (ai != null && IsOneOf(ai.gameObject.name, prefabNames))
            {
                ids.Add(ai.gameObject.GetInstanceID());
            }
        }
        return ids;
    }

    private static bool IsOneOf(string objectName, ICollection<string> prefabNames)
    {
        foreach (var name in prefabNames)
        {
            if (objectName == name + "(Clone)")
            {
                return true;
            }
        }
        return false;
    }

    // Nearest creature of these prefabs that was not there before (hatchling, grown animal).
    private static Character FindNewCreature(ICollection<string> prefabNames, HashSet<int> before, Vector3 near, float range)
    {
        Character found = null;
        var best = range;
        foreach (var ai in BaseAI.BaseAIInstances)
        {
            if (ai == null || ai.m_character == null || !IsOneOf(ai.gameObject.name, prefabNames)
                || before.Contains(ai.gameObject.GetInstanceID()))
            {
                continue;
            }
            var d = Vector3.Distance(near, ai.transform.position);
            if (d <= best)
            {
                best = d;
                found = ai.m_character;
            }
        }
        return found;
    }

    // Game's own hatching (EggGrow.GrowUpdate); fire, roof and wait taken away on this one egg. The egg must have
    // lived a frame (EggGrow.Start). Null = nothing hatched.
    private static Character HatchNow(Checks c, GameObject eggObject, string label, out string hatchName, out bool tamed,
        out string note)
    {
        hatchName = "?";
        tamed = false;
        note = "has no EggGrow";
        var grow = eggObject != null ? eggObject.GetComponent<EggGrow>() : null;
        if (!c.Check(grow != null && grow.m_grownPrefab != null, $"{label}: the egg has no EggGrow with a grown prefab"))
        {
            return null;
        }
        hatchName = grow.m_grownPrefab.name;
        tamed = grow.m_tamed;
        note = $"hatches into {hatchName} (tamed {tamed}; in the game it needs fire {grow.m_requireNearbyFire}, roof "
               + $"{grow.m_requireUnderRoof}, {Inv(grow.m_growTime)} s)";
        var names = new List<string> { hatchName };
        var before = InstanceIds(names);
        var position = eggObject.transform.position;
        grow.m_requireNearbyFire = false;
        grow.m_requireUnderRoof = false;
        grow.m_growTime = -1f;
        grow.GrowUpdate();
        var young = FindNewCreature(names, before, position, 5f);
        c.Check(young != null, $"{label}: no {hatchName} after the game's EggGrow.GrowUpdate");
        return young;
    }

    // Game's own growing up (Growup.GrowUpdate), wait time taken away on this one animal. Null = nothing grew.
    private static Character GrowUp(Checks c, Character young, string label, out string chain)
    {
        chain = "";
        var growup = young != null ? young.GetComponent<Growup>() : null;
        if (!c.Check(growup != null, $"{label}: the young animal has no Growup component"))
        {
            return null;
        }
        var names = new List<string>();
        if (growup.m_grownPrefab != null)
        {
            names.Add(growup.m_grownPrefab.name);
        }
        if (growup.m_altGrownPrefabs != null)
        {
            foreach (var alt in growup.m_altGrownPrefabs)
            {
                if (alt != null && alt.m_prefab != null && !names.Contains(alt.m_prefab.name))
                {
                    names.Add(alt.m_prefab.name);
                }
            }
        }
        chain = $"{Utils.GetPrefabName(young.gameObject)} grows into {string.Join(" / ", names.ToArray())} "
                + $"(grow time {Inv(growup.m_growTime)} s, keeps tame {growup.m_inheritTame})";
        if (!c.Check(names.Count > 0, $"{label}: Growup has no grown prefab"))
        {
            return null;
        }
        var before = InstanceIds(names);
        var position = young.transform.position;
        growup.m_growTime = -1f;
        growup.GrowUpdate();
        var adult = FindNewCreature(names, before, position, 5f);
        c.Check(adult != null, $"{label}: no grown animal after the game's Growup.GrowUpdate");
        if (adult != null)
        {
            var proc = adult.GetComponent<Procreation>();
            if (proc != null)
            {
                proc.CancelInvoke(nameof(Procreation.Procreate));
            }
        }
        return adult;
    }

    // ---------- skills of the test character ----------

    // Whole skill list as found: me change Farming for real (game methods) and put every skill back after.
    private sealed class SkillSnapshot
    {
        private readonly Dictionary<Skills.SkillType, Skills.Skill> _skills = new Dictionary<Skills.SkillType, Skills.Skill>();
        private readonly Dictionary<Skills.SkillType, Vector2> _values = new Dictionary<Skills.SkillType, Vector2>();

        internal static SkillSnapshot Take(Player player)
        {
            var snapshot = new SkillSnapshot();
            var skills = player != null ? player.GetSkills() : null;
            if (skills != null)
            {
                foreach (var kv in skills.m_skillData)
                {
                    snapshot._skills[kv.Key] = kv.Value;
                    snapshot._values[kv.Key] = new Vector2(kv.Value.m_level, kv.Value.m_accumulator);
                }
            }
            return snapshot;
        }

        // Also on a new character object (after a respawn): entries the test added go, old values come back.
        internal void Restore(Player player)
        {
            var skills = player != null ? player.GetSkills() : null;
            if (skills == null)
            {
                return;
            }
            var data = skills.m_skillData;
            var added = new List<Skills.SkillType>();
            foreach (var key in data.Keys)
            {
                if (!_values.ContainsKey(key))
                {
                    added.Add(key);
                }
            }
            foreach (var key in added)
            {
                data.Remove(key);
            }
            foreach (var kv in _values)
            {
                if (!data.TryGetValue(kv.Key, out var skill))
                {
                    skill = _skills[kv.Key];
                    data[kv.Key] = skill;
                }
                skill.m_level = kv.Value.x;
                skill.m_accumulator = kv.Value.y;
            }
        }
    }

    // Like console "resetskill Farming" then "raiseskill Farming <level>" (0 = no Farming entry at all).
    private static void SetFarming(Player player, float level)
    {
        var skills = player.GetSkills();
        skills.ResetSkill(Skills.SkillType.Farming);
        if (level > 0f)
        {
            skills.CheatRaiseSkill("Farming", level, false);
        }
    }

    private static bool SkillListHasFarming(Player player)
    {
        foreach (var skill in player.GetSkills().GetSkillList())
        {
            if (skill != null && skill.m_info != null && skill.m_info.m_skill == Skills.SkillType.Farming)
            {
                return true;
            }
        }
        return false;
    }

    // ---------- breeding.lines ----------

    private static IEnumerator RunLines()
    {
        var pen = new Pen(LinesName, egg: false);
        var c = new Checks(LinesName);
        try
        {
            if (PenBlocked(LinesName) || !SetupPen(pen, "Boar", 1, 3))
            {
                yield break;
            }
            yield return null;
            var player = Player.m_localPlayer;
            const string test = "self-test settings.";

            // 1. Chance 0, both parents give birth: lower parent level, partner read from the note.
            Override = Never;
            for (var i = 0; i < 4; i++)
            {
                var mother = i % 2 == 0 ? pen.B : pen.A;
                var mate = Mate(pen, mother);
                var own = mother.GetLevel();
                var other = mate.GetLevel();
                var label = $"chance 0, own {own}";
                var w = Begin(c, pen, mother, mate, label);
                End(c, pen, mother, w, label, 1, PartnerSource.Recorded, other, test,
                    $"own {own}, partner {other} (recorded)", "-> base 1;", "-> level 1;");
                yield return null;
            }

            // 2. Default numbers, roll forced: high roll = base level, roll 0 = one more. Never the higher parent.
            Override = RuleSettings.Defaults;
            var farming = FarmerSkill.OwnLevel(player);
            var chance = BirthRule.Chance(true, farming, RuleSettings.Defaults);
            foreach (var mother in new[] { pen.B, pen.A })
            {
                var mate = Mate(pen, mother);
                RollOverride = 0.999f;
                var label = $"defaults, roll 0.999, own {mother.GetLevel()}";
                var w = Begin(c, pen, mother, mate, label);
                End(c, pen, mother, w, label, 1, PartnerSource.Recorded, mate.GetLevel(), test,
                    $"farmer you Farming {Pct(farming)} (own) -> chance {Pct(chance)}%; roll 0.999 -> level 1;");
                c.Check(!LastBirth.Decision.Bonus && Close(LastBirth.Decision.Chance, chance) && LastBirth.FarmerLocal,
                    $"{label}: chance {Pct(LastBirth.Decision.Chance)}%, expected {Pct(chance)}% with the own Farming {Pct(farming)}");
                RollOverride = 0f;
                label = $"defaults, roll 0, own {mother.GetLevel()}";
                w = Begin(c, pen, mother, mate, label);
                End(c, pen, mother, w, label, 2, PartnerSource.Recorded, mate.GetLevel(), test,
                    "roll 0.000 -> level 2, extra star;");
                c.Check(LastBirth.Decision.Bonus && LastBirth.Decision.Base == 1, $"{label}: the extra star must come on top of base 1");
                yield return null;
            }
            RollOverride = null;

            // 3. Chance 100: one above the lower parent; 2 + 2 gives 3.
            Override = Always;
            foreach (var mother in new[] { pen.B, pen.A })
            {
                var mate = Mate(pen, mother);
                var label = $"chance 100, own {mother.GetLevel()}";
                var w = Begin(c, pen, mother, mate, label);
                End(c, pen, mother, w, label, 2, PartnerSource.Recorded, mate.GetLevel(), test,
                    "-> base 1;", "-> chance 100%;", "-> level 2, extra star;");
            }
            yield return null;
            pen.A.SetLevel(2);
            pen.B.SetLevel(2);
            foreach (var mother in new[] { pen.B, pen.A })
            {
                var label = "chance 100, parents 2 + 2";
                var w = Begin(c, pen, mother, Mate(pen, mother), label);
                End(c, pen, mother, w, label, 3, PartnerSource.Recorded, 2, test,
                    "own 2, partner 2 (recorded)", "-> base 2;", "-> level 3, extra star;");
                c.Check(LastBirth.Decision.Bonus && LastBirth.Decision.Base == 2 && LastBirth.Decision.Cap == 3,
                    $"{label}: base 2, cap 3, extra star expected");
            }
            yield return null;

            // 4. Cap: 3 + 3 stays 3 with no roll; MaxStars 1 caps 2 + 2 at 2.
            pen.A.SetLevel(3);
            pen.B.SetLevel(3);
            {
                var label = "chance 100, parents 3 + 3";
                var w = Begin(c, pen, pen.B, pen.A, label);
                End(c, pen, pen.B, w, label, 3, PartnerSource.Recorded, 3, test, "-> base 3;", "-> level 3, at the cap: no roll;");
                var d = LastBirth.Decision;
                c.Check(d.AtCap && !d.Bonus && d.Chance == 0f && d.Cap == 3, $"{label}: at the cap (3), chance 0, no extra star expected");
            }
            Override = new RuleSettings(100f, 100f, 100f, 1);
            pen.A.SetLevel(2);
            pen.B.SetLevel(2);
            {
                var label = "MaxStars 1, chance 100, parents 2 + 2";
                var w = Begin(c, pen, pen.B, pen.A, label);
                End(c, pen, pen.B, w, label, 2, PartnerSource.Recorded, 2, test, "-> base 2;", "-> level 2, at the cap: no roll;");
                var d = LastBirth.Decision;
                c.Check(d.AtCap && !d.Bonus && d.Chance == 0f && d.Cap == 2, $"{label}: at the cap (2), chance 0, no extra star expected");
            }
            yield return null;

            // 5. Above the cap: never lowered.
            Override = Always;
            pen.A.SetLevel(5);
            pen.B.SetLevel(5);
            {
                var label = "chance 100, parents 5 + 5";
                var w = Begin(c, pen, pen.B, pen.A, label);
                End(c, pen, pen.B, w, label, 5, PartnerSource.Recorded, 5, test, "-> base 5;", "-> level 5, at the cap: no roll;");
                c.Check(LastBirth.Decision.AtCap, $"{label}: at the cap expected");
            }
            yield return null;

            // 6. Partner gone after the conception: the note still gives its level.
            Override = Never;
            pen.A.SetLevel(1);
            pen.B.SetLevel(3);
            {
                var label = "partner moved 25 m away after the conception";
                var w = Begin(c, pen, pen.B, pen.A, label);
                Place(pen.A, pen.Center + pen.Side * 25f, pen.Forward);
                End(c, pen, pen.B, w, label, 1, PartnerSource.Recorded, 1, test, "own 3, partner 1 (recorded)");
            }
            yield return null;
            {
                var label = "partner destroyed after the conception";
                var w = Begin(c, pen, pen.B, pen.A, label);
                ZNetScene.instance.Destroy(pen.A.gameObject);
                yield return null;
                yield return null;
                c.Check(pen.A == null, $"{label}: the partner is still there");
                End(c, pen, pen.B, w, label, 1, PartnerSource.Recorded, 1, test, "own 3, partner 1 (recorded)");
            }
            Override = null;
            c.Report($"{pen.Births} births through the game's breeding code: lower parent from both parents with the recorded "
                     + "partner, forced roll both ways at the default numbers, extra star (1 + 3 -> 2, 2 + 2 -> 3), cap (3 + 3, "
                     + "MaxStars 1 with 2 + 2), level 5 kept, partner moved or destroyed after the conception; every Debug "
                     + "birth line checked");
        }
        finally
        {
            RollOverride = null;
            Cleanup(pen);
        }
    }

    // ---------- breeding.ticks ----------

    private sealed class TickRun
    {
        internal readonly List<int> Levels = new List<int>();
        internal readonly List<BirthNote> Notes = new List<BirthNote>();
        internal bool LevelSlip;
        internal float Seconds;
    }

    private static IEnumerator RunTicks()
    {
        var pen = new Pen(TicksName, egg: false);
        var c = new Checks(TicksName);
        Procreation procA = null;
        Procreation procB = null;
        try
        {
            if (PenBlocked(TicksName) || !SetupPen(pen, "Boar", 1, 3))
            {
                yield break;
            }
            yield return null;
            procA = pen.A.GetComponent<Procreation>();
            procB = pen.B.GetComponent<Procreation>();
            // Parents sense nothing: a creature passing by never alerts them between two frames (alerted animals do
            // not start a pregnancy, and the ticks here are the game's own, not mine).
            Calm(pen.A);
            Calm(pen.B);
            foreach (var proc in new[] { procA, procB })
            {
                // Fast pen: the game's own repeating tick every 0.3 s, pregnancy 0.5 s. Skip chance, population and
                // ranges as set by Spawn.
                proc.m_pregnancyDuration = 0.5f;
                var zdo = proc.m_nview.GetZDO();
                zdo.Set(ZDOVars.s_pregnant, 0L);
                zdo.Set(ZDOVars.s_lovePoints, 0);
                proc.InvokeRepeating(nameof(Procreation.Procreate), 0.2f, 0.3f);
            }

            // 1. Chance 0 until 4 births with both parents seen.
            Override = Never;
            var first = new TickRun();
            yield return WatchTicks(pen, 4, true, 35f, first);
            c.Check(first.Levels.Count >= 4, $"chance 0: only {first.Levels.Count} births in {Inv(first.Seconds)} s of the game's own breeding ticks");
            var ownSeen = new HashSet<int>();
            foreach (var note in first.Notes)
            {
                ownSeen.Add(note.Own);
                var partner = note.Own == 1 ? 3 : 1;
                c.Check(note.Decided && note.Source == PartnerSource.Recorded && note.Partner == partner && note.Decision.Level == 1,
                    $"chance 0: birth by the level {note.Own} parent decided partner {note.Partner} ({note.Source}), level {note.Decision.Level}; "
                    + $"expected partner {partner} (Recorded), level 1");
            }
            c.Check(ownSeen.Contains(1) && ownSeen.Contains(3), "chance 0: both parents must have given birth");
            foreach (var level in first.Levels)
            {
                c.Check(level == 1, $"chance 0: a piglet of level {level}, expected 1");
            }
            c.Check(first.Notes.Count == first.Levels.Count, $"chance 0: {first.Levels.Count} piglets but the patch handled {first.Notes.Count} births");
            c.Check(!first.LevelSlip, "chance 0: a parent showed another level between two frames (the level swap leaked out of the call)");

            // 2. Default numbers, 8 more births: never the higher parent's level.
            Override = RuleSettings.Defaults;
            var second = new TickRun();
            yield return WatchTicks(pen, 8, false, 45f, second);
            c.Check(second.Levels.Count >= 8, $"defaults: only {second.Levels.Count} births in {Inv(second.Seconds)} s of the game's own breeding ticks");
            var counts = new int[4];
            foreach (var level in second.Levels)
            {
                c.Check(level == 1 || level == 2, $"defaults: a piglet of level {level}, expected 1 or 2 (never the level-3 parent's level)");
                counts[Mathf.Clamp(level, 0, 3)]++;
            }
            foreach (var note in second.Notes)
            {
                c.Check(note.Source == PartnerSource.Recorded && note.Decision.Base == 1,
                    $"defaults: birth by the level {note.Own} parent used partner {note.Partner} ({note.Source}), base {note.Decision.Base}; expected the recorded partner and base 1");
            }
            c.Check(!second.LevelSlip, "defaults: a parent showed another level between two frames");
            Override = null;
            c.Report($"the game's own breeding ticks (0.3 s tick, 0.5 s pregnancy): chance 0 -> {first.Levels.Count} piglets, all level 1, "
                     + $"from both parents with the recorded partner ({Inv(first.Seconds)} s); default numbers -> {second.Levels.Count} piglets, "
                     + $"level 1 x{counts[1]}, level 2 x{counts[2]}, level 3 x{counts[3]} ({Inv(second.Seconds)} s)");
        }
        finally
        {
            if (procA != null)
            {
                procA.CancelInvoke(nameof(Procreation.Procreate));
            }
            if (procB != null)
            {
                procB.CancelInvoke(nameof(Procreation.Procreate));
            }
            Cleanup(pen);
        }
    }

    // Parents held in place, fed and calm; every new piglet read then removed. Ends at <births> births (and both
    // parents seen when asked) or at the timeout.
    private static IEnumerator WatchTicks(Pen pen, int births, bool bothParents, float timeout, TickRun run)
    {
        var levelA = pen.A.GetLevel();
        var levelB = pen.B.GetLevel();
        var seq0 = BirthCount;
        var wanted = pen.Offspring + "(Clone)";
        var start = Time.time;
        var fresh = new List<BaseAI>();
        while (Time.time - start < timeout)
        {
            Place(pen.B, pen.Center, pen.Forward);
            Place(pen.A, pen.Center + pen.Side * MateGap, pen.Forward);
            Ready(pen.A);
            Ready(pen.B);
            if (pen.A.GetLevel() != levelA || pen.B.GetLevel() != levelB)
            {
                run.LevelSlip = true;
            }
            fresh.Clear();
            foreach (var ai in BaseAI.BaseAIInstances)
            {
                if (ai != null && ai.m_character != null && ai.gameObject.name == wanted
                    && Vector3.Distance(pen.Center, ai.transform.position) <= PenRange + 5f
                    && pen.Seen.Add(ai.gameObject.GetInstanceID()))
                {
                    fresh.Add(ai);
                }
            }
            foreach (var ai in fresh)
            {
                run.Levels.Add(ai.m_character.GetLevel());
                pen.Births++;
                ZNetScene.instance.Destroy(ai.gameObject);
            }
            run.Notes.Clear();
            var owns = 0;
            foreach (var note in Births)
            {
                if (note.Seq > seq0)
                {
                    run.Notes.Add(note);
                    owns |= note.Own == levelA ? 1 : note.Own == levelB ? 2 : 0;
                }
            }
            if (run.Levels.Count >= births && (!bothParents || owns == 3))
            {
                break;
            }
            yield return null;
        }
        run.Seconds = Time.time - start;
    }

    // ---------- breeding.farming ----------

    private static IEnumerator RunFarming()
    {
        var pen = new Pen(FarmingName, egg: false);
        var c = new Checks(FarmingName);
        var player = Player.m_localPlayer;
        SkillSnapshot skillsBefore = null;
        try
        {
            if (player == null)
            {
                SelfTest.Fail(FarmingName, "no local player (the test needs a world)");
                yield break;
            }
            if (PenBlocked(FarmingName) || !SetupPen(pen, "Boar", 1, 1))
            {
                yield break;
            }
            skillsBefore = SkillSnapshot.Take(player);
            Calm(pen.A);
            Calm(pen.B);
            var wild = SpawnWild(pen, "Boar", pen.Center + pen.Side * 6f, 1);
            yield return null;
            if (wild == null)
            {
                SelfTest.Fail(FarmingName, "could not spawn the untamed boar");
                yield break;
            }
            var skills = player.GetSkills();
            var procA = pen.A.GetComponent<Procreation>();
            var procB = pen.B.GetComponent<Procreation>();
            var procWild = wild.GetComponent<Procreation>();
            const string test = "self-test settings.";

            // 1. A character without a Farming entry keeps none, whatever kind of breeding tick runs (all in this
            //    frame: no other mod can add the entry in between). Throttle reset before each call = each call also
            //    reads the Farming level to publish it.
            Override = Never;
            SetFarming(player, 0f);
            c.Check(!FarmerSkill.HasFarmingEntry(player), "resetskill Farming must leave no Farming entry");
            var ticks = 0;
            for (var i = 0; i < 2; i++)
            {
                foreach (var proc in new[] { procA, procB, procWild })
                {
                    // Plain tick (not pregnant, love points from 0: no conception here).
                    var zdo = proc.m_nview.GetZDO();
                    zdo.Set(ZDOVars.s_pregnant, 0L);
                    zdo.Set(ZDOVars.s_lovePoints, 0);
                    Ready(proc.GetComponent<Character>());
                    FarmerSkill.Reset();
                    proc.Procreate();
                    ticks++;
                }
            }
            {
                var label = "no Farming entry: birth";
                var w = Begin(c, pen, pen.B, pen.A, label);
                ticks++;
                if (w != null)
                {
                    // Pregnant but not due: the whole decision runs and the game returns early.
                    procB.m_pregnancyDuration = 3600f;
                    for (var i = 0; i < 2; i++)
                    {
                        FarmerSkill.Reset();
                        procB.Procreate();
                        ticks++;
                        c.Check(pen.B.GetLevel() == w.Own && procB.m_nview.GetZDO().GetLong(ZDOVars.s_pregnant, 0L) == w.Stamp
                                && BirthCount == w.Births0,
                            $"{label}: a pregnant tick that is not due must change nothing (level {pen.B.GetLevel()}, births handled {BirthCount - w.Births0})");
                    }
                    procB.m_pregnancyDuration = -1f;
                    FarmerSkill.Reset();
                    End(c, pen, pen.B, w, label, 1, PartnerSource.Recorded, 1, test, "farmer you Farming 0 (own)");
                    ticks++;
                }
            }
            c.Check(!FarmerSkill.HasFarmingEntry(player) && !SkillListHasFarming(player),
                $"{ticks} breeding ticks (tamed, untamed, pregnant, birth) added a Farming entry to a character that had none "
                + "(the Skills tab lists these entries)");
            c.Check(FarmerSkill.ReadPublished(player) == FarmerSkill.OwnLevel(player),
                $"published Farming {Inv(FarmerSkill.ReadPublished(player))}, own Farming {Inv(FarmerSkill.OwnLevel(player))}");
            yield return null;

            // 2. Farming decides the chance: 0 -> never, 100 -> always (chances 0 / 100, no farmer 0).
            Override = new RuleSettings(0f, 100f, 0f, 2);
            SetFarming(player, 0f);
            {
                var label = "ChanceAtFarming0 0, ChanceAtFarming100 100, Farming 0";
                var w = Begin(c, pen, pen.B, pen.A, label);
                End(c, pen, pen.B, w, label, 1, PartnerSource.Recorded, 1, test, "farmer you Farming 0 (own) -> chance 0%;");
                var b = LastBirth;
                c.Check(b.FarmerKnown && b.FarmerLocal && b.FarmingLevel == 0f && b.Decision.Chance == 0f,
                    $"{label}: farmer known {b.FarmerKnown}, local {b.FarmerLocal}, Farming {Inv(b.FarmingLevel)}, chance {Pct(b.Decision.Chance)}%");
                c.Check(!FarmerSkill.HasFarmingEntry(player), $"{label}: the birth added a Farming entry");
            }
            skills.CheatRaiseSkill("Farming", 100f, false);
            c.Check(FarmerSkill.HasFarmingEntry(player) && FarmerSkill.OwnLevel(player) == 100f,
                $"raiseskill Farming 100 must give Farming 100 (found {Inv(FarmerSkill.OwnLevel(player))})");
            {
                var label = "ChanceAtFarming0 0, ChanceAtFarming100 100, Farming 100";
                var w = Begin(c, pen, pen.B, pen.A, label);
                End(c, pen, pen.B, w, label, 2, PartnerSource.Recorded, 1, test,
                    "farmer you Farming 100 (own) -> chance 100%;", "-> level 2, extra star;");
                var b = LastBirth;
                c.Check(b.FarmerLocal && b.FarmingLevel == 100f && Close(b.Decision.Chance, 100f),
                    $"{label}: local {b.FarmerLocal}, Farming {Inv(b.FarmingLevel)}, chance {Pct(b.Decision.Chance)}%");
            }
            yield return null;

            // 3. A raise is published at the next breeding tick (here: a tick of the untamed boar).
            SetFarming(player, 0f);
            FarmerSkill.Reset();
            FarmerSkill.PublishNow();
            c.Check(FarmerSkill.ReadPublished(player) == 0f, $"Farming 0 must be published before the raise (found {Inv(FarmerSkill.ReadPublished(player))})");
            skills.CheatRaiseSkill("Farming", 10f, false);
            c.Check(FarmerSkill.HasFarmingEntry(player) && SkillListHasFarming(player), "raiseskill Farming 10 must add the Farming entry (the game's own rule)");
            c.Check(FarmerSkill.ReadPublished(player) == 0f, "the raise must not be published before a breeding tick");
            {
                var mark = LogMark();
                FarmerSkill.Reset();
                procWild.Procreate();
                c.Check(FarmerSkill.ReadPublished(player) == 10f, $"after a breeding tick the published Farming must be 10 (found {Inv(FarmerSkill.ReadPublished(player))})");
                c.Check(CountLines(mark, "Published Farming level 10 for other players.") == 1,
                    "one Debug line 'Published Farming level 10 for other players.' expected after the breeding tick");
            }
            yield return null;

            // 4. Chance line with the default numbers: Farming 0 / 50 / 100 -> 15 / 32.5 / 50 %. Roll forced high:
            //    the baby stays at the base level, only the chance matters here.
            Override = RuleSettings.Defaults;
            RollOverride = 0.999f;
            var steps = new[] { new Vector2(0f, 15f), new Vector2(50f, 32.5f), new Vector2(100f, 50f) };
            foreach (var step in steps)
            {
                SetFarming(player, step.x);
                var label = $"defaults, Farming {Pct(step.x)}";
                var w = Begin(c, pen, pen.B, pen.A, label);
                End(c, pen, pen.B, w, label, 1, PartnerSource.Recorded, 1, test,
                    $"farmer you Farming {Pct(step.x)} (own) -> chance {Pct(step.y)}%;");
                c.Check(Close(LastBirth.Decision.Chance, step.y) && LastBirth.FarmerLocal,
                    $"{label}: chance {Pct(LastBirth.Decision.Chance)}%, expected {Pct(step.y)}%");
            }
            yield return null;

            // 5. Pen 80 m from the character: the own Farming still counts, same chance.
            SetFarming(player, 50f);
            {
                var home = pen.Center;
                pen.Center = Ground(player.transform.position + pen.Forward * 80f);
                var label = "defaults, Farming 50, pen 80 m away";
                var w = Begin(c, pen, pen.B, pen.A, label);
                var distance = Vector3.Distance(player.transform.position, pen.B.transform.position);
                End(c, pen, pen.B, w, label, 1, PartnerSource.Recorded, 1, test, "farmer you Farming 50 (own) -> chance 32.5%;");
                c.Check(distance >= 79f && LastBirth.FarmerLocal && Close(LastBirth.Decision.Chance, 32.5f),
                    $"{label}: the parent was {Inv(distance)} m away, own farmer {LastBirth.FarmerLocal}, chance {Pct(LastBirth.Decision.Chance)}%");
                pen.Center = home;
                Place(pen.B, pen.Center, pen.Forward);
                Place(pen.A, pen.Center + pen.Side * MateGap, pen.Forward);
            }
            yield return null;

            // 6. ChanceAtFarming0 = 10 with Farming 0.
            Override = new RuleSettings(10f, 50f, 10f, 2);
            SetFarming(player, 0f);
            {
                var label = "ChanceAtFarming0 10, Farming 0";
                var w = Begin(c, pen, pen.B, pen.A, label);
                End(c, pen, pen.B, w, label, 1, PartnerSource.Recorded, 1, test, "farmer you Farming 0 (own) -> chance 10%;");
                c.Check(Close(LastBirth.Decision.Chance, 10f), $"{label}: chance {Pct(LastBirth.Decision.Chance)}%, expected 10%");
            }
            yield return null;

            // 7. No override: this game's own config, also with numbers of an older server session still in memory
            //    (a game that left a server keeps them; single player must not use them).
            Override = null;
            ServerSettings.TestKeepOldServerNumbers(new RuleSettings(0f, 0f, 0f, 0), 5f);
            var own = Plugin.OwnSettings();
            var current = Plugin.CurrentSettings();
            c.Check(current.Source == SettingsSource.Own && Same(current.Rule, own.Rule) && current.FarmerRange == own.FarmerRange,
                $"single player must use the own config, also after a server session (source {current.Source})");
            // Own config still at the default numbers (as shipped): the line itself must give 15 / 32.5 / 50 % with
            // "own settings" (TESTING.md T06 as written). Changed by the user: only source and rule are checked here.
            var ownIsDefault = Same(own.Rule, RuleSettings.Defaults);
            if (!ownIsDefault)
            {
                c.Note("this game's own config is not at the default numbers: 'chance 15% / 32.5% / 50%' was checked with the default "
                       + "numbers forced in memory only, the 'own settings' births with the config's own numbers");
            }
            foreach (var step in steps)
            {
                SetFarming(player, step.x);
                var expected = BirthRule.Decide(1, 1, 0, own.Rule, true, step.x, 0.999f);
                var label = $"own settings ({ServerSettings.Describe(own.Rule, own.FarmerRange)}), Farming {Pct(step.x)}";
                var w = Begin(c, pen, pen.B, pen.A, label);
                var ownParts = ownIsDefault
                    ? new[] { $"farmer you Farming {Pct(step.x)} (own) -> chance {Pct(step.y)}%; roll 0.999 -> level 1;" }
                    : new string[0];
                End(c, pen, pen.B, w, label, expected.Level, PartnerSource.Recorded, 1, "own settings.", ownParts);
                var b = LastBirth;
                c.Check(b.Settings == SettingsSource.Own && Close(b.Decision.Chance, expected.Chance) && b.Decision.AtCap == expected.AtCap,
                    $"{label}: settings {b.Settings}, chance {Pct(b.Decision.Chance)}% (at the cap {b.Decision.AtCap}); expected Own, "
                    + $"{Pct(expected.Chance)}% (at the cap {expected.AtCap})");
            }
            c.Check(CountLines(0, ServerSettingsLineStart) == 0, "a 'Using the server's breeding settings' line was logged in single player");
            RollOverride = null;
            c.Report($"{pen.Births} births with the character's real Farming skill: no Farming entry added by {ticks} breeding ticks, "
                     + "a raise to 10 published at the next tick, Farming 0 / 100 -> never / always with chances 0 / 100, chance "
                     + "line 15 / 32.5 / 50 % and 10 %, pen 80 m away, own settings (" + ServerSettings.Describe(own.Rule, own.FarmerRange)
                     + ") also with an older server session in memory; no server-settings line in single player");
        }
        finally
        {
            RollOverride = null;
            Override = null;
            ServerSettings.Forget();
            var now = Player.m_localPlayer;
            if (skillsBefore != null && now != null)
            {
                skillsBefore.Restore(now);
            }
            FarmerSkill.Reset();
            FarmerSkill.PublishNow();
            Cleanup(pen);
        }
    }

    // ---------- breeding.hatch, breeding.asksvin ----------

    // Egg of two level-1 parents at chance 100 = quality 2; the game's own hatching and growing keep level 2.
    private static IEnumerator RunChain(string test, string species)
    {
        var pen = new Pen(test, egg: true);
        var c = new Checks(test);
        var extra = new List<GameObject>();
        try
        {
            if (PenBlocked(test) || !SetupPen(pen, species, 1, 1))
            {
                yield break;
            }
            yield return null;
            const string settings = "self-test settings.";

            Override = Always;
            var w = Begin(c, pen, pen.B, pen.A, "chance 100, parents 1 + 1");
            End(c, pen, pen.B, w, "chance 100, parents 1 + 1", 2, PartnerSource.Recorded, 1, out var eggObject, true, settings,
                "-> level 2, extra star;");
            if (eggObject == null)
            {
                c.Report("");
                yield break;
            }
            var item = eggObject.GetComponent<ItemDrop>();
            var hover = FirstLine(item.GetHoverText());
            c.Check(hover.IndexOf("[2]", StringComparison.Ordinal) >= 0, $"the ground hover of the egg must show [2], got '{hover}'");
            yield return null;
            yield return null;
            if (!c.Check(eggObject != null, "the egg vanished before hatching"))
            {
                c.Report("");
                yield break;
            }
            item.Load();
            c.Check(item.m_itemData.m_quality == 2 && item.m_itemData.m_stack == 1,
                $"the saved egg must be one egg of quality 2 (quality {item.m_itemData.m_quality}, stack {item.m_itemData.m_stack})");
            var maxQuality = item.m_itemData.m_shared.m_maxQuality;
            var young = HatchNow(c, eggObject, "hatching", out var hatchName, out var hatchTamed, out var hatchNote);
            var eggNote = $"{species} lays {pen.Offspring} (max quality {maxQuality}), which {hatchNote}";
            if (young == null)
            {
                c.Note(eggNote);
                c.Report("");
                yield break;
            }
            extra.Add(young.gameObject);
            c.Check(young.GetLevel() == 2, $"the hatchling is level {young.GetLevel()}, expected 2 (1 star)");
            c.Check(young.IsTamed() == hatchTamed, $"the hatchling is tamed {young.IsTamed()}, the egg says {hatchTamed}");
            yield return null;
            yield return null;
            if (!c.Check(young != null, "the hatchling vanished before growing up"))
            {
                c.Report("");
                yield break;
            }
            var youngTamed = young.IsTamed();
            var adult = GrowUp(c, young, "growing up", out var chain);
            c.Note($"{eggNote}; {chain}");
            if (adult != null)
            {
                extra.Add(adult.gameObject);
                c.Check(adult.GetLevel() == 2, $"the grown animal is level {adult.GetLevel()}, expected 2 (1 star)");
                c.Check(adult.IsTamed() == youngTamed, $"the grown animal is tamed {adult.IsTamed()}, the young one was {youngTamed}");
                c.Check(Utils.GetPrefabName(adult.gameObject) == species, $"the grown animal is a {Utils.GetPrefabName(adult.gameObject)}, expected {species}");
            }
            yield return null;

            // Chance 0, parents 1 + 3: plain eggs only (quality 1, no number in the hover), whoever lays.
            Override = Never;
            pen.B.SetLevel(3);
            var mothers = pen.BothBreed ? new[] { pen.B, pen.A } : new[] { pen.B };
            foreach (var mother in mothers)
            {
                var mate = Mate(pen, mother);
                var label = $"chance 0, own {mother.GetLevel()}";
                w = Begin(c, pen, mother, mate, label);
                End(c, pen, mother, w, label, 1, PartnerSource.Recorded, mate.GetLevel(), out var plain, true, settings);
                if (plain != null)
                {
                    var plainHover = FirstLine(plain.GetComponent<ItemDrop>().GetHoverText());
                    c.Check(plainHover.IndexOf('[') < 0, $"{label}: the hover of a quality-1 egg must show no number, got '{plainHover}'");
                    ZNetScene.instance.Destroy(plain);
                }
            }
            Override = null;
            c.Report($"{species}: egg of two level-1 parents at chance 100 is quality 2 ('{hover}'), hatches into a level-2 "
                     + $"{hatchName} and grows into a level-2 {species} (the game's own hatching and growing code; fire, roof and "
                     + "waiting time taken away on the test egg and hatchling); chance 0 with parents 1 + 3 gives quality-1 eggs");
        }
        finally
        {
            Override = null;
            foreach (var go in extra)
            {
                if (go != null && ZNetScene.instance != null)
                {
                    ZNetScene.instance.Destroy(go);
                }
            }
            Cleanup(pen);
        }
    }

    private static string FirstLine(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }
        var cut = text.IndexOf('\n');
        return cut >= 0 ? text.Substring(0, cut) : text;
    }

    // ---------- breeding.scope ----------

    private static IEnumerator RunScope()
    {
        var pen = new Pen(ScopeName, egg: false);
        var c = new Checks(ScopeName);
        var extra = new List<GameObject>();
        try
        {
            if (PenBlocked(ScopeName) || !SetupPen(pen, "Boar", 1, 3))
            {
                yield break;
            }
            yield return null;

            // 1. A level-2 piglet grows (game code) into a tamed level-2 boar.
            Override = Always;
            var w = Begin(c, pen, pen.B, pen.A, "piglet");
            End(c, pen, pen.B, w, "piglet", 2, PartnerSource.Recorded, 1, out var piglet, true, null);
            var chain = "";
            if (piglet != null)
            {
                yield return null;
                yield return null;
                var young = piglet != null ? piglet.GetComponent<Character>() : null;
                if (c.Check(young != null, "the piglet vanished before growing up"))
                {
                    var adult = GrowUp(c, young, "growing up", out chain);
                    if (adult != null)
                    {
                        extra.Add(adult.gameObject);
                        Calm(adult);
                        c.Check(adult.GetLevel() == 2, $"the grown boar is level {adult.GetLevel()}, expected 2 (1 star)");
                        c.Check(adult.IsTamed(), "the grown boar is not tamed");
                        c.Check(Utils.GetPrefabName(adult.gameObject) == "Boar", $"the piglet grew into {Utils.GetPrefabName(adult.gameObject)}, expected Boar");
                    }
                }
            }
            Override = Never;
            yield return null;

            // 2. Untamed boars: nothing of the mod acts on them, their levels stay.
            Calm(pen.A);
            Calm(pen.B);
            var wildA = SpawnWild(pen, "Boar", pen.Center + pen.Side * 6f, 2);
            var wildB = SpawnWild(pen, "Boar", pen.Center + pen.Side * 7.5f, 1);
            yield return null;
            if (c.Check(wildA != null && wildB != null, "could not spawn the untamed boars"))
            {
                c.Check(!wildA.IsTamed() && !wildB.IsTamed(), "the spawned boars must be untamed");
                var zdoA = wildA.m_nview.GetZDO();
                var zdoB = wildB.m_nview.GetZDO();
                var procWildA = wildA.GetComponent<Procreation>();
                var procWildB = wildB.GetComponent<Procreation>();
                // As if pregnant and due: a mod that acted on untamed animals would decide a birth here.
                var stamp = ZNet.instance.GetTime().Ticks - TimeSpan.FromSeconds(120).Ticks;
                zdoA.Set(ZDOVars.s_pregnant, stamp);
                zdoA.Set(ZDOVars.s_lovePoints, Mathf.Max(0, procWildA.m_requiredLovePoints - 1));
                zdoB.Set(ZDOVars.s_lovePoints, Mathf.Max(0, procWildB.m_requiredLovePoints - 1));
                var mark = LogMark();
                var births = BirthCount;
                var conceptions = ConceptionCount;
                for (var i = 0; i < 3; i++)
                {
                    procWildA.Procreate();
                    procWildB.Procreate();
                }
                c.Check(zdoA.GetLong(ZDOVars.s_pregnant, 0L) == stamp && zdoB.GetLong(ZDOVars.s_pregnant, 0L) == 0L,
                    "breeding ticks changed the pregnancy of an untamed boar");
                c.Check(BirthCount == births && ConceptionCount == conceptions, "the patch handled a birth or conception of an untamed boar");
                c.Check(BirthRecord.StoredStamp(zdoA) == -1L && BirthRecord.StoredStamp(zdoB) == -1L, "a partner note was written on an untamed boar");
                c.Check(CountLines(mark, BirthLineStart) == 0 && CountLines(mark, ConceivedLineStart) == 0, "a Debug birth or conception line appeared for an untamed boar");
                c.Check(TakeNew(pen, wildA.transform.position, out _) == null, "a piglet appeared by an untamed boar");
                yield return null;
                yield return null;
                yield return null;
                if (c.Check(wildA != null && wildB != null, "an untamed boar vanished"))
                {
                    c.Check(wildA.GetLevel() == 2 && wildA.m_nview.GetZDO().GetInt(ZDOVars.s_level, 1) == 2,
                        $"the untamed 1-star boar is now level {wildA.GetLevel()}");
                    c.Check(wildB.GetLevel() == 1, $"the untamed 0-star boar is now level {wildB.GetLevel()}");
                }
            }

            // 3. The mod patches its five game methods and nothing that sets a level (wild spawns can never change).
            var patched = OwnPatchedMethods();
            c.Check(SamePatches(patched), $"this mod patches [{string.Join(", ", patched.ToArray())}], expected [{string.Join(", ", ExpectedPatches)}]");
            Override = null;
            c.Note(chain);
            c.Report("a level-2 piglet grows into a tamed level-2 Boar (the game's own growing code); untamed boars (levels 2 and 1, "
                     + "one as if pregnant and due) keep their level over 6 breeding ticks: no birth, no note, no Debug line; the mod "
                     + $"patches only {string.Join(", ", ExpectedPatches)}");
        }
        finally
        {
            Override = null;
            foreach (var go in extra)
            {
                if (go != null && ZNetScene.instance != null)
                {
                    ZNetScene.instance.Destroy(go);
                }
            }
            Cleanup(pen);
        }
    }

    // ---------- breeding.toggle ----------

    private static void SetForceOff(bool off)
    {
        ForceOff = off;
        FeatureRegistry.RefreshAll();
    }

    private static IEnumerator RunToggle()
    {
        var pen = new Pen(ToggleName, egg: false);
        var c = new Checks(ToggleName);
        try
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                SelfTest.Fail(ToggleName, "no local player (the test needs a world)");
                yield break;
            }
            if (PenBlocked(ToggleName) || !SetupPen(pen, "Boar", 1, 3))
            {
                yield break;
            }
            yield return null;
            const string test = "self-test settings.";
            FarmerSkill.Reset();
            FarmerSkill.PublishNow();
            var ownFarming = FarmerSkill.OwnLevel(player);
            c.Check(MyActive() && SamePatches(OwnPatchedMethods()), $"the mod must be active with its patches before the test (state {MyState()})");
            c.Check(FarmerSkill.ReadPublished(player) == ownFarming, "the own Farming must be published before the test");

            // 1. On: a birth by the level-3 parent gives level 1.
            Override = Never;
            var w = Begin(c, pen, pen.B, pen.A, "on");
            End(c, pen, pen.B, w, "on", 1, PartnerSource.Recorded, 1, test, "own 3, partner 1 (recorded)");
            yield return null;

            // 2. Turned off live: withdraw, patches gone, vanilla births (the parent's own level), no Debug line.
            var mark = LogMark();
            SetForceOff(true);
            c.Check(!MyActive() && MyStatus() == ForceOffText, $"turned off: state {MyState()}, status '{MyStatus()}'");
            var patched = OwnPatchedMethods();
            c.Check(patched.Count == 0, $"turned off: the mod still patches [{string.Join(", ", patched.ToArray())}]");
            c.Check(FarmerSkill.ReadPublished(player) == FarmerSkill.NotTakingPart,
                $"turned off: the published Farming must be withdrawn (-1), found {Inv(FarmerSkill.ReadPublished(player))}");
            c.Check(CountLines(mark, WithdrewLine) == 1, $"turned off: one Debug line '{WithdrewLine}' expected");
            yield return null;
            foreach (var mother in new[] { pen.B, pen.B, pen.A })
            {
                var label = $"off, own {mother.GetLevel()}";
                w = Begin(c, pen, mother, Mate(pen, mother), label, noted: false);
                EndVanilla(c, pen, mother, w, label, mother.GetLevel());
            }
            c.Check(CountLines(mark, BirthLineStart) == 0 && CountLines(mark, ConceivedLineStart) == 0,
                "turned off: Debug birth or conception lines appeared");
            yield return null;

            // 3. Pregnancy started while off, then on: partner found at birth (1.5 m), never the level-3 own level.
            var pending = Begin(c, pen, pen.B, pen.A, "conceived while off", noted: false);
            mark = LogMark();
            SetForceOff(false);
            c.Check(MyActive(), $"turned on again: state {MyState()}, status '{MyStatus()}'");
            patched = OwnPatchedMethods();
            c.Check(SamePatches(patched), $"turned on again: the mod patches [{string.Join(", ", patched.ToArray())}]");
            c.Check(FarmerSkill.ReadPublished(player) == ownFarming,
                $"turned on again: the own Farming must be published at once ({Inv(FarmerSkill.ReadPublished(player))}, own {Inv(ownFarming)})");
            Override = Never; // turning off cleared it
            if (pending != null)
            {
                pending.Mark = mark;
                pending.Births0 = BirthCount;
                End(c, pen, pen.B, pending, "conceived while off, born while on", 1, PartnerSource.FoundAtBirth, 1, test,
                    "own 3, partner 1 (found at birth, 1.5 m)", "-> base 1;", "-> level 1;");
                c.Check(Close(LastBirth.PartnerDistance, MateGap), $"found at birth: partner distance {Inv(LastBirth.PartnerDistance)} m, expected {Inv(MateGap)}");
            }
            yield return null;

            // 4. Next pregnancy starts while on: recorded again, Debug lines are back, no restart.
            w = Begin(c, pen, pen.B, pen.A, "on again");
            End(c, pen, pen.B, w, "on again", 1, PartnerSource.Recorded, 1, test, "own 3, partner 1 (recorded)");
            yield return null;

            // 5. An animal the mod never saw (no note at all), pregnant: partner found at birth.
            var stranger = Spawn(pen, "Boar", pen.Center, 3);
            yield return null;
            if (c.Check(stranger != null, "could not spawn the third boar"))
            {
                Place(pen.B, pen.Center + pen.Side * 25f, pen.Forward);
                Place(stranger, pen.Center, pen.Forward);
                Place(pen.A, pen.Center + pen.Side * MateGap, pen.Forward);
                var zdo = stranger.m_nview.GetZDO();
                c.Check(BirthRecord.StoredStamp(zdo) == -1L, "the new boar must carry no note");
                zdo.Set(ZDOVars.s_pregnant, ZNet.instance.GetTime().Ticks);
                var watch = new Watch { Mark = LogMark(), Births0 = BirthCount, Own = 3, Stamp = zdo.GetLong(ZDOVars.s_pregnant, 0L) };
                End(c, pen, stranger, watch, "pregnancy from before the mod", 1, PartnerSource.FoundAtBirth, 1, test,
                    "own 3, partner 1 (found at birth, 1.5 m)");
                c.Check(BirthRecord.StoredStamp(zdo) == -1L, "a birth without a note must not write one");
                ZNetScene.instance.Destroy(stranger.gameObject);
            }
            yield return null;
            yield return null;

            // 6. Conceived while off, partner destroyed, on: no tamed partner within the pen range = own level.
            SetForceOff(true);
            pending = Begin(c, pen, pen.B, pen.A, "conceived while off (partner then destroyed)", noted: false);
            ZNetScene.instance.Destroy(pen.A.gameObject);
            yield return null;
            yield return null;
            c.Check(pen.A == null, "the partner is still there after it was destroyed");
            mark = LogMark();
            SetForceOff(false);
            Override = Never;
            c.Check(MyActive(), $"turned on a second time: state {MyState()}");
            if (pending != null)
            {
                pending.Mark = mark;
                pending.Births0 = BirthCount;
                // Base 3 = the cap of the test numbers (MaxStars 2): the line says so instead of a roll.
                End(c, pen, pen.B, pending, "no partner at birth", 3, PartnerSource.None, 0, test,
                    "own 3, partner none (no tamed partner within 10.0 m)", "-> base 3;", "-> level 3, at the cap: no roll;");
            }
            Override = null;
            c.Report($"{pen.Births} births while the mod was turned off and on live (Debug switch, same path as the MC Mods panel): off "
                     + "-> Farming withdrawn (-1), none of its patches left, births at the parent's own level (3, 3, 1), no note, no "
                     + "Debug line; on -> patches back, Farming published at once, pregnancy from the off time born with the partner "
                     + "found at birth (level 1), next one recorded again, an animal without any note -> found at birth, partner "
                     + "destroyed -> 'no tamed partner within 10.0 m' and the own level 3");
        }
        finally
        {
            if (ForceOff)
            {
                SetForceOff(false);
            }
            Override = null;
            Cleanup(pen);
        }
    }

    // ---------- breeding.respawn ----------

    // Real respawn (as after a death: start point or bed). Breeding ticks held: only the spawn patch can publish.
    // Ends in the frame the new character is first seen (caller checks before any other frame).
    private static IEnumerator Respawn(Box box)
    {
        box.Ok = false;
        box.Object = null;
        var old = Player.m_localPlayer;
        var game = Game.instance;
        if (old == null || game == null)
        {
            box.Detail = "no local player or no game";
            yield break;
        }
        FarmerSkill.HoldThrottle(600f);
        game.RequestRespawn(0f, true);
        var end = Time.realtimeSinceStartup + 40f;
        while (Time.realtimeSinceStartup < end)
        {
            var now = Player.m_localPlayer;
            if (now != null && !ReferenceEquals(now, old))
            {
                box.Ok = true;
                box.Object = now.gameObject;
                yield break;
            }
            yield return null;
        }
        box.Detail = "no new character within 40 s of the respawn request";
    }

    // After a respawn: god mode back (the probe turned it on), character back where it stood.
    private static IEnumerator AfterRespawn(Vector3 position, Quaternion rotation)
    {
        var end = Time.realtimeSinceStartup + 20f;
        while (Time.realtimeSinceStartup < end && (Player.m_localPlayer == null || Game.instance == null || Game.instance.WaitingForRespawn()))
        {
            yield return null;
        }
        var player = Player.m_localPlayer;
        if (player == null)
        {
            yield break;
        }
        player.SetGodMode(true);
        var distance = Vector3.Distance(player.transform.position, position);
        if (distance > 60f)
        {
            // Far: the game's own teleport (wait until it let go, then until it ended).
            var wait = Time.realtimeSinceStartup + 5f;
            while (!player.TeleportTo(position, rotation, true) && Time.realtimeSinceStartup < wait)
            {
                yield return null;
            }
            wait = Time.realtimeSinceStartup + 40f;
            while (player != null && player.IsTeleporting() && Time.realtimeSinceStartup < wait)
            {
                yield return null;
            }
        }
        else if (distance > 0.3f)
        {
            player.transform.position = position;
            player.transform.rotation = rotation;
            if (player.m_body != null)
            {
                player.m_body.position = position;
                player.m_body.rotation = rotation;
                player.m_body.linearVelocity = Vector3.zero;
            }
        }
        yield return null;
    }

    // New character stand up after a respawn (wake-up animation = cutscene for the game). While that last the game
    // close the inventory window every frame, so its slots never show the new character's items. Me wait until the
    // character is alive, not in cutscene, not teleporting for half a second in a row. Bounded: box.Ok false = never.
    private static IEnumerator WaitPlayerFree(Box box, float seconds)
    {
        box.Ok = false;
        var end = Time.realtimeSinceStartup + seconds;
        var freeSince = -1f;
        while (Time.realtimeSinceStartup < end)
        {
            var player = Player.m_localPlayer;
            var free = player != null && !player.IsDead() && !player.InCutscene() && !player.IsTeleporting();
            if (!free)
            {
                freeSince = -1f;
            }
            else if (freeSince < 0f)
            {
                freeSince = Time.realtimeSinceStartup;
            }
            else if (Time.realtimeSinceStartup - freeSince >= 0.5f)
            {
                box.Ok = true;
                yield break;
            }
            yield return null;
        }
        box.Detail = $"the character was still dead, in a cutscene (wake-up after a respawn) or teleporting after {Inv(seconds)} s";
    }

    // Respawn of a test give "no skill drain" effect like a death: me take it off again when it was not there before.
    private static void DropSoftDeath(Player player, bool hadBefore)
    {
        if (hadBefore || player == null)
        {
            return;
        }
        var seman = player.GetSEMan();
        if (seman != null && seman.HaveStatusEffect(SEMan.s_statusEffectSoftDeath))
        {
            seman.RemoveStatusEffect(SEMan.s_statusEffectSoftDeath, true);
        }
    }

    private static bool HasSoftDeath(Player player)
    {
        var seman = player != null ? player.GetSEMan() : null;
        return seman != null && seman.HaveStatusEffect(SEMan.s_statusEffectSoftDeath);
    }

    private static IEnumerator RunRespawn()
    {
        var c = new Checks(RespawnName);
        var first = Player.m_localPlayer;
        if (first == null || Game.instance == null)
        {
            SelfTest.Fail(RespawnName, "no local player (the test needs a world)");
            yield break;
        }
        var position = first.transform.position;
        var rotation = first.transform.rotation;
        var skillsBefore = SkillSnapshot.Take(first);
        var hadSoftDeath = HasSoftDeath(first);
        try
        {
            c.Check(MyActive() && OwnPatchedMethods().Contains("Player.OnSpawned"), "the mod must be active with its Player.OnSpawned patch");
            var levels = new[] { 37f, 20f }; // second one lower: like the skill drain of a death
            var seen = new List<string>();
            foreach (var level in levels)
            {
                var before = Player.m_localPlayer;
                SetFarming(before, level);
                var mark = LogMark();
                var box = new Box();
                yield return Respawn(box);
                if (!c.Check(box.Ok, $"respawn with Farming {Pct(level)}: {box.Detail}"))
                {
                    break;
                }
                // Same frame the new character is first seen; breeding ticks could not publish (throttle held).
                var now = Player.m_localPlayer;
                var own = FarmerSkill.OwnLevel(now);
                var published = FarmerSkill.ReadPublished(now);
                c.Check(!ReferenceEquals(now, before) && own == level, $"the new character must have Farming {Pct(level)} (found {Inv(own)})");
                c.Check(published == level, $"the new character must carry the published Farming {Pct(level)} at once (found {Inv(published)})");
                c.Check(CountLines(mark, $"Published Farming level {Pct(level)} for other players.") == 1,
                    $"one Debug line 'Published Farming level {Pct(level)} for other players.' expected when the character appeared");
                seen.Add($"{Pct(level)} -> published {Inv(published)}");
                yield return AfterRespawn(position, rotation);
            }
            // Next test (mine or other mod's) get a character that stand and can open its inventory.
            var free = new Box();
            yield return WaitPlayerFree(free, 30f);
            if (!free.Ok)
            {
                c.Note($"after the respawns {free.Detail}: a test that opens the inventory right after this one may fail");
            }
            c.Report($"{seen.Count} real respawns (breeding ticks kept from publishing): the Farming level of the new character was on its "
                     + $"player object in the frame it appeared, with the Debug publish line ({string.Join("; ", seen.ToArray())})");
        }
        finally
        {
            var now = Player.m_localPlayer;
            if (now != null)
            {
                skillsBefore.Restore(now);
                now.SetGodMode(true);
                DropSoftDeath(now, hadSoftDeath);
            }
            FarmerSkill.Reset();
            FarmerSkill.PublishNow();
        }
    }

    // ---------- breeding.eggstack ----------

    private static IEnumerator RunEggStack()
    {
        // Test before may have respawned the character: me wait until it stand (else game shut the inventory window).
        var free = new Box();
        yield return WaitPlayerFree(free, 30f);
        if (!free.Ok)
        {
            SelfTest.Fail(EggStackName, $"{free.Detail}: the inventory window cannot be opened");
            yield break;
        }
        var c = new Checks(EggStackName);
        var player = Player.m_localPlayer;
        var scene = ZNetScene.instance;
        var gui = InventoryGui.instance;
        if (player == null || scene == null || gui == null)
        {
            SelfTest.Fail(EggStackName, "no local player, world or inventory window");
            yield break;
        }
        var prefab = scene.GetPrefab("ChickenEgg");
        if (prefab == null || prefab.GetComponent<ItemDrop>() == null)
        {
            SelfTest.Fail(EggStackName, "prefab ChickenEgg not found or not an item");
            yield break;
        }
        var inventory = player.GetInventory();
        var drops = new List<GameObject>();
        var before = new List<ItemDrop.ItemData>(inventory.GetAllItems());
        var knownMaterial = new HashSet<string>(player.m_knownMaterial);
        var knownRecipes = new HashSet<string>(player.m_knownRecipes);
        try
        {
            if (inventory.GetEmptySlots() < 2)
            {
                SelfTest.Fail(EggStackName, "the test character needs two free inventory slots");
                yield break;
            }
            var forward = player.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward;
            var side = Vector3.Cross(Vector3.up, forward);
            var spot = Ground(player.transform.position + forward * 4f) + Vector3.up * 0.5f;

            // Like a laid egg: prefab made, quality set on the drop (what the game's breeding code does).
            ItemDrop Lay(Vector3 at, int quality)
            {
                var go = Object.Instantiate(prefab, at, Quaternion.identity);
                drops.Add(go);
                var drop = go.GetComponent<ItemDrop>();
                drop.m_autoPickup = false; // me pick up myself
                if (quality > 1)
                {
                    drop.SetQuality(quality);
                }
                return drop;
            }

            var plain = Lay(spot - side * 0.5f, 1);
            var star = Lay(spot + side * 0.5f, 2);
            var star2 = Lay(spot + forward * 0.7f, 2);
            yield return null;
            yield return null;
            var shared = plain.m_itemData.m_shared;
            var maxQuality = shared.m_maxQuality;
            var eggName = shared.m_name;
            var plainHover = FirstLine(plain.GetHoverText());
            var starHover = FirstLine(star.GetHoverText());
            c.Check(plainHover.IndexOf('[') < 0, $"the ground hover of a quality-1 egg must show no number, got '{plainHover}'");
            c.Check(starHover.IndexOf("[2]", StringComparison.Ordinal) >= 0, $"the ground hover of a quality-2 egg must show [2], got '{starHover}'");

            // Pick up (the game's own pickup): quality 1 and quality 2 = two stacks; one more quality 2 joins its own.
            c.Check(player.Pickup(plain.gameObject, false, false), "could not pick up the quality-1 egg");
            c.Check(player.Pickup(star.gameObject, false, false), "could not pick up the quality-2 egg");
            c.Check(player.Pickup(star2.gameObject, false, false), "could not pick up the second quality-2 egg");
            var eggs = NewItems(inventory, before, eggName);
            ItemDrop.ItemData plainItem = null;
            ItemDrop.ItemData starItem = null;
            foreach (var egg in eggs)
            {
                if (egg.m_quality == 1)
                {
                    plainItem = egg;
                }
                else if (egg.m_quality == 2)
                {
                    starItem = egg;
                }
            }
            if (!c.Check(eggs.Count == 2 && plainItem != null && starItem != null && plainItem.m_stack == 1 && starItem.m_stack == 2,
                    $"picked up one quality-1 and two quality-2 eggs: expected two stacks (1 x quality 1, 2 x quality 2), found {DescribeStacks(eggs)}"))
            {
                c.Report("");
                yield break;
            }

            // What the inventory shows (observation: the README says it depends on the egg item). Window must be
            // really open and its slots must be this character's: game shut it at once while character in cutscene,
            // and slots then still belong to inventory shown last (older character's): a drop there lose the eggs.
            gui.Show(null);
            var grid = gui.m_playerGrid;
            var until = Time.realtimeSinceStartup + 5f;
            var shownSince = -1f;
            var open = false;
            while (Time.realtimeSinceStartup < until && !open)
            {
                yield return null;
                var showing = InventoryGui.IsVisible() && gui.m_animator.GetBool("visible") && ReferenceEquals(grid.GetInventory(), inventory);
                if (!showing)
                {
                    shownSince = -1f;
                }
                else if (shownSince < 0f)
                {
                    shownSince = Time.realtimeSinceStartup;
                }
                else
                {
                    open = Time.realtimeSinceStartup - shownSince >= 1f; // open animation done: screenshot shows it
                }
            }
            if (!c.Check(open, "the inventory window did not open and stay open for a second with this character's items "
                               + $"(visible {InventoryGui.IsVisible()}, slots of this character {ReferenceEquals(grid.GetInventory(), inventory)}, "
                               + $"in a cutscene {player.InCutscene()})"))
            {
                c.Report("");
                yield break;
            }
            var width = inventory.GetWidth();
            var plainSlot = grid.GetElement(plainItem.m_gridPos.x, plainItem.m_gridPos.y, width);
            var starSlot = grid.GetElement(starItem.m_gridPos.x, starItem.m_gridPos.y, width);
            var slotNumber = false;
            if (c.Check(plainSlot != null && starSlot != null && plainSlot.m_quality != null && starSlot.m_quality != null,
                    "the inventory window has no slot for the eggs"))
            {
                c.Check(plainSlot.m_used && starSlot.m_used && plainSlot.m_icon.enabled && starSlot.m_icon.enabled,
                    "the inventory window does not draw the two egg stacks in their slots");
                slotNumber = starSlot.m_quality.enabled;
                c.Check(starSlot.m_quality.enabled == (maxQuality > 1) && plainSlot.m_quality.enabled == (maxQuality > 1),
                    $"slot quality number shown {starSlot.m_quality.enabled}, the egg's max quality is {maxQuality} (the game shows it only above 1)");
                if (slotNumber)
                {
                    c.Check(starSlot.m_quality.text == "2", $"the slot of the quality-2 eggs shows '{starSlot.m_quality.text}', expected 2");
                }
            }
            var tooltip = starItem.GetTooltip();
            var tooltipLine = tooltip != null && tooltip.IndexOf("$item_quality", StringComparison.Ordinal) >= 0;
            c.Check(tooltipLine == (maxQuality > 1), $"tooltip quality line {tooltipLine}, the egg's max quality is {maxQuality}");
            SelfTest.Screenshot(EggStackName, "inventory");
            yield return null;
            yield return null;

            // Drag quality-2 stack onto quality-1 stack: game's own click code of the inventory window, one click on
            // the stack (whole stack go on cursor), one click on the other slot (stack dropped there).
            var total = plainItem.m_stack + starItem.m_stack;
            var target = plainItem.m_gridPos;
            c.Check(ReferenceEquals(grid.GetInventory(), inventory) && inventory.ContainsItem(starItem) && inventory.ContainsItem(plainItem),
                "drag: the inventory window must still show this character's two egg stacks");
            gui.OnSelectedItem(grid, starItem, starItem.m_gridPos, InventoryGrid.Modifier.Select);
            var held = gui.m_dragGo != null && ReferenceEquals(gui.m_dragItem, starItem) && gui.m_dragAmount == starItem.m_stack;
            if (c.Check(held, "drag: a click on the quality-2 stack did not put the whole stack on the cursor"))
            {
                gui.OnSelectedItem(grid, plainItem, target, InventoryGrid.Modifier.Select);
                c.Check(gui.m_dragGo == null, "drag: the stack is still on the cursor after the click on the quality-1 stack");
            }
            var after = NewItems(inventory, before, eggName);
            string drag;
            if (maxQuality > 1)
            {
                drag = "kept apart (swapped places)";
                c.Check(after.Count == 2, $"drag: an item with quality levels must stay in two stacks, found {DescribeStacks(after)}");
                // README "Good to know" tells players a drag may merge egg stacks: no longer true for this egg.
                c.Problem($"the egg now has quality levels (max quality {maxQuality}): the inventory keeps the stacks apart, so the "
                          + "README 'Good to know' warning about dragging eggs onto other stacks no longer matches; update it");
            }
            else
            {
                drag = "merged into one stack, the level of the dragged eggs is lost (game behaviour, as the README warns)";
                c.Check(after.Count == 1 && after[0].m_stack == total && after[0].m_quality == 1 && ReferenceEquals(after[0], plainItem),
                    $"drag: the game merges stacks of an item without quality levels; expected one stack of {total} (quality 1), found {DescribeStacks(after)}");
            }
            c.Note($"egg display: max quality {maxQuality}; ground hover '{plainHover}' / '{starHover}'; picked up -> two stacks; inventory "
                   + $"slot quality number shown: {(slotNumber ? "yes" : "no")}; tooltip quality line: {(tooltipLine ? "yes" : "no")}; "
                   + $"dragging the quality-2 stack onto the quality-1 stack: {drag}");
            c.Report($"egg and egg[2] picked up stay two stacks (a second egg[2] joins its own stack); slot number {(slotNumber ? "shown" : "not shown")}, "
                     + $"tooltip quality line {(tooltipLine ? "shown" : "not shown")}, drag: {drag} (max quality {maxQuality})");
        }
        finally
        {
            foreach (var item in NewItems(inventory, before, null))
            {
                inventory.RemoveItem(item);
            }
            if (InventoryGui.instance != null && InventoryGui.IsVisible())
            {
                InventoryGui.instance.Hide();
            }
            foreach (var go in drops)
            {
                if (go != null && ZNetScene.instance != null)
                {
                    ZNetScene.instance.Destroy(go);
                }
            }
            // Picking up an egg taught the character the item (and maybe recipes): put the lists back.
            var now = Player.m_localPlayer;
            if (now != null)
            {
                now.m_knownMaterial.RemoveWhere(name => !knownMaterial.Contains(name));
                now.m_knownRecipes.RemoveWhere(name => !knownRecipes.Contains(name));
            }
        }
    }

    // Items in the inventory that were not there before (by object), of this name (null = any).
    private static List<ItemDrop.ItemData> NewItems(Inventory inventory, List<ItemDrop.ItemData> before, string sharedName)
    {
        var result = new List<ItemDrop.ItemData>();
        foreach (var item in inventory.GetAllItems())
        {
            if (before.Contains(item) || (sharedName != null && item.m_shared.m_name != sharedName))
            {
                continue;
            }
            result.Add(item);
        }
        return result;
    }

    private static string DescribeStacks(List<ItemDrop.ItemData> items)
    {
        var parts = new List<string>();
        foreach (var item in items)
        {
            parts.Add($"{item.m_stack} x quality {item.m_quality}");
        }
        return parts.Count == 0 ? "no stack" : string.Join(", ", parts.ToArray());
    }

    // ---------- breeding.stars ----------

    private static IEnumerator RunStars()
    {
        var pen = new Pen(StarsName, egg: false);
        var c = new Checks(StarsName);
        try
        {
            if (PenBlocked(StarsName) || !SetupPen(pen, "Boar", 1, 3))
            {
                yield break;
            }
            yield return null;
            var babies = new List<Character>();

            void Keep(GameObject baby)
            {
                var character = baby != null ? baby.GetComponent<Character>() : null;
                if (character != null)
                {
                    babies.Add(character);
                }
            }

            Override = Never;
            var w = Begin(c, pen, pen.B, pen.A, "level 1");
            End(c, pen, pen.B, w, "level 1", 1, PartnerSource.Recorded, 1, out var baby1, true, null);
            Keep(baby1);
            Override = Always;
            w = Begin(c, pen, pen.B, pen.A, "level 2");
            End(c, pen, pen.B, w, "level 2", 2, PartnerSource.Recorded, 1, out var baby2, true, null);
            Keep(baby2);
            pen.A.SetLevel(3);
            w = Begin(c, pen, pen.B, pen.A, "level 3");
            End(c, pen, pen.B, w, "level 3", 3, PartnerSource.Recorded, 3, out var baby3, true, null);
            Keep(baby3);
            Override = null;
            if (!c.Check(babies.Count == 3, $"only {babies.Count} of 3 piglets to look at"))
            {
                c.Report("");
                yield break;
            }

            // Parents aside, piglets in a row in front of the camera. The creature HUD shows only what the player
            // looked at in the last minute: me set that timer like a look does.
            Place(pen.A, pen.Center + pen.Side * 4f + pen.Forward * 3f, pen.Forward);
            Place(pen.B, pen.Center - pen.Side * 4f + pen.Forward * 3f, pen.Forward);
            var hudMissing = 0;
            var until = Time.time + 1.5f;
            while (Time.time < until)
            {
                hudMissing = 0;
                for (var i = 0; i < babies.Count; i++)
                {
                    if (babies[i] == null)
                    {
                        continue;
                    }
                    Place(babies[i], Ground(pen.Center - pen.Forward * 1.5f + pen.Side * ((i - 1) * 1.3f)), -pen.Forward);
                    if (!LookAtHud(babies[i]))
                    {
                        hudMissing++;
                    }
                }
                yield return null;
            }
            var shown = new List<string>();
            foreach (var baby in babies)
            {
                if (!c.Check(baby != null, "a piglet vanished"))
                {
                    continue;
                }
                var level = baby.GetLevel();
                if (!c.Check(HudStars(baby, out var onScreen, out var star1, out var star2),
                        $"the creature HUD has no star marks for the level-{level} piglet"))
                {
                    continue;
                }
                c.Check(onScreen, $"level-{level} piglet: its health bar is not drawn on the screen (the screenshot would not show it)");
                c.Check(star1 == (level == 2) && star2 == (level == 3),
                    $"level-{level} piglet: 1-star mark {star1}, 2-star mark {star2}; expected {level == 2} / {level == 3}");
                shown.Add($"level {level}: {(star2 ? "2 stars" : star1 ? "1 star" : "no star")}");
            }
            SelfTest.Screenshot(StarsName, "piglet_star_hud");
            yield return null;
            yield return null;
            c.Report($"creature HUD star marks of three piglets born at levels 1, 2 and 3 ({string.Join("; ", shown.ToArray())}); "
                     + "screenshot piglet_star_hud");
        }
        finally
        {
            Override = null;
            Cleanup(pen);
        }
    }

    // Like the player looking at this creature: its HUD (name, health, stars) shows for the next minute.
    // False = no HUD entry yet (made by the game for creatures within 10 m).
    private static bool LookAtHud(Character character)
    {
        var hud = EnemyHud.instance;
        if (hud == null || character == null || !hud.m_huds.TryGetValue(character, out var data) || data == null)
        {
            return false;
        }
        data.m_hoverTimer = 0f;
        return true;
    }

    // What the creature HUD draw for this animal right now: health bar on the screen (HUD object and all above it
    // on, top of the animal inside the screen, in front of camera) and which star mark is drawn. False = game made
    // no HUD entry for it (or entry has no star marks).
    private static bool HudStars(Character character, out bool onScreen, out bool star1, out bool star2)
    {
        onScreen = false;
        star1 = false;
        star2 = false;
        var hud = EnemyHud.instance;
        if (hud == null || character == null || !hud.m_huds.TryGetValue(character, out var data) || data == null
            || data.m_gui == null || data.m_level2 == null || data.m_level3 == null)
        {
            return false;
        }
        star1 = data.m_level2.gameObject.activeInHierarchy;
        star2 = data.m_level3.gameObject.activeInHierarchy;
        var cam = Utils.GetMainCamera();
        if (data.m_gui.activeInHierarchy && cam != null)
        {
            var point = cam.WorldToScreenPoint(character.GetTopPoint());
            onScreen = point.z > 0f && point.x >= 0f && point.x <= Screen.width && point.y >= 0f && point.y <= Screen.height;
        }
        return true;
    }

    // ---------- breeding.farmer-range ----------

    // Other players exist only in multiplayer. Stand-in: a player object without network part (like the main-menu
    // preview) with a hand-made ZDO for the published value, alive inside ONE frame step only: no game code and no
    // other mod ever updates it. FindBest and one real birth see it like a loaded friend.
    private static IEnumerator RunFarmerRange()
    {
        var pen = new Pen(FarmerRangeName, egg: false);
        var c = new Checks(FarmerRangeName);
        var player = Player.m_localPlayer;
        SkillSnapshot skillsBefore = null;
        GameObject ghost = null;
        ZNetView ghostView = null;
        ZDO ghostZdo = null;
        try
        {
            if (player == null || Game.instance == null || Game.instance.m_playerPrefab == null || ZDOMan.instance == null)
            {
                SelfTest.Fail(FarmerRangeName, "no local player or no player prefab (the test needs a world)");
                yield break;
            }
            if (PenBlocked(FarmerRangeName) || !SetupPen(pen, "Boar", 1, 1))
            {
                yield break;
            }
            skillsBefore = SkillSnapshot.Take(player);
            yield return null;
            const string test = "self-test settings.";
            var key = FarmerSkill.Key.GetStableHashCode();
            var animal = pen.Center;
            var playersBefore = Player.GetAllPlayers().Count;

            ZNetView.m_forceDisableInit = true;
            try
            {
                ghost = Object.Instantiate(Game.instance.m_playerPrefab, animal + pen.Side * 5f, Quaternion.identity);
            }
            finally
            {
                ZNetView.m_forceDisableInit = false;
            }
            var friend = ghost.GetComponent<Player>();
            ghostView = friend != null ? friend.m_nview : null;
            if (friend == null || ghostView == null)
            {
                SelfTest.Fail(FarmerRangeName, "could not make the stand-in player");
                yield break;
            }
            // Far away: no zone of the loaded area ever lists this ZDO.
            ghostZdo = ZDOMan.instance.CreateNewZDO(animal + new Vector3(3000f, 0f, 3000f), 0);
            ghostZdo.Set(ZDOVars.s_playerName, "Friend");
            ghostView.m_zdo = ghostZdo;
            c.Check(Player.GetAllPlayers().Count == playersBefore + 1 && !ReferenceEquals(friend, Player.m_localPlayer),
                "the stand-in must be a second loaded player, not the local one");

            void At(float metres)
            {
                friend.transform.position = animal + pen.Side * metres;
            }

            // (label, friend's published value or null = no key, distance, range, own Farming) -> who counts.
            void Expect(string label, float? published, float metres, float range, float ownFarming, bool expectFriend)
            {
                SetFarming(player, ownFarming);
                if (published.HasValue)
                {
                    ghostZdo.Set(key, published.Value);
                }
                At(metres);
                var known = FarmerSkill.FindBest(animal, range, out var farmer, out var level, out var local);
                var wantLevel = expectFriend ? published ?? 0f : ownFarming;
                c.Check(known && local == !expectFriend && ReferenceEquals(farmer, expectFriend ? friend : player) && level == wantLevel,
                    $"{label}: best farmer {(local ? "the own character" : "the other player")} with Farming {Inv(level)}; expected "
                    + $"{(expectFriend ? "the other player" : "the own character")} with Farming {Inv(wantLevel)}");
            }

            Expect("other player 5 m away without a published value (no mod there)", null, 5f, 60f, 0f, false);
            Expect("other player 5 m away, Farming 80 published, range 60", 80f, 5f, 60f, 0f, true);
            Expect("other player 20 m away, range 10 (out of range)", 80f, 20f, 10f, 0f, false);
            Expect("other player 20 m away, range 40 (in range again)", 80f, 20f, 40f, 0f, true);
            Expect("other player just outside the range (10.1 m, range 10)", 80f, 10.1f, 10f, 0f, false);
            Expect("other player just inside the range (9.9 m, range 10)", 80f, 9.9f, 10f, 0f, true);
            Expect("other player withdrew (-1, mod turned off there)", FarmerSkill.NotTakingPart, 5f, 60f, 0f, false);
            Expect("other player Farming 80, own Farming 90: the best one counts", 80f, 5f, 60f, 90f, false);
            Expect("other player Farming 100, own Farming 90: the best one counts", 100f, 5f, 60f, 90f, true);
            Expect("same Farming 50 on both: the own character wins the tie", 50f, 5f, 60f, 50f, false);
            Expect("other player Farming 0 published, own Farming 0: the own character wins the tie", 0f, 5f, 60f, 0f, false);

            // A real birth 3 m from the other player (every FarmerRange setting is 5 m or more).
            Override = new RuleSettings(0f, 100f, 0f, 2);
            SetFarming(player, 0f);
            ghostZdo.Set(key, 100f);
            At(3f);
            {
                var label = "birth with the other player (Farming 100) 3 m away";
                var w = Begin(c, pen, pen.B, pen.A, label);
                At(3f);
                End(c, pen, pen.B, w, label, 2, PartnerSource.Recorded, 1, test,
                    "farmer Friend Farming 100 (published) -> chance 100%;", "-> level 2, extra star;");
                var b = LastBirth;
                c.Check(b.FarmerKnown && !b.FarmerLocal && b.FarmerName == "Friend" && b.FarmingLevel == 100f,
                    $"{label}: farmer '{b.FarmerName}' (own {b.FarmerLocal}) with Farming {Inv(b.FarmingLevel)}");
            }
            ghostZdo.Set(key, FarmerSkill.NotTakingPart);
            {
                var label = "birth after the other player withdrew";
                var w = Begin(c, pen, pen.B, pen.A, label);
                At(3f);
                End(c, pen, pen.B, w, label, 1, PartnerSource.Recorded, 1, test, "farmer you Farming 0 (own) -> chance 0%;");
                c.Check(LastBirth.FarmerLocal, $"{label}: the own character must be the farmer");
            }
            Override = null;
            c.Report("another player's published Farming (stand-in player object, never simulated): counts only with a published value "
                     + "of 0 or more and inside the range (5 m of 60, 20 m of 40, 9.9 m of 10; not 20 m of 10, not 10.1 m of 10, "
                     + "not after a withdraw), the best Farming wins and the own character wins a tie; a real birth names "
                     + "'farmer Friend Farming 100 (published)' and falls back to the own character after the withdraw");
        }
        finally
        {
            Override = null;
            if (ghostView != null)
            {
                ghostView.m_zdo = null;
            }
            if (ghost != null)
            {
                Object.DestroyImmediate(ghost);
            }
            if (ghostZdo != null && ZDOMan.instance != null)
            {
                ZDOMan.instance.DestroyZDO(ghostZdo);
            }
            var now = Player.m_localPlayer;
            if (skillsBefore != null && now != null)
            {
                skillsBefore.Restore(now);
            }
            FarmerSkill.Reset();
            FarmerSkill.PublishNow();
            Cleanup(pen);
        }
    }

    // ---------- breeding.compat ----------

    // Stand-in for a mod (Seasons in winter, a breeding mod) whose prefix skips the game's breeding code.
    private static bool SkipProcreateForTest()
    {
        return false;
    }

    private static IEnumerator RunCompat()
    {
        var pen = new Pen(CompatName, egg: false);
        var c = new Checks(CompatName);
        Harmony other = null;
        try
        {
            if (PenBlocked(CompatName) || !SetupPen(pen, "Boar", 1, 3))
            {
                yield break;
            }
            yield return null;
            const string test = "self-test settings.";
            const string standAside = "Star Level System is installed: it decides breeding levels, so Breeding Star Inheritance leaves births to it.";
            const string skipped = "Another mod skipped the game's breeding code for a birth that was due; Breeding Star Inheritance leaves that call alone.";
            Override = Never;

            // 1. Star Level System (switch says "installed"): one Info line, births left alone (the game's own rule).
            var mark = LogMark();
            Compat.TestStarLevelSystem = true;
            Compat.Reset();
            for (var i = 0; i < 2; i++)
            {
                var label = $"Star Level System, birth {i + 1}";
                var w = Begin(c, pen, pen.B, pen.A, label, noted: false);
                EndVanilla(c, pen, pen.B, w, label, 3);
            }
            c.Check(CountLines(mark, standAside) == 1, $"Star Level System: the Info line must appear exactly once, found {CountLines(mark, standAside)}");
            Compat.TestStarLevelSystem = null;
            Compat.Reset();
            {
                var label = "after Star Level System";
                var w = Begin(c, pen, pen.B, pen.A, label);
                End(c, pen, pen.B, w, label, 1, PartnerSource.Recorded, 1, test, "own 3, partner 1 (recorded)");
            }
            yield return null;

            // 2. Another mod's prefix skips the game's breeding code on a due birth: one Debug line, nothing else.
            ProcreationPatches.ClearStandAsideLog();
            var pending = Begin(c, pen, pen.B, pen.A, "skipped by another mod");
            if (pending != null)
            {
                var procB = pen.B.GetComponent<Procreation>();
                var target = AccessTools.Method(typeof(Procreation), nameof(Procreation.Procreate));
                var prefix = typeof(SelfTests).GetMethod(nameof(SkipProcreateForTest), BindingFlags.Static | BindingFlags.NonPublic);
                other = new Harmony(ModInfo.Guid + ".selftest");
                other.Patch(target, prefix: new HarmonyMethod(prefix) { priority = Priority.First });
                mark = LogMark();
                for (var i = 0; i < 3; i++)
                {
                    Ready(pen.B);
                    procB.Procreate();
                }
                other.UnpatchSelf();
                other = null;
                c.Check(procB.m_nview.GetZDO().GetLong(ZDOVars.s_pregnant, 0L) == pending.Stamp && BirthCount == pending.Births0,
                    "skipped by another mod: a birth happened although the game's breeding code was skipped");
                c.Check(pen.B.GetLevel() == 3, $"skipped by another mod: the parent is level {pen.B.GetLevel()}, was 3");
                c.Check(CountLines(mark, skipped) == 1, $"skipped by another mod: the Debug line must appear exactly once in 3 skipped calls, found {CountLines(mark, skipped)}");
                c.Check(CountLines(mark, BirthLineStart) == 0, "skipped by another mod: a Debug birth line appeared");
                // The other mod lets go: the birth happens with the note from the conception.
                End(c, pen, pen.B, pending, "after the other mod let go", 1, PartnerSource.Recorded, 1, test, "own 3, partner 1 (recorded)");
            }
            Override = null;
            c.Report("stand aside: with Star Level System (Debug switch) one Info line, births at the parent's own level, no note, no "
                     + "Debug birth line, back to the mod's rule after; with a prefix of another mod skipping the game's breeding code "
                     + "one Debug line in 3 skipped calls, no birth, parent level kept, and the birth follows the rule once the other "
                     + "mod lets go");
        }
        finally
        {
            if (other != null)
            {
                other.UnpatchSelf();
            }
            Compat.TestStarLevelSystem = null;
            Compat.Reset();
            ProcreationPatches.ClearStandAsideLog();
            Override = null;
            Cleanup(pen);
        }
    }

    // ---------- breeding.setup ----------

    // TESTING.md Setup tell the tester how a pen must look and how long things take (partner range, population, wait
    // times). T18 say: NOTE values differ from the Setup = update the Setup. Me compare, so nobody has to read NOTEs:
    // game update changed a value = this test fail and name it.
    private static void CheckSetupSpecies(Checks c, List<string> seen, string name, string offspring, float partnerRange,
        int maxCreatures)
    {
        var prefab = ZNetScene.instance.GetPrefab(name);
        var proc = prefab != null ? prefab.GetComponent<Procreation>() : null;
        if (!c.Check(proc != null, $"prefab {name} not found or it has no Procreation"))
        {
            return;
        }
        var tameable = prefab.GetComponent<Tameable>();
        var born = proc.m_offspring != null ? Utils.GetPrefabName(proc.m_offspring) : "none";
        var fed = tameable != null ? Inv(tameable.m_fedDuration) : "?";
        c.Check(born == offspring, $"{name}: offspring {born}, the Setup says {offspring}");
        c.Check(Close(proc.m_partnerCheckRange, partnerRange),
            $"{name}: partner range {Inv(proc.m_partnerCheckRange)} m, the Setup says {Inv(partnerRange)}");
        c.Check(proc.m_maxCreatures == maxCreatures && Close(proc.m_totalCheckRange, 10f),
            $"{name}: population {proc.m_maxCreatures} within {Inv(proc.m_totalCheckRange)} m, the Setup says {maxCreatures} within 10");
        c.Check(Close(proc.m_pregnancyDuration, 60f), $"{name}: pregnancy {Inv(proc.m_pregnancyDuration)} s, the Setup says 60");
        c.Check(Close(proc.m_updateInterval, 30f), $"{name}: a breeding tick every {Inv(proc.m_updateInterval)} s, the Setup says 30");
        c.Check(proc.m_requiredLovePoints == 3, $"{name}: {proc.m_requiredLovePoints} love points, the Setup says 3");
        c.Check(tameable != null && Close(tameable.m_fedDuration, 600f), $"{name}: fed for {fed} s, the Setup says 600");
        seen.Add($"{name} -> {born}, partner range {Inv(proc.m_partnerCheckRange)} m, population {proc.m_maxCreatures} within "
                 + $"{Inv(proc.m_totalCheckRange)} m, pregnancy {Inv(proc.m_pregnancyDuration)} s, tick {Inv(proc.m_updateInterval)} s, "
                 + $"{proc.m_requiredLovePoints} love points, fed {fed} s");
    }

    private static IEnumerator RunSetup()
    {
        var c = new Checks(SetupName);
        if (ZNetScene.instance == null)
        {
            SelfTest.Fail(SetupName, "no world (the prefabs are read from it)");
            yield break;
        }
        var seen = new List<string>();
        CheckSetupSpecies(c, seen, "Boar", "Boar_piggy", 3f, 5);
        CheckSetupSpecies(c, seen, "Hen", "ChickenEgg", 4f, 10);
        var eggPrefab = ZNetScene.instance.GetPrefab("ChickenEgg");
        var grow = eggPrefab != null ? eggPrefab.GetComponent<EggGrow>() : null;
        if (c.Check(grow != null && grow.m_grownPrefab != null, "prefab ChickenEgg not found or it has no EggGrow with a grown prefab"))
        {
            c.Check(grow.m_grownPrefab.name == "Chicken" && grow.m_tamed,
                $"ChickenEgg hatches into {grow.m_grownPrefab.name} (tamed {grow.m_tamed}), the Setup says a tamed Chicken");
            seen.Add($"ChickenEgg hatches into a {(grow.m_tamed ? "tamed" : "wild")} {grow.m_grownPrefab.name}");
        }
        yield return null;
        c.Report("the breeding values of the prefabs are the ones the TESTING.md Setup gives: " + string.Join("; ", seen.ToArray()));
    }

    // ---------- breeding.log ----------

    private static IEnumerator RunLog()
    {
        var lines = LogSince(0);
        var troubles = Troubles(lines);
        yield return null;
        if (troubles.Count == 0)
        {
            SelfTest.Pass(LogName, $"no warning or error line from {ModInfo.Name} among the {lines.Count} lines it logged in this session "
                                   + "(since the mod was first turned on, the other breeding tests included)");
            yield break;
        }
        var shown = new List<string>();
        for (var i = 0; i < troubles.Count && i < 5; i++)
        {
            shown.Add($"[{troubles[i].Level}] {FirstLine(troubles[i].Text)}");
        }
        SelfTest.Fail(LogName, $"{troubles.Count} warning or error line(s) from {ModInfo.Name} in this session: {string.Join(" | ", shown.ToArray())}");
    }
}
#endif
