#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx.Bootstrap;
using MC.Shared;
using UnityEngine;
using ItemType = ItemDrop.ItemData.ItemType;
using Object = UnityEngine.Object;

namespace MC.Combat.ShieldsTowerWallMod;

// Debug build only. Self tests with other MC mods (every MC mod is loaded in a test run). The other mod is found by
// GUID in the shared registry and must be active, else the test fails (no pass without a check). Never a type of
// theirs at compile time: the game-level effect, or reflection on their assembly (read doors and their own test
// switches only).
//   tower.x.dual        C05 Dual Wielding: tower shield against a pair, a weapon against the tower shield
//   tower.x.moveset     C04 Weapon Moveset: attack in a jump and attack held from the second half of a roll = the bash
//   tower.x.sneak       C08 Sneak Ambush: raising the tower ends the crouch; bash on an unaware Greydwarf = no backstab,
//                       no Sneak XP; NG+: knife after the bash = no backstab, no Sneak XP (control: knife alone pays)
//   tower.x.kills       C10 Creature Kill and Tame Counts: Greyling killed with bashes only (god mode off) = its line
//                       gains one kill, one melee kill
//   tower.x.spyglass    C11 Spyglass, tower.x.instrument C11 Music Instruments: press kept for the bash cooldown, then
//                       their item drawn: nothing swings by itself, attack button with it = no attack, put away = no punch
//   tower.input.spyglass, tower.input.instrument   C11: a real left click with their item still does its own thing
//   tower.x.forge       C03 Forge Idol Upgrades: a tower shield refined at the Forge of Potential stays two-handed with
//                       block armor of its new level x BlockArmorMultiplier
//   tower.x.sort        C01 Sort Chest: its sort (by type) of a real chest puts tower shields next to round shields
//   tower.x.kinds       C01: Sort Chest's, Crafting Search and Sort's and Encyclopedia's own item classifier call an
//                       applied tower shield a shield
//   tower.x.harpoon     C09 Harpoon Hooks Tames: a real harpoon throw hooks a tame Boar without damage; tower shield
//                       drawn, bash staggers a wild Greydwarf, the Boar still unhurt
internal static partial class SelfTests
{
    private const string DualName = "tower.x.dual";
    private const string MovesetName = "tower.x.moveset";
    private const string SneakName = "tower.x.sneak";
    private const string KillsName = "tower.x.kills";
    private const string SpyglassName = "tower.x.spyglass";
    private const string InstrumentName = "tower.x.instrument";
    private const string InputSpyglassName = "tower.input.spyglass";
    private const string InputInstrumentName = "tower.input.instrument";
    private const string ForgeName = "tower.x.forge";
    private const string SortName = "tower.x.sort";
    private const string KindsName = "tower.x.kinds";
    private const string HarpoonName = "tower.x.harpoon";

    private const string DualGuid = "MC.Combat.Weapons.DualWield";
    private const string MovesetGuid = "MC.Combat.Weapons.Moveset";
    private const string SneakGuid = "MC.Combat.Sneak.Ambush";
    private const string StatsGuid = "MC.Exploration.Stats.PerCreature";
    private const string SpyglassGuid = "MC.Exploration.View.Spyglass";
    private const string MusicGuid = "MC.Exploration.Music.Instruments";
    private const string ForgeGuid = "MC.Crafting.Forge.IdolUpgrades";
    private const string SortGuid = "MC.UX.Container.Sort";
    private const string CraftListGuid = "MC.UX.Crafting.SearchSort";
    private const string EncyclopediaGuid = "MC.Exploration.Compendium.Encyclopedia";
    private const string HarpoonGuid = "MC.Farming.Harpoon.HooksTames";

    private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    // ---------- other mod helpers ----------

    private static bool OtherActive(Checks c, string guid, string name, string item)
    {
        var view = FeatureRegistry.Find(guid);
        var ok = view != null && view.Value.IsActive;
        c.Check(ok, $"{name} ({guid}) is loaded and active ({(view == null ? "not installed" : view.Value.State)}): without it TESTING {item} was not checked");
        return ok;
    }

    // Type of another plugin by its short name (their namespace is their business). Null = plugin or type missing.
    private static Type OtherType(string guid, string typeName)
    {
        if (!Chainloader.PluginInfos.TryGetValue(guid, out var info) || info == null || info.Instance == null)
        {
            return null;
        }
        Type[] types;
        try
        {
            types = info.Instance.GetType().Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            types = e.Types;
        }
        return types.FirstOrDefault(t => t != null && t.Name == typeName);
    }

    // Static bool property or field of their type. Null = not there.
    private static bool? OtherBool(Type type, string member)
    {
        if (type == null)
        {
            return null;
        }
        try
        {
            var property = type.GetProperty(member, AnyStatic);
            if (property != null)
            {
                return property.GetValue(null, null) as bool?;
            }
            var field = type.GetField(member, AnyStatic);
            return field != null ? field.GetValue(null) as bool? : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool OtherCall(Type type, string method)
    {
        var m = type != null ? type.GetMethod(method, AnyStatic, null, Type.EmptyTypes, null) : null;
        if (m == null)
        {
            return false;
        }
        m.Invoke(null, null);
        return true;
    }

    private static string PrefabOf(ItemDrop.ItemData item) => item == null ? "nothing" : item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared.m_name;

    // One real swing of the weapon in hand at the creature put in front. landed = it took health from it.
    private static IEnumerator RealSwing(Player p, Character target, Box<bool> landed)
    {
        landed.Value = false;
        yield return WaitIdle(p);
        PlaceInFront(p, target);
        yield return Fixed;
        var health = target.GetHealth();
        var swung = p.StartAttack(null, false);
        var t0 = Time.time;
        while (swung && Time.time - t0 < 2.5f)
        {
            yield return null;
            if (target == null)
            {
                break;
            }
            if (target.GetHealth() < health - 0.001f)
            {
                landed.Value = true;
                break;
            }
        }
        yield return WaitIdle(p);
    }

    // ---------- tower.x.dual ----------

    private static IEnumerator RunDual()
    {
        var c = new Checks(DualName);
        var rig = new Rig(DualName);
        try
        {
            var p = rig.P;
            if (!OtherActive(c, DualGuid, "Dual Wielding", "C05"))
            {
                c.Report();
                yield break;
            }
            yield return UseRules(TowerRules.Defaults());
            var sword = rig.Give("SwordIron");
            var axe = rig.Give("AxeIron");
            var tower = rig.Give(IronTower);
            if (sword == null || axe == null || tower == null)
            {
                c.Check(false, "could not add SwordIron, AxeIron and ShieldIronTower");
                c.Report();
                yield break;
            }
            p.EquipItem(sword);
            p.EquipItem(axe);
            yield return FixedSteps(3);
            var paired = ReferenceEquals(p.m_rightItem, sword) && ReferenceEquals(p.m_leftItem, axe);
            c.Check(paired, $"two one-handed weapons are held as a pair, one in each hand ({Hands(p)})");
            p.EquipItem(tower);
            yield return FixedSteps(3);
            c.Check(Only(p, tower, left: true) && !sword.m_equipped && !axe.m_equipped && p.m_hiddenRightItem == null && p.m_hiddenLeftItem == null,
                $"the tower shield puts both weapons of the pair away ({Hands(p)}; sword equipped {sword.m_equipped}, axe equipped {axe.m_equipped})");
            c.Check(tower.m_shared.m_itemType == ItemType.TwoHandedWeaponLeft, $"and it is two-handed ({tower.m_shared.m_itemType})");
            p.EquipItem(sword);
            yield return FixedSteps(3);
            c.Check(Only(p, sword, left: false) && !tower.m_equipped, $"with the tower shield in hand, a one-handed weapon puts it away ({Hands(p)})");
            p.EquipItem(tower);
            yield return FixedSteps(3);
            p.EquipItem(axe);
            yield return FixedSteps(3);
            c.Check(axe.m_equipped && !tower.m_equipped && !ReferenceEquals(p.m_leftItem, tower) && !ReferenceEquals(p.m_hiddenLeftItem, tower),
                $"the same with the other weapon ({Hands(p)})");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- tower.x.moveset ----------

    private static IEnumerator RunMoveset()
    {
        var c = new Checks(MovesetName);
        var rig = new Rig(MovesetName);
        try
        {
            var p = rig.P;
            if (!OtherActive(c, MovesetGuid, "Weapon Moveset", "C04"))
            {
                c.Report();
                yield break;
            }
            var rules = TowerRules.Defaults();
            yield return UseRules(rules);
            var tower = rig.Give(IronTower);
            var troll = rig.Creature("Troll");
            if (tower == null || troll == null)
            {
                c.Check(false, "could not add ShieldIronTower or spawn a Troll");
                c.Report();
                yield break;
            }
            SetSkill(p, Skills.SkillType.Blocking, 0f);
            yield return FreshBash(p, tower);
            var trigger = BashAttack.TriggerOf(BashAttack.Mode, rules);

            // Jump, attack button held in the air.
            yield return WaitIdle(p);
            yield return WaitCooldown(p);
            FaceClearLane(p, Reach(p, troll), LaneFan, MovesetName, troll);
            PlaceInFront(p, troll);
            troll.m_staggerDamage = 0f;
            yield return Fixed;
            var starts = BashWatch.DebugStarts.Count;
            var events = BashWatch.DebugHitEvents;
            var before = p.m_currentAttack;
            p.Jump();
            yield return Until(() => !p.IsOnGround(), 1f);
            var jumped = !p.IsOnGround();
            SelfTestHooks.HoldAttack = true;
            yield return Until(() => p.m_currentAttack != null && !ReferenceEquals(p.m_currentAttack, before), 1f);
            var inAir = !p.IsOnGround();
            SelfTestHooks.HoldAttack = false;
            var jumpBash = p.m_currentAttack != null && !ReferenceEquals(p.m_currentAttack, before) ? p.m_currentAttack : null;
            c.Check(jumped && inAir && jumpBash != null, $"an attack starts while the player is in the air after a jump (jumped {jumped}, in the air at the start {inAir})");
            if (jumpBash != null)
            {
                c.Check(ReferenceEquals(jumpBash.GetWeapon(), tower) && jumpBash.m_attackAnimation == trigger && Near(jumpBash.m_staggerMultiplier, rules.BashStagger)
                        && ReferenceEquals(BashWatch.Running, jumpBash),
                    $"attack in a jump: the tower shield's own bash, no jump attack ('{jumpBash.m_attackAnimation}', stagger x{F(jumpBash.m_staggerMultiplier)}, weapon {PrefabOf(jumpBash.GetWeapon())})");
                var skill = p.GetSkillFactor(Skills.SkillType.Blocking);
                yield return Until(() => BashWatch.DebugHitEvents > events, 4f);
                var s = new Swing { Skill = skill, Drain = Drain(troll) };
                if (BashWatch.DebugHitEvents > events)
                {
                    yield return null;
                    s.Events = BashWatch.DebugHitEvents - events;
                    s.Gained = troll.m_staggerDamage + s.Drain * (Time.time - BashWatch.DebugFirstHitTime);
                }
                c.Check(s.Events == 1, $"its hit comes ({s.Events} hit event(s))");
                if (s.Events > 0)
                {
                    CheckHit(c, "bash in a jump: its own stagger on the Troll", s, TowerCatalog.TowerOf(tower).BashDamage, rules.BashStagger, 1f, -1f);
                }
                yield return Until(() => jumpBash.IsDone() || !ReferenceEquals(p.m_currentAttack, jumpBash), 4f);
            }
            yield return Until(() => p.IsOnGround(), 3f);
            yield return WaitIdle(p);
            yield return Seconds(0.3f);
            c.Check(BashWatch.DebugStarts.Count == starts + (jumpBash != null ? 1 : 0), $"one bash, nothing else started ({BashWatch.DebugStarts.Count - starts} bash start(s))");

            // Roll, attack button held from the second half of the roll on.
            yield return WaitCooldown(p);
            Park(p, troll);
            FaceClearLane(p, 6f, 20f, MovesetName, null);
            yield return Fixed;
            starts = BashWatch.DebugStarts.Count;
            before = p.m_currentAttack;
            p.Dodge(Flat(p.transform.forward));
            yield return Until(() => p.InDodge(), 1f);
            var rolled = p.InDodge();
            var anim = p.m_animator;
            yield return Until(() =>
            {
                if (!p.InDodge())
                {
                    return true;
                }
                var info = anim.GetCurrentAnimatorStateInfo(0);
                return info.tagHash == Player.s_animatorTagDodge && info.normalizedTime >= 0.5f;
            }, 2f);
            var pressedInRoll = p.InDodge();
            SelfTestHooks.HoldAttack = true;
            Attack rollBash = null;
            var startedInRoll = false;
            var rollEnd = -1f;
            var startAt = -1f;
            var t0 = Time.time;
            while (Time.time - t0 < 3f && rollBash == null)
            {
                yield return Fixed;
                var rolling = p.InDodge();
                if (rollEnd < 0f && !rolling)
                {
                    rollEnd = Time.time - t0;
                }
                var now = p.m_currentAttack;
                if (now != null && !ReferenceEquals(now, before))
                {
                    rollBash = now;
                    startedInRoll = rolling;
                    startAt = Time.time - t0;
                }
            }
            SelfTestHooks.HoldAttack = false;
            c.Check(rolled && pressedInRoll, $"the attack button goes down in the second half of a roll (rolled {rolled}, still rolling at the press {pressedInRoll})");
            c.Check(rollBash != null && !startedInRoll && rollEnd >= 0f && startAt >= rollEnd,
                $"the attack starts only once the roll is over, it never cuts into the roll (roll over {T(rollEnd)} after the press, attack started {T(startAt)} after it, in the roll {startedInRoll})");
            if (rollBash != null)
            {
                c.Check(ReferenceEquals(rollBash.GetWeapon(), tower) && rollBash.m_attackAnimation == trigger && Near(rollBash.m_staggerMultiplier, rules.BashStagger),
                    $"attack after a roll: the tower shield's own bash, no roll attack ('{rollBash.m_attackAnimation}', stagger x{F(rollBash.m_staggerMultiplier)}, weapon {PrefabOf(rollBash.GetWeapon())})");
                yield return Until(() => rollBash.IsDone() || !ReferenceEquals(p.m_currentAttack, rollBash), 4f);
            }
            yield return WaitIdle(p);
            c.Check(BashWatch.DebugStarts.Count == starts + (rollBash != null ? 1 : 0), $"one bash after the roll, nothing else started ({BashWatch.DebugStarts.Count - starts} bash start(s))");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- tower.x.sneak ----------

    private static IEnumerator RunSneak()
    {
        var c = new Checks(SneakName);
        var rig = new Rig(SneakName);
        try
        {
            var p = rig.P;
            if (!OtherActive(c, SneakGuid, "Sneak Ambush", "C08"))
            {
                c.Report();
                yield break;
            }
            var rules = TowerRules.Defaults();
            yield return UseRules(rules);
            var tower = rig.Give(IronTower);
            var knife = rig.Give("KnifeCopper");
            var grey = rig.Creature("Greydwarf");
            if (tower == null || knife == null || grey == null)
            {
                c.Check(false, "could not add ShieldIronTower and KnifeCopper or spawn a Greydwarf");
                c.Report();
                yield break;
            }
            Park(p, grey);
            SetSkill(p, Skills.SkillType.Blocking, 0f);
            yield return FreshBash(p, tower);

            // Raising the tower shield ends the crouch.
            yield return Crouch(p, true);
            var crouched = p.IsCrouching();
            yield return Guard(p, true);
            yield return Until(() => !p.IsCrouching() && !p.m_crouchToggled, 2f);
            c.Check(crouched && !p.IsCrouching() && !p.m_crouchToggled, $"crouched, then the tower shield raised: the crouch ends (crouched before {crouched}, after {p.IsCrouching()})");
            yield return Guard(p, false);

            // Crouched, bash on a Greydwarf that has not noticed anything: no backstab, no Sneak XP.
            var ai = grey.GetComponent<MonsterAI>();
            yield return Crouch(p, true);
            c.Check(p.IsCrouching(), "the player is crouched again for the bash");
            c.Check(ai != null && !ai.IsAlerted(), "the Greydwarf is unaware before the bash");
            var sneak = Trained(p, Skills.SkillType.Sneak);
            var stab = grey.m_backstabTime;
            var s = new Swing();
            yield return Press(p, grey, s, SneakName, null);
            yield return Seconds(0.3f);
            SelfTest.Note(SneakName, Describe("bash on the unaware Greydwarf", s));
            c.Check(s.Landed, "the bash lands on the unaware Greydwarf");
            c.Check(Near(grey.m_backstabTime, stab), "the bash is no sneak attack (the Greydwarf's backstab time did not move)");
            c.Check(s.Lost <= TowerCatalog.TowerOf(tower).BashDamage * BluntTaken(grey) + 0.2f, $"its damage is a plain bash's, no sneak-attack bonus (health -{F(s.Lost)})");
            c.Check(Trained(p, Skills.SkillType.Sneak) == sneak, "the bash gives no Sneak XP");
            p.SetCrouch(false);
            ZNetScene.instance.Destroy(grey.gameObject);

            // New Game+ (world level 1). Control: a knife hit on an unaware Greydwarf is a sneak attack and pays Sneak XP
            // (Sneak Ambush). Then: bash an unaware one, knife at once: no sneak attack, no Sneak XP (the bash alerted it).
            rig.SetWorldLevel(1);
            yield return null;
            c.Check(Game.m_worldLevel == 1, $"world level {Game.m_worldLevel} for the New Game+ part");
            var control = rig.Creature("Greydwarf");
            var bashed = rig.Creature("Greydwarf");
            if (control == null || bashed == null)
            {
                c.Check(false, "could not spawn the two New Game+ Greydwarfs");
                c.Report();
                yield break;
            }
            Park(p, bashed, 30f);
            var landed = new Box<bool>();
            yield return WaitIdle(p);
            p.EquipItem(knife);
            yield return Fixed;
            sneak = Trained(p, Skills.SkillType.Sneak);
            stab = control.m_backstabTime;
            yield return RealSwing(p, control, landed);
            yield return Seconds(0.2f);
            var paid = Trained(p, Skills.SkillType.Sneak) > sneak;
            c.Check(landed.Value && control.m_backstabTime > stab && paid,
                $"control: a real KnifeCopper hit on an unaware Greydwarf is a sneak attack that pays Sneak XP (landed {landed.Value}, sneak attack {control.m_backstabTime > stab}, Sneak XP {paid})");
            Park(p, control, -30f);

            yield return WaitIdle(p);
            p.EquipItem(tower);
            yield return Fixed;
            var aiBashed = bashed.GetComponent<MonsterAI>();
            c.Check(aiBashed != null && !aiBashed.IsAlerted(), "New Game+: the other Greydwarf is unaware before the bash");
            SetSkill(p, Skills.SkillType.Blocking, 0f);
            // Read before the bash, not after it: a bash that was itself a sneak attack would move the backstab time
            // (the game then refuse a second backstab for 5 minutes) and the knife check after it would pass for the
            // wrong reason.
            sneak = Trained(p, Skills.SkillType.Sneak);
            stab = bashed.m_backstabTime;
            s = new Swing();
            yield return Press(p, bashed, s, SneakName, null);
            c.Check(s.Hit && aiBashed != null && aiBashed.IsAlerted(), $"New Game+: the bash reaches it and alerts it (hit {s.Hit}, health -{F(s.Lost)})");
            c.Check(Near(bashed.m_backstabTime, stab) && Trained(p, Skills.SkillType.Sneak) == sneak, "New Game+: the bash itself is no sneak attack and gives no Sneak XP");
            yield return Until(() => !p.InAttack(), 2f);
            p.EquipItem(knife);
            yield return Fixed;
            yield return RealSwing(p, bashed, landed);
            yield return Seconds(0.2f);
            c.Check(landed.Value, "New Game+: the KnifeCopper hit right after the bash lands");
            c.Check(Near(bashed.m_backstabTime, stab), "New Game+: that knife hit is no sneak attack");
            c.Check(Trained(p, Skills.SkillType.Sneak) == sneak, "New Game+: and it gives no Sneak XP");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- tower.x.kills ----------

    private static int OtherKills(Type counts, string creature, KillModifiers bucket)
    {
        var m = counts != null ? counts.GetMethod("GetKills", AnyStatic, null, new[] { typeof(string), typeof(KillModifiers) }, null) : null;
        return m != null ? (int)m.Invoke(null, new object[] { creature, bucket }) : -1;
    }

    // The creature's line on their statistics page ("    Greyling: 3 killed (melee 2, other 1)"), "" = no line.
    private static string OtherKillLine(Type section, string shownName)
    {
        var build = section != null ? section.GetMethod("Build", AnyStatic, null, Type.EmptyTypes, null) : null;
        var text = build != null ? build.Invoke(null, null) as string : null;
        if (text == null)
        {
            return null;
        }
        foreach (var line in text.Split('\n'))
        {
            if (line.TrimStart().StartsWith(shownName + ": ", StringComparison.Ordinal))
            {
                return line.Trim();
            }
        }
        return "";
    }

    private static int NumberAfter(string line, string pattern)
    {
        var m = Regex.Match(line ?? "", pattern);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : 0;
    }

    // Their statistics page with the weapon types shown, whatever the player's setting: their own in-memory test switch
    // (Plugin.TestDisplay, a nullable pair of sort order and "show weapon types"), never their config. was = what it
    // held (put back in finally). Null = switch not found (Release build of theirs): the page follows their setting.
    private static FieldInfo ShowWeaponTypes(string guid, out object was)
    {
        was = null;
        if (!Chainloader.PluginInfos.TryGetValue(guid, out var info) || info == null || info.Instance == null)
        {
            return null;
        }
        var field = info.Instance.GetType().GetField("TestDisplay", AnyStatic);
        var pair = field != null ? Nullable.GetUnderlyingType(field.FieldType) : null;
        var parts = pair != null && pair.IsGenericType ? pair.GetGenericArguments() : null;
        if (parts == null || parts.Length != 2 || !parts[0].IsEnum || parts[1] != typeof(bool))
        {
            return null;
        }
        was = field.GetValue(null);
        field.SetValue(null, Activator.CreateInstance(pair, Enum.ToObject(parts[0], 0), true));
        return field;
    }

    private static IEnumerator RunKills()
    {
        var c = new Checks(KillsName);
        var rig = new Rig(KillsName);
        FieldInfo display = null;
        object displayWas = null;
        try
        {
            var p = rig.P;
            if (!OtherActive(c, StatsGuid, "Creature Kill and Tame Counts", "C10"))
            {
                c.Report();
                yield break;
            }
            var counts = OtherType(StatsGuid, "CreatureCounts");
            var section = OtherType(StatsGuid, "StatsSection");
            display = ShowWeaponTypes(StatsGuid, out displayWas); // before the first read of the page
            // Fast bashes: no cooldown, swing at the clip's own speed (the kill count does not care).
            yield return UseRules(TowerRules.Defaults().With(r =>
            {
                r.BashCooldown = 0f;
                r.BashAnimationSpeed = 1f;
            }));
            var tower = rig.Give(IronTower);
            var grey = rig.Creature("Greyling");
            if (tower == null || grey == null)
            {
                c.Check(false, "could not add ShieldIronTower or spawn a Greyling");
                c.Report();
                yield break;
            }
            var name = grey.m_name;
            var shown = Localization.instance.Localize(name);
            var total0 = OtherKills(counts, name, KillModifiers.MixedAndTotal);
            var melee0 = OtherKills(counts, name, KillModifiers.Melee);
            var line0 = OtherKillLine(section, shown);
            if (total0 < 0 || melee0 < 0 || line0 == null)
            {
                c.Check(false, "could not read Creature Kill and Tame Counts' numbers (CreatureCounts.GetKills, StatsSection.Build)");
                c.Report();
                yield break;
            }
            p.SetGodMode(false); // a kill by a player in god mode is marked as cheated
            var drops = grey.GetComponent<CharacterDrop>();
            if (drops != null)
            {
                drops.SetDropsEnabled(false); // no loot left on the ground
            }
            yield return FreshBash(p, tower);
            yield return WaitIdle(p);
            FaceClearLane(p, Reach(p, grey), LaneFan, KillsName, grey);
            var bashes = BashWatch.DebugStarts.Count;
            SelfTestHooks.HoldAttack = true;
            var until = Time.time + 50f;
            while (Time.time < until && grey != null && !grey.IsDead())
            {
                if (!p.InAttack())
                {
                    PlaceInFront(p, grey);
                }
                p.SetHealth(p.GetMaxHealth()); // god mode is off: nothing that wanders by may kill the player
                yield return null;
            }
            SelfTestHooks.HoldAttack = false;
            var dead = grey == null || grey.IsDead();
            bashes = BashWatch.DebugStarts.Count - bashes;
            yield return WaitIdle(p);
            yield return Seconds(1f);
            c.Check(dead && ReferenceEquals(p.m_leftItem, tower), $"the Greyling is dead after {bashes} bash(es), nothing but the tower shield was used");
            if (dead)
            {
                var total = OtherKills(counts, name, KillModifiers.MixedAndTotal);
                var melee = OtherKills(counts, name, KillModifiers.Melee);
                var line = OtherKillLine(section, shown) ?? "";
                c.Check(total == total0 + 1 && melee == melee0 + 1, $"Creature Kill and Tame Counts: {shown} {total0} -> {total} killed, melee {melee0} -> {melee} (one more of each expected)");
                c.Check(NumberAfter(line, @": (\d+) killed") == total0 + 1, $"its line on the statistics page shows one more kill ('{line0}' -> '{line}')");
                // TESTING C10 is about the line: no melee number on it = not checked = a failed check, never a note.
                var shows = line.Contains("melee ");
                c.Check(shows && NumberAfter(line, @"melee (\d+)") == NumberAfter(line0, @"melee (\d+)") + 1,
                    shows
                        ? $"and one more melee kill ('{line0}' -> '{line}')"
                        : $"its line shows no melee number ('{line}'): the page hides weapon types (its ShowWeaponTypes setting) and its test switch {(display != null ? "did not show them" : "was not found")}");
            }
            c.Report();
        }
        finally
        {
            if (display != null)
            {
                try
                {
                    display.SetValue(null, displayWas);
                }
                catch (Exception e)
                {
                    SelfTest.Note(KillsName, $"could not put back Creature Kill and Tame Counts' display switch: {e.GetType().Name}: {e.Message}");
                }
            }
            rig.Done();
        }
    }

    // ---------- tower.x.spyglass, tower.x.instrument, tower.input.spyglass, tower.input.instrument ----------

    private sealed class DrawnItem
    {
        internal ItemDrop.ItemData Tower;
        internal ItemDrop.ItemData Other;
        internal bool Ready;       // press kept, item in the right hand, nothing swung meanwhile
        internal Attack Before;    // the player's attack when the item was asked for
    }

    // TESTING C11 up to "it comes out": BashCooldown 6, a bash, one press of the attack button once the swing is over,
    // a second, then the other mod's item drawn the way a hotbar key does it. Checks: press still kept then, the item
    // comes into the right hand and puts the tower away, nothing swings by itself until 0.7 s after.
    private static IEnumerator DrawOverKeptPress(Checks c, Rig rig, string prefab, string what, string test, DrawnItem d)
    {
        var p = rig.P;
        yield return UseRules(TowerRules.Defaults().With(r => r.BashCooldown = 6f));
        d.Tower = rig.Give(IronTower);
        d.Other = rig.Give(prefab);
        var draugr = rig.Creature("Draugr");
        if (d.Tower == null || d.Other == null || draugr == null)
        {
            c.Check(false, $"could not add ShieldIronTower and {prefab} or spawn a Draugr");
            yield break;
        }
        yield return FreshBash(p, d.Tower);
        var s = new Swing();
        yield return Press(p, draugr, s, test, null);
        var starts = BashWatch.DebugStarts.Count;
        p.m_queuedAttackTimer = BashWatch.QueuedPress; // one press of the attack button, the swing is over
        yield return Seconds(1f);
        var kept = s.Started && BashWatch.CooldownLeft(p) > 1f && p.m_queuedAttackTimer > 0.1f && BashWatch.DebugStarts.Count == starts && !p.InAttack();
        c.Check(kept, $"setup: 1 s after the press the bash still waits for its cooldown and the press is kept for it ({F(BashWatch.CooldownLeft(p))} s left, press timer {F(p.m_queuedAttackTimer)})");
        Park(p, draugr);
        d.Before = p.m_currentAttack;
        p.ToggleEquipped(d.Other);
        Attack leaked = null;
        var inHand = false;
        var until = Time.time + 4f;
        while (Time.time < until && leaked == null)
        {
            yield return null;
            if (!inHand && ReferenceEquals(p.m_rightItem, d.Other))
            {
                inHand = true;
                until = Time.time + 0.7f;
            }
            var now = p.m_currentAttack;
            if (now != null && !ReferenceEquals(now, d.Before))
            {
                leaked = now;
            }
        }
        c.Check(inHand && !d.Tower.m_equipped, $"{what} comes into the hand and the tower shield is put away ({Hands(p)})");
        c.Check(leaked == null, leaked == null
            ? $"nothing swings by itself while {what} comes out"
            : $"an attack with {PrefabOf(leaked.GetWeapon())} ('{leaked.m_attackAnimation}') started by itself while {what} came out");
        d.Ready = kept && inHand && leaked == null;
    }

    private static IEnumerator RunDrawn(string test, string guid, string mod, string prefab, string what)
    {
        var c = new Checks(test);
        var rig = new Rig(test);
        try
        {
            var p = rig.P;
            if (!OtherActive(c, guid, mod, "C11"))
            {
                c.Report();
                yield break;
            }
            var d = new DrawnItem();
            yield return DrawOverKeptPress(c, rig, prefab, what, test, d);
            if (!d.Ready)
            {
                c.Report();
                yield break;
            }
            // Attack button held with it: no attack (the click is theirs).
            SelfTestHooks.HoldAttack = true;
            yield return Seconds(0.5f);
            SelfTestHooks.HoldAttack = false;
            yield return Fixed;
            var held = p.m_currentAttack;
            c.Check(held == null || ReferenceEquals(held, d.Before), $"the attack button with {what} in hand starts no attack (no punch)");
            // Put away again: still nothing by itself.
            p.UnequipItem(d.Other);
            Attack leaked = null;
            var until = Time.time + 0.9f;
            while (Time.time < until && leaked == null)
            {
                yield return null;
                var now = p.m_currentAttack;
                if (now != null && !ReferenceEquals(now, d.Before))
                {
                    leaked = now;
                }
            }
            c.Check(!d.Other.m_equipped && leaked == null, leaked == null
                ? $"{what} put away again: still no punch by itself"
                : $"an attack with {PrefabOf(leaked.GetWeapon())} ('{leaked.m_attackAnimation}') started by itself after {what} was put away");
            // Control: a fresh press does start an attack now (so "nothing" means something).
            yield return WaitIdle(p);
            var last = p.m_currentAttack;
            p.m_queuedAttackTimer = BashWatch.QueuedPress;
            yield return Until(() => p.m_currentAttack != null && !ReferenceEquals(p.m_currentAttack, last), 0.6f);
            c.Check(p.m_currentAttack != null && !ReferenceEquals(p.m_currentAttack, last), "control: a new press of the attack button starts an attack");
            yield return WaitIdle(p);
            p.m_queuedAttackTimer = 0f;
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    private static IEnumerator RunSpyglass() => RunDrawn(SpyglassName, SpyglassGuid, "Spyglass", "MC_Spyglass", "the spyglass");

    private static IEnumerator RunInstrument() => RunDrawn(InstrumentName, MusicGuid, "Music Instruments", "MC_Flute", "the flute");

    // A real left click with their item in hand, after the kept press: the click does their thing (state read from
    // their own flag), and no attack starts.
    private static IEnumerator RunDrawnClick(string test, string guid, string mod, string prefab, string what, string stateType, string stateMember, string effect, string closeMethod)
    {
        var c = new Checks(test);
        var rig = new Rig(test);
        Type state = null;
        try
        {
            var p = rig.P;
            if (!OtherActive(c, guid, mod, "C11"))
            {
                c.Report();
                yield break;
            }
            state = OtherType(guid, stateType);
            if (OtherBool(state, stateMember) == null)
            {
                c.Check(false, $"could not read {mod}'s {stateType}.{stateMember} (what a click with {what} does)");
                c.Report();
                yield break;
            }
            if (!AttackOnLeftMouse(out var path))
            {
                c.Check(false, $"the attack button is bound to '{path}', not to the left mouse button: the test can only press that one");
                c.Report();
                yield break;
            }
            var d = new DrawnItem();
            yield return DrawOverKeptPress(c, rig, prefab, what, test, d);
            if (!d.Ready)
            {
                c.Report();
                yield break;
            }
            yield return WaitIdle(p);
            c.Check(OtherBool(state, stateMember) == false, $"before the click: {effect} is not so yet");
            if (!SelfTest.Key(KeyCode.Mouse0, true))
            {
                c.Check(false, "no input driver: the left mouse button could not be pressed (not a probe run?)");
                c.Report();
                yield break;
            }
            yield return new WaitForSecondsRealtime(0.15f);
            SelfTest.Key(KeyCode.Mouse0, false);
            Attack leaked = null;
            var done = false;
            var until = Time.realtimeSinceStartup + 2.5f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                done |= OtherBool(state, stateMember) == true;
                var now = p.m_currentAttack;
                if (now != null && !ReferenceEquals(now, d.Before))
                {
                    leaked = now;
                }
                if (done && Time.realtimeSinceStartup > until - 1.8f)
                {
                    break; // their thing happened and 0.7 s passed since the click
                }
            }
            c.Check(done, $"a real left click with {what} in hand does what it did before: {effect}");
            c.Check(leaked == null, leaked == null ? "and it starts no attack (no punch)" : $"an attack with {PrefabOf(leaked.GetWeapon())} ('{leaked.m_attackAnimation}') started on the click");
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            try
            {
                if (closeMethod != null && OtherBool(state, stateMember) == true)
                {
                    OtherCall(state, closeMethod);
                }
            }
            catch (Exception e)
            {
                SelfTest.Note(test, $"could not close what the click opened: {e.GetType().Name}: {e.Message}");
            }
            rig.Done();
        }
    }

    private static IEnumerator RunInputSpyglass() =>
        RunDrawnClick(InputSpyglassName, SpyglassGuid, "Spyglass", "MC_Spyglass", "the spyglass", "Scope", "Engaged", "the spyglass goes up to the eye", "Abort");

    private static IEnumerator RunInputInstrument() =>
        RunDrawnClick(InputInstrumentName, MusicGuid, "Music Instruments", "MC_Flute", "the flute", "Performance", "WindowOpen", "the Songs window opens", "CloseWindow");

    // ---------- tower.x.forge ----------

    private static IEnumerator RunForge()
    {
        var c = new Checks(ForgeName);
        var rig = new Rig(ForgeName);
        GameObject forge = null;
        string taught = null;
        var idols = new Dictionary<string, int>(); // idol item name -> how many the player had before
        try
        {
            var p = rig.P;
            var inv = rig.Inv;
            if (!OtherActive(c, ForgeGuid, "Forge Idol Upgrades", "C03"))
            {
                c.Report();
                yield break;
            }
            var rules = TowerRules.Defaults();
            yield return UseRules(rules);
            var stationPrefab = ZNetScene.instance.GetPrefab("UpgradeStation");
            // A default tower shield whose recipe takes an idol (the Iron one when it does).
            string prefabName = null;
            Piece.Requirement idol = null;
            foreach (var entry in rules.Entries)
            {
                var go = ObjectDB.instance.GetItemPrefab(entry.Prefab);
                var drop = go != null ? go.GetComponent<ItemDrop>() : null;
                var recipe = drop != null ? ObjectDB.instance.GetRecipe(drop.m_itemData) : null;
                var req = recipe != null && recipe.m_resources != null ? recipe.m_resources.FirstOrDefault(q => q != null && q.m_upgraderResource && q.m_resItem != null) : null;
                if (req != null && (prefabName == null || entry.Prefab == IronTower))
                {
                    prefabName = entry.Prefab;
                    idol = req;
                }
            }
            if (stationPrefab == null || prefabName == null)
            {
                c.Check(false, stationPrefab == null ? "no Forge of Potential (UpgradeStation) in this game" : "no default tower shield has a recipe with an idol: none can be refined");
                c.Report();
                yield break;
            }
            var snap = SnapshotOf(prefabName);
            var tower = rig.Give(prefabName);
            var sword = rig.Give("SwordIron");
            if (tower == null || sword == null || snap == null)
            {
                c.Check(false, $"could not add {prefabName} and SwordIron");
                c.Report();
                yield break;
            }
            if (p.m_knownRecipes.Add(tower.m_shared.m_name))
            {
                taught = tower.m_shared.m_name;
            }
            // Three-star idols of every tier of the recipe's kind (Forge Idol Upgrades may ask for a higher tier than
            // the recipe's own), enough for five tries.
            var own = idol.m_resItem.gameObject.name;
            var match = Regex.Match(own, @"^Upgrader(\d+)(\w+)$");
            var idolPrefabs = new List<string>();
            if (match.Success)
            {
                for (var tier = 0; tier <= 7; tier++)
                {
                    var name = "Upgrader" + tier + match.Groups[2].Value;
                    if (ObjectDB.instance.GetItemPrefab(name) != null)
                    {
                        idolPrefabs.Add(name);
                    }
                }
            }
            if (!idolPrefabs.Contains(own))
            {
                idolPrefabs.Add(own);
            }
            // Five tries' worth of each. No room for every tier (idols may not stack) = the recipe's own tier only: an
            // item that does not fit would fall on the ground.
            var amount = Mathf.Max(1, idol.GetAmount(tower.m_quality + 1)) * 5;
            var ownShared = SharedOf(own);
            var slotsEach = ownShared != null ? Mathf.CeilToInt(amount / (float)Mathf.Max(1, ownShared.m_maxStackSize)) : amount;
            if (inv.GetEmptySlots() < slotsEach * idolPrefabs.Count + 2)
            {
                idolPrefabs.Clear();
                idolPrefabs.Add(own);
            }
            if (inv.GetEmptySlots() < slotsEach + 2)
            {
                c.Check(false, $"no room in the inventory for {amount} idols ({inv.GetEmptySlots()} free slots)");
                c.Report();
                yield break;
            }
            foreach (var name in idolPrefabs)
            {
                var shared = SharedOf(name);
                if (shared == null)
                {
                    continue;
                }
                if (!idols.ContainsKey(shared.m_name))
                {
                    idols[shared.m_name] = inv.CountItems(shared.m_name, -1, false);
                }
                inv.AddItem(name, amount, 4, 0, 0L, "", false);
            }
            SelfTest.Note(ForgeName, $"{prefabName} (level {tower.m_quality}); its recipe's idol is {own}; given {amount} three-star idols of each of {string.Join(", ", idolPrefabs.ToArray())}");

            // The Forge of Potential, 3 m ahead (it is big), opened on its Upgrade tab.
            var fwd = Flat(p.transform.forward);
            var pos = p.transform.position + fwd * 3f;
            pos.y = ZoneSystem.instance.GetGroundHeight(pos);
            forge = Object.Instantiate(stationPrefab, pos, Quaternion.LookRotation(-fwd));
            yield return new WaitForSeconds(0.5f);
            var station = forge.GetComponentInChildren<CraftingStation>();
            var gui = InventoryGui.instance;
            if (station == null || gui == null)
            {
                c.Check(false, "the Forge of Potential has no crafting station, or there is no inventory screen");
                c.Report();
                yield break;
            }
            station.m_useDistance = 50f;
            p.SetCraftingStation(station);
            gui.Show(null, 3);
            yield return new WaitForSecondsRealtime(1f);

            var start = tower.m_quality;
            var listed = false;
            var tries = 0;
            var off = false;
            while (tries < 5 && tower != null && tower.m_quality <= start)
            {
                var row = -1;
                for (var i = 0; i < gui.m_availableRecipes.Count; i++)
                {
                    if (ReferenceEquals(gui.m_availableRecipes[i].ItemData, tower))
                    {
                        row = i;
                    }
                }
                if (row < 0)
                {
                    break;
                }
                listed = true;
                gui.SetRecipe(row, false);
                yield return null;
                yield return null;
                if (!gui.m_craftButton.interactable)
                {
                    off = true;
                    break;
                }
                tries++;
                gui.OnCraftPressed();
                yield return null;
                if (gui.m_craftTimer >= 0f)
                {
                    gui.m_craftTimer = 1000f; // skip the wait: the next update refines
                }
                yield return null;
                yield return null;
                // The refined shield: the same item, or a new one of the same prefab.
                if (!inv.ContainsItem(tower))
                {
                    tower = inv.GetAllItems().FirstOrDefault(i => i != null && i.m_dropPrefab != null && i.m_dropPrefab.name == prefabName);
                    rig.Track(tower);
                }
                gui.UpdateCraftingPanel();
                yield return null;
            }
            c.Check(listed, $"the Forge of Potential lists the {prefabName} on its Upgrade tab");
            var refined = tower != null && tower.m_quality == start + 1;
            c.Check(refined, $"the tower shield is refined from level {start} to {start + 1} within {tries} tries (level {(tower != null ? tower.m_quality.ToString() : "item gone")}"
                             + $"{(off ? "; the refine button was off: no idol the Forge accepts" : "")})");
            gui.Hide();
            p.SetCraftingStation(null);
            yield return null;
            if (refined)
            {
                var level = tower.m_quality;
                var want = (snap.BlockPower + snap.BlockPowerPerLevel * (level - 1)) * rules.BlockArmorMultiplier;
                c.Check(tower.m_shared.m_itemType == ItemType.TwoHandedWeaponLeft && tower.IsTwoHanded() && TowerData.IsAppliedTower(tower),
                    $"refined: still a two-handed tower shield ({tower.m_shared.m_itemType})");
                c.Check(Near(tower.GetBaseBlockPower(), want, 0.01f),
                    $"refined: block armor {F(tower.GetBaseBlockPower())} = its new level's ({F(snap.BlockPower)} + {level - 1} x {F(snap.BlockPowerPerLevel)}) x {F(rules.BlockArmorMultiplier)} = {F(want)}");
                p.EquipItem(sword);
                p.EquipItem(tower);
                yield return Fixed;
                c.Check(Only(p, tower, left: true) && !sword.m_equipped, $"refined: it takes both hands ({Hands(p)})");
            }
            c.Report();
        }
        finally
        {
            var gui = InventoryGui.instance;
            if (gui != null && InventoryGui.IsVisible())
            {
                gui.Hide();
            }
            var p = rig.P;
            if (p != null)
            {
                p.SetCraftingStation(null);
                if (taught != null)
                {
                    p.m_knownRecipes.Remove(taught);
                }
                foreach (var pair in idols)
                {
                    var extra = rig.Inv.CountItems(pair.Key, -1, false) - pair.Value;
                    if (extra > 0)
                    {
                        rig.Inv.RemoveItem(pair.Key, extra, -1, false);
                    }
                }
            }
            if (forge != null)
            {
                ZNetScene.instance.Destroy(forge);
            }
            rig.Done();
        }
    }

    // ---------- tower.x.sort ----------

    private static IEnumerator RunSort()
    {
        var c = new Checks(SortName);
        var rig = new Rig(SortName);
        FieldInfo forced = null;
        try
        {
            var p = rig.P;
            if (!OtherActive(c, SortGuid, "Sort Chest", "C01"))
            {
                c.Report();
                yield break;
            }
            yield return UseRules(TowerRules.Defaults());
            var sorter = OtherType(SortGuid, "ContainerSorter");
            var chest = rig.Chest(out var chestName);
            if (sorter == null || chest == null)
            {
                c.Check(false, $"could not find Sort Chest's sorter or build a chest ({chestName})");
                c.Report();
                yield break;
            }
            // Sort by type, whatever the player's setting: their own in-memory test switch, never their config.
            var plugin = Chainloader.PluginInfos[SortGuid].Instance;
            var field = plugin.GetType().GetField("TestSortBy", AnyStatic);
            var enumType = field != null ? Nullable.GetUnderlyingType(field.FieldType) : null;
            if (enumType != null && enumType.IsEnum && Enum.IsDefined(enumType, "Type"))
            {
                field.SetValue(null, Enum.Parse(enumType, "Type"));
                forced = field;
            }
            else
            {
                var entry = plugin.Config.Keys.FirstOrDefault(k => k.Key == "SortBy");
                var value = entry != null ? plugin.Config[entry].BoxedValue : null;
                if (value == null || value.ToString() != "Type")
                {
                    c.Check(false, $"Sort Chest sorts by '{value}', not by type, and its test switch was not found: the grouping was not checked");
                    c.Report();
                    yield break;
                }
            }
            var inv = chest.GetInventory();
            var shields = new[] { IronTower, "ShieldBanded", "ShieldWoodTower", "ShieldWood" };
            var round = new[] { "ShieldBanded", "ShieldWood" };
            var weapons = new[] { "SwordIron", "AxeIron", "MaceIron" };
            // Arrows too: Sort Chest put ammo between the weapons and the shields. Without them a tower shield filed as
            // a weapon (last of the weapons: its skill is Blocking) would still sit right before the round shields,
            // and "the four shields are together" would pass all the same.
            const string ammo = "ArrowWood";
            // Mixed on purpose: weapon, tower, weapon, round shield...
            foreach (var prefab in new[] { weapons[0], shields[0], weapons[1], shields[1], ammo, shields[2], weapons[2], shields[3] })
            {
                if (inv.AddItem(prefab, 1, 1, 0, 0L, "", false) == null)
                {
                    c.Check(false, $"could not put {prefab} in the chest");
                }
            }
            yield return null;
            var towers = inv.GetAllItems().Count(i => i.m_shared.m_itemType == ItemType.TwoHandedWeaponLeft);
            c.Check(towers == 2, $"the two tower shields in the chest carry the tower data (two-handed): {towers}");
            var gui = InventoryGui.instance;
            chest.Interact(p, false, false);
            yield return Until(() => ReferenceEquals(gui.m_currentContainer, chest), 2f);
            if (!ReferenceEquals(gui.m_currentContainer, chest))
            {
                gui.Show(chest);
                yield return Until(() => ReferenceEquals(gui.m_currentContainer, chest), 2f);
            }
            yield return new WaitForSecondsRealtime(0.5f);
            c.Check(ReferenceEquals(gui.m_currentContainer, chest) && InventoryGui.IsVisible(), "the chest is open");
            var called = OtherCall(sorter, "TrySortOpenContainer"); // what Sort Chest's button runs
            yield return null;
            yield return null;
            c.Check(called, "Sort Chest's Sort ran on the open chest");
            var order = inv.GetAllItems().OrderBy(i => i.m_gridPos.y).ThenBy(i => i.m_gridPos.x).Select(PrefabOf).ToList();
            var at = order.Select((name, index) => shields.Contains(name) ? index : -1).Where(i => i >= 0).ToList();
            var together = at.Count == shields.Length && at.Max() - at.Min() == shields.Length - 1;
            var weaponAt = order.Select((name, index) => weapons.Contains(name) ? index : -1).Where(i => i >= 0).ToList();
            var roundAt = order.Select((name, index) => round.Contains(name) ? index : -1).Where(i => i >= 0).ToList();
            var ammoAt = order.IndexOf(ammo);
            // What make the next check tell something: weapons and round shields are apart in this chest.
            c.Check(ammoAt >= 0 && weaponAt.Count == weapons.Length && roundAt.Count == round.Length && weaponAt.Max() < ammoAt && ammoAt < roundAt.Min(),
                $"Sort Chest puts the arrows between the weapons and the round shields, so a tower shield filed as a weapon would sit apart from the shields ({string.Join(", ", order.ToArray())})");
            c.Check(order.Count == 8 && together,
                $"after the sort the tower shields sit with the round shields, no weapon and no arrow between them ({string.Join(", ", order.ToArray())})");
            gui.Hide();
            yield return null;
            c.Report();
        }
        finally
        {
            if (forced != null)
            {
                forced.SetValue(null, null);
            }
            var gui = InventoryGui.instance;
            if (gui != null && InventoryGui.IsVisible())
            {
                gui.Hide();
            }
            rig.Done();
        }
    }

    // ---------- tower.x.kinds ----------

    private static IEnumerator RunKinds()
    {
        var c = new Checks(KindsName);
        try
        {
            yield return UseRules(TowerRules.Defaults()); // inside the try: a stop while waiting still clear the test rules
            var tower = SharedOf(IronTower);
            var round = SharedOf("ShieldBanded");
            var sword = SharedOf("SwordIron");
            if (tower == null || round == null || sword == null)
            {
                c.Check(false, "ShieldIronTower, ShieldBanded or SwordIron is missing from the game's items");
                c.Report();
                yield break;
            }
            c.Check(tower.m_itemType == ItemType.TwoHandedWeaponLeft, $"the ShieldIronTower prefab carries the tower data (item type {tower.m_itemType}, which the game counts as a weapon)");
            foreach (var mod in new[]
                     {
                         new KeyValuePair<string, string>(SortGuid, "Sort Chest"),
                         new KeyValuePair<string, string>(CraftListGuid, "Crafting Search and Sort"),
                         new KeyValuePair<string, string>(EncyclopediaGuid, "Encyclopedia"),
                     })
            {
                if (!OtherActive(c, mod.Key, mod.Value, "C01"))
                {
                    continue;
                }
                // Their own copy of the item classifier (each MC mod carries one): what their lists group by.
                var kinds = OtherType(mod.Key, "ItemKinds");
                var classify = kinds != null ? kinds.GetMethod("Classify", AnyStatic, null, new[] { typeof(ItemDrop.ItemData.SharedData) }, null) : null;
                if (classify == null)
                {
                    c.Check(false, $"{mod.Value}: its item classifier (ItemKinds.Classify) was not found");
                    continue;
                }
                var asTower = classify.Invoke(null, new object[] { tower }).ToString();
                var asRound = classify.Invoke(null, new object[] { round }).ToString();
                var asSword = classify.Invoke(null, new object[] { sword }).ToString();
                c.Check(asTower == "Shield" && asRound == "Shield" && asSword == "Weapon",
                    $"{mod.Value} files a tower shield with the shields, not with the weapons (tower shield {asTower}, round shield {asRound}, sword {asSword})");
            }
            c.Report();
        }
        finally
        {
            ServerRules.TestRules = null;
        }
    }

    // ---------- tower.x.harpoon ----------

    // Look so that the thrown projectile reaches the target (launch point of the attack, gravity drop added).
    private static void AimThrow(Player p, Attack attack, Projectile projectile, Vector3 target)
    {
        var flat = Flat(target - p.transform.position);
        var origin = p.transform;
        if (!string.IsNullOrEmpty(attack.m_attackOriginJoint) && p.GetVisual() != null)
        {
            var joint = Utils.FindChild(p.GetVisual().transform, attack.m_attackOriginJoint);
            if (joint != null)
            {
                origin = joint;
            }
        }
        var height = origin.position.y + attack.m_attackHeight - p.transform.position.y;
        var launch = p.transform.position + Vector3.up * height + flat * attack.m_attackRange;
        var to = target - launch;
        var time = attack.m_projectileVel > 0.1f ? to.magnitude / attack.m_projectileVel : 0f;
        to.y += 0.5f * projectile.m_gravity * time * time;
        var dir = to.normalized;
        p.m_lookYaw = Quaternion.LookRotation(flat);
        p.m_lookPitch = -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg; // positive pitch = down
        p.transform.rotation = p.m_lookYaw;
        if (p.m_body != null)
        {
            p.m_body.rotation = p.m_lookYaw;
        }
        if (p.m_eye != null)
        {
            p.m_eye.rotation = p.m_lookYaw * Quaternion.Euler(p.m_lookPitch, 0f, 0f);
        }
    }

    private static IEnumerator RunHarpoon()
    {
        var c = new Checks(HarpoonName);
        var rig = new Rig(HarpoonName);
        var pitch = rig.P.m_lookPitch;
        try
        {
            var p = rig.P;
            if (!OtherActive(c, HarpoonGuid, "Harpoon Hooks Tames", "C09"))
            {
                c.Report();
                yield break;
            }
            yield return UseRules(TowerRules.Defaults());
            var harpoon = rig.Give("SpearChitin");
            var tower = rig.Give(IronTower);
            var boar = rig.Creature("Boar");
            var grey = rig.Creature("Greydwarf");
            if (harpoon == null || tower == null || boar == null || grey == null)
            {
                c.Check(false, "could not add SpearChitin and ShieldIronTower or spawn a Boar and a Greydwarf");
                c.Report();
                yield break;
            }
            var shared = harpoon.m_shared;
            var secondary = false;
            Attack thrown = null;
            if (shared.m_attack != null && shared.m_attack.m_attackProjectile != null)
            {
                thrown = shared.m_attack;
            }
            else if (shared.m_secondaryAttack != null && shared.m_secondaryAttack.m_attackProjectile != null)
            {
                thrown = shared.m_secondaryAttack;
                secondary = true;
            }
            var projectile = thrown != null ? thrown.m_attackProjectile.GetComponent<Projectile>() : null;
            var effect = shared.m_attackStatusEffect;
            if (thrown == null || projectile == null || effect == null)
            {
                c.Check(false, "SpearChitin has no thrown attack with a hook effect in this game");
                c.Report();
                yield break;
            }
            var hook = effect.NameHash();
            boar.SetTamed(true);
            SetSkill(p, Skills.SkillType.Blocking, 0f);
            Park(p, grey, 25f);
            FaceClearLane(p, 6f, LaneFan, HarpoonName, boar);
            var fwd = Flat(p.transform.forward);
            var spot = p.transform.position + fwd * 5f;
            Action hold = () =>
            {
                var rot = Quaternion.LookRotation(-fwd);
                boar.transform.SetPositionAndRotation(spot, rot);
                if (boar.m_body != null)
                {
                    boar.m_body.position = spot;
                    boar.m_body.rotation = rot;
                }
                AimThrow(p, thrown, projectile, boar.GetCenterPoint());
            };
            p.EquipItem(harpoon);
            yield return WaitIdle(p);
            boar.SetHealth(boar.GetMaxHealth());
            var health = boar.GetHealth();
            c.Check(boar.IsTamed() && ReferenceEquals(p.GetCurrentWeapon(), harpoon), "a tame Boar 5 m ahead, the Abyssal Harpoon in hand");

            // Real throws (attack, animation, projectile, flight) until one hooks it, five at most.
            var hooked = false;
            var throws = 0;
            for (var attempt = 0; attempt < 5 && !hooked; attempt++)
            {
                var t0 = Time.time;
                while (p.InAttack() && Time.time - t0 < 3f)
                {
                    hold();
                    yield return null;
                }
                t0 = Time.time;
                while (Time.time - t0 < 0.3f)
                {
                    hold();
                    yield return null;
                }
                harpoon.m_lastProjectile = null;
                if (!p.StartAttack(null, secondary))
                {
                    yield return Seconds(0.7f);
                    continue;
                }
                throws++;
                t0 = Time.time;
                while (harpoon.m_lastProjectile == null && Time.time - t0 < 4f)
                {
                    hold();
                    yield return null;
                }
                var flying = harpoon.m_lastProjectile;
                if (flying == null)
                {
                    continue;
                }
                rig.Track(flying);
                t0 = Time.time;
                while (Time.time - t0 < 4f && flying != null && !boar.GetSEMan().HaveStatusEffect(hook))
                {
                    hold();
                    yield return Fixed;
                }
                hooked = boar.GetSEMan().HaveStatusEffect(hook);
                if (!hooked && flying != null)
                {
                    ZNetScene.instance.Destroy(flying);
                }
            }
            c.Check(hooked, $"a real harpoon throw hooks the tame Boar ({throws} throw(s))");
            if (hooked)
            {
                yield return Seconds(0.5f);
                c.Check(Near(boar.GetHealth(), health, 0.01f), $"hooked, the tame takes no damage from the harpoon (health {F(health)} -> {F(boar.GetHealth())})");
                // Switch to the tower shield, bash the wild Greydwarf.
                yield return WaitIdle(p);
                p.m_lookPitch = pitch;
                p.EquipItem(tower);
                yield return Fixed;
                c.Check(Only(p, tower, left: true), $"the tower shield is in hand, the harpoon put away ({Hands(p)})");
                yield return FreshBash(p, tower);
                var s = new Swing();
                yield return Press(p, grey, s, HarpoonName, null);
                SelfTest.Note(HarpoonName, Describe("bash on the wild Greydwarf", s));
                c.Check(s.Landed && s.Staggered, "the bash staggers the wild Greydwarf in one hit, as without a hooked tame");
                yield return Seconds(1f);
                c.Check(boar != null && !boar.IsDead() && Near(boar.GetHealth(), health, 0.01f),
                    $"after the switch and the bash the tame still has taken no damage (health {(boar != null ? F(boar.GetHealth()) : "gone")})");
            }
            c.Report();
        }
        finally
        {
            if (rig.P != null)
            {
                rig.P.m_lookPitch = pitch;
            }
            rig.Done();
        }
    }
}
#endif
