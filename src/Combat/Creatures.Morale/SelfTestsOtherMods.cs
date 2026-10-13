#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.CreaturesMoraleMod;

// Debug build only. Creature Morale together with another MC mod loaded in the same game (TESTING.md "other mods").
// Soft: the other mod is found by its GUID in the shared registry and must be active (else the test fails: no pass
// without a check); me never touch its types, only what it do to the game (the attack object, the hit, a status
// effect, a page of the game's Compendium, its Debug console command).
//   morale.x.tower-bash         Tower Shield Wall: a real shield bash on an afraid Greydwarf, running or unaware (X05)
//   morale.x.tower-bash-ngplus  New Game+ world (world level 1): a real bash on an afraid Greydwarf that has not noticed
//                               the player (a sneak attack on it is possible), then a knife stab right after (X05)
//   morale.x.dual-wield         Dual Wielding: first swing in the air, the off-hand swing hits an afraid Greydwarf (X07)
//   morale.x.moveset            Weapon Moveset: jump attack, sneak roll attack seen and from behind, on afraid
//                               Greylings; Sneak Ambush's XP with them (X08)
//   morale.x.harpoon-hook       Harpoon Hooks Tames: a real harpoon (with the game's hook effect) on a wild afraid Boar
//                               and on the player's own tame next to afraid Boars (X06)
//   morale.x.encyclopedia       Encyclopedia: an afraid creature never met, seen running then calm, is met and listed
//                               under its normal name (X02)
//   morale.x.kill-counts        Creature Kill and Tame Counts: its Greyling line on the Compendium's Player Statistics
//                               page show the count the kill step used (X03)
//   morale.x.sneak-xp           Sneak Ambush: no sneak attack and no Sneak XP for a shot at an afraid Greydwarf that
//                               watches the player; a stab from behind, unseen, is one and pays (X01, the parts without
//                               the Smoke Screen)
// A swing is started the way the attack button do it (the game keep a press half a second and start the swing
// itself: Player.PlayerAttackInput), so the other mod's own attack rules run.
internal static partial class SelfTests
{
    private const string TowerBashName = "morale.x.tower-bash";
    private const string TowerNgName = "morale.x.tower-bash-ngplus";
    private const string DualWieldName = "morale.x.dual-wield";
    private const string MovesetName = "morale.x.moveset";
    private const string HarpoonHookName = "morale.x.harpoon-hook";
    private const string EncyclopediaName = "morale.x.encyclopedia";
    private const string KillCountsName = "morale.x.kill-counts";
    private const string SneakXpName = "morale.x.sneak-xp";

    private const string TowerGuid = "MC.Combat.Shields.TowerWall";
    private const string DualWieldGuid = "MC.Combat.Weapons.DualWield";
    private const string MovesetGuid = "MC.Combat.Weapons.Moveset";
    private const string HarpoonGuid = "MC.Farming.Harpoon.HooksTames";
    private const string KillCountsGuid = "MC.Exploration.Stats.PerCreature";
    private const string SneakAmbushGuid = "MC.Combat.Sneak.Ambush";
    private const string EncyclopediaCommand = "compendium_debug";

    // Sneak attack XP of Sneak Ambush is 3 or more with its default settings; a second of crawling gives 1 at most.
    private const float SneakXpPaid = 1.5f;

    // ================================================================ shared

    private static bool ModOn(string guid)
    {
        var view = FeatureRegistry.Find(guid);
        return view != null && view.Value.IsActive;
    }

    // Other MC mod loaded and active? Else the test fails here: a skipped test must not read as proof.
    private static bool OtherModOn(string test, string guid, string name, string item)
    {
        var view = FeatureRegistry.Find(guid);
        if (view != null && view.Value.IsActive)
        {
            return true;
        }
        SelfTest.Fail(test, $"{name} is not loaded and active ({(view != null ? view.Value.State + ": " + view.Value.Status : "not installed")}): "
                            + $"nothing of TESTING.md {item} was checked");
        return false;
    }

    // What a press of the attack button leave in the game: Player.PlayerAttackInput keep it half a second and starts
    // the swing as soon as the game (and every mod on that path) allows it.
    private static void PressAttack(Player player) => player.m_queuedAttackTimer = 0.5f;

    private static void ReleaseAttack(Player player)
    {
        if (player != null)
        {
            player.m_queuedAttackTimer = 0f;
        }
    }

    // The player's hit reached this creature since the flag was cleared (vanilla set it at the start of
    // Character.RPC_Damage for every hit of the local player, also one that deals no damage).
    private static bool WasHit(MonsterAI ai) => ai != null && ai.m_character != null && ai.m_character.m_localPlayerHasHit;

    private static void ClearHit(MonsterAI ai)
    {
        if (ai != null && ai.m_character != null)
        {
            ai.m_character.m_localPlayerHasHit = false;
        }
    }

    // Hit of a thrown or shot weapon as vanilla Attack build it (the status effect of the weapon ride on it).
    private static HitData WeaponHit(Player player, ItemDrop.ItemData weapon, ItemDrop.ItemData ammo)
    {
        var hit = new HitData();
        hit.m_toolTier = (short)weapon.m_shared.m_toolTier;
        hit.m_pushForce = weapon.m_shared.m_attackForce;
        hit.m_backstabBonus = weapon.m_shared.m_backstabBonus;
        hit.m_damage.Add(weapon.GetDamage());
        if (ammo != null)
        {
            hit.m_damage.Add(ammo.GetDamage());
        }
        hit.m_statusEffectHash = weapon.m_shared.m_attackStatusEffect != null ? weapon.m_shared.m_attackStatusEffect.NameHash() : 0;
        hit.m_blockable = weapon.m_shared.m_blockable;
        hit.m_dodgeable = weapon.m_shared.m_dodgeable;
        hit.m_skill = weapon.m_shared.m_skillType;
        hit.SetAttacker(player);
        hit.m_hitType = HitData.HitType.PlayerHit;
        return hit;
    }

    // Sneak skill at a fixed place for a test (level 20: a sneak attack's XP never levels it up, so the gain can be
    // read from the accumulator). Dispose-like: Restore.
    private sealed class SneakSkill
    {
        private readonly Skills.Skill _skill;
        private readonly float _level;
        private readonly float _accumulator;

        internal SneakSkill(Player player)
        {
            _skill = player.GetSkills().GetSkill(Skills.SkillType.Sneak);
            _level = _skill.m_level;
            _accumulator = _skill.m_accumulator;
            _skill.m_level = 20f;
            _skill.m_accumulator = 0f;
        }

        internal float Xp => (_skill.m_level - 20f) * 1000f + _skill.m_accumulator;

        internal void Restore()
        {
            _skill.m_level = _level;
            _skill.m_accumulator = _accumulator;
        }
    }

    // Afraid Greydwarf (or other) held `ahead` m in front of the player, facing them or facing away.
    private static Vector3 Ahead(Stage s, float ahead) => s.Player.transform.position + s.Forward * ahead;

    // ================================================================ morale.x.tower-bash

    private static IEnumerator RunTowerBash()
    {
        if (!OtherModOn(TowerBashName, TowerGuid, "MC Tower Shield Wall", "X05"))
        {
            yield break;
        }
        var c = new Checks(TowerBashName);
        Stage s = null;
        try
        {
            s = Stage.Begin(TowerBashName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            // CorneredRange 0: only the bash can make it fight (a creature held this close would be cornered).
            ServerRules.TestRules = NewRules(r => r.CorneredRange = 0f);
            Standing.TestOverride = new Standing.Override(4);
            yield return BashSteps(s, c, TowerBashName, "X05", true);
            yield return BashSteps(s, c, TowerBashName, "X05", false);
            c.Report();
        }
        finally
        {
            ReleaseAttack(Player.m_localPlayer);
            Finish(s);
        }
    }

    // running = it run from the (noisy) player, held 1.3 m in front; else it faces away and not noticed the
    // silent player behind it. One real bash; it must turn and fight.
    private static IEnumerator BashSteps(Stage s, Checks c, string test, string item, bool running)
    {
        var player = s.Player;
        var w = new Waiter();
        var what = running ? "a Greydwarf that runs from the player" : "a Greydwarf that has not noticed the player behind it";
        var shield = s.EquipWeapon("ShieldWoodTower");
        if (!c.Check(shield != null && player.IsItemEquiped(shield), $"{item}: a wood tower shield (ShieldWoodTower) added and equipped"))
        {
            yield break;
        }
        var pos = Stage.Ground(Ahead(s, 1.3f));
        var g = s.Spawn("Greydwarf", pos, running ? player.transform.position - pos : pos - player.transform.position);
        if (!c.Check(g != null, $"{item}: could not spawn a Greydwarf"))
        {
            yield break;
        }
        Tough(g.m_character);
        Action hold = () =>
        {
            if (running)
            {
                s.Noise();
            }
            else
            {
                s.Silence();
            }
            Stage.Place(g.m_character, pos, running ? player.transform.position - pos : pos - player.transform.position);
        };
        if (running)
        {
            yield return Until(() => FearOf(g) == player && g.IsAlerted(), 4f, hold, w);
            c.Check(w.Met && !Provoked(g, player), $"{item} setup: the afraid Greydwarf runs from the rank 4 player and is not provoked (runs from {Who(FearOf(g))})");
        }
        else
        {
            yield return Wait(1.2f, hold);
            c.Check(Attitudes.Judge(g, player) == Attitude.Afraid && !g.IsAlerted() && FearOf(g) == null && !Provoked(g, player),
                $"{item} setup: the afraid Greydwarf has not noticed the silent player behind it (alerted {g.IsAlerted()}, runs from {Who(FearOf(g))})");
        }
        ClearHit(g);
        Attack bash = null;
        var before = player.m_currentAttack;
        yield return Until(() => WasHit(g), 6f, () =>
        {
            hold();
            if (bash == null)
            {
                PressAttack(player);
                var current = player.m_currentAttack;
                if (current != null && !ReferenceEquals(current, before))
                {
                    bash = current;
                    ReleaseAttack(player);
                }
            }
        }, w);
        ReleaseAttack(player);
        var weapon = bash != null ? bash.m_weapon : null;
        c.Check(w.Met && bash != null && ReferenceEquals(weapon, shield),
            $"{item}: pressing attack with the tower shield starts a shield bash that reaches {what} within 6 s (swing started {bash != null}, "
            + $"its weapon is the shield {ReferenceEquals(weapon, shield)}, hit {w.Met})");
        if (w.Met)
        {
            yield return Until(() => Provoked(g, player) && g.m_targetCreature == player && FearOf(g) == null && g.IsAlerted(), 1.5f, hold, w);
            c.Check(w.Met, $"{item}: the bash on {what} makes it fight: provoked, the player its target, not running "
                           + $"(provoked {Provoked(g, player)}, target {Who(g.m_targetCreature)}, runs from {Who(FearOf(g))}, alerted {g.IsAlerted()})");
            var ranAgain = false;
            yield return Wait(1.5f, () =>
            {
                hold();
                ranAgain |= FearOf(g) != null;
            });
            c.Check(!ranAgain && g.m_targetCreature == player, $"{item}: it keeps fighting the player for the next 1.5 s (ran again {ranAgain}, target {Who(g.m_targetCreature)})");
        }
        s.Destroy(g);
        // The bash wait 2 s before the next one; the shield go back before the next step equips its own.
        yield return Wait(2.2f, null);
        if (player.IsItemEquiped(shield))
        {
            player.UnequipItem(shield, false);
        }
        player.GetInventory().RemoveItem(shield);
    }

    // ================================================================ morale.x.tower-bash-ngplus

    private static IEnumerator RunTowerBashNgPlus()
    {
        if (!OtherModOn(TowerNgName, TowerGuid, "MC Tower Shield Wall", "X05 (New Game+)"))
        {
            yield break;
        }
        var c = new Checks(TowerNgName);
        Stage s = null;
        var w = new Waiter();
        var zones = ZoneSystem.instance;
        var hadLevel = false;
        var oldLevel = 0f;
        var changed = false;
        try
        {
            s = Stage.Begin(TowerNgName, c);
            if (s == null || zones == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            hadLevel = zones.GetGlobalKey(GlobalKeys.WorldLevel, out oldLevel);
            changed = true;
            zones.SetGlobalKey(GlobalKeys.WorldLevel, 1f);
            yield return Until(() => Game.m_worldLevel == 1, 3f, null, w);
            if (!c.Check(w.Met, $"X05 setup: the world is a New Game+ world (world level {Game.m_worldLevel}, expected 1)"))
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = NewRules(r => r.CorneredRange = 0f);
            Standing.TestOverride = new Standing.Override(4);
            var shield = s.EquipWeapon("ShieldWoodTower");
            var pos = Stage.Ground(Ahead(s, 1.3f));
            // It faces away from the silent player (its T23 pair: "crouch up behind an unaware one"): unalerted, so a
            // sneak attack on it stay possible until the bash make it fight. A Greydwarf that already runs is alerted:
            // no hit on it is ever a sneak attack, and the knife check below could never fail.
            var g = s.Spawn("Greydwarf", pos, pos - player.transform.position);
            if (!c.Check(shield != null && player.IsItemEquiped(shield) && g != null, "X05: could not equip a wood tower shield or spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            Tough(g.m_character);
            var bashed = false;
            Action hold = () =>
            {
                if (bashed)
                {
                    // After the bash only its place is held: it turns, sees and fights as the game makes it.
                    Stage.Place(g.m_character, pos, Vector3.zero);
                    return;
                }
                s.Silence();
                Stage.Place(g.m_character, pos, pos - player.transform.position);
            };
            yield return Wait(1.2f, hold);
            hold();
            c.Check(Attitudes.Judge(g, player) == Attitude.Afraid && !g.IsAlerted() && FearOf(g) == null && !Provoked(g, player) && !g.CanSeeTarget(player),
                $"X05 setup: in the New Game+ world the afraid Greydwarf has not noticed the silent rank 4 player behind it, so a hit on it now could be a sneak attack "
                + $"(alerted {g.IsAlerted()}, sees the player {g.CanSeeTarget(player)}, runs from {Who(FearOf(g))})");
            // Sneak-attack mark of the creature before anything hits it: the bash and the knife must both leave it.
            var backstab = g.m_character.m_backstabTime;
            ClearHit(g);
            var health = g.m_character.GetHealth();
            Attack bash = null;
            var idle = player.m_currentAttack;
            yield return Until(() => WasHit(g), 6f, () =>
            {
                hold();
                if (bash == null)
                {
                    PressAttack(player);
                    var current = player.m_currentAttack;
                    if (current != null && !ReferenceEquals(current, idle))
                    {
                        bash = current;
                        ReleaseAttack(player);
                    }
                }
            }, w);
            ReleaseAttack(player);
            bashed = true;
            var bashWeapon = bash != null ? bash.m_weapon : null;
            if (c.Check(w.Met && bash != null && ReferenceEquals(bashWeapon, shield),
                    $"X05: in the New Game+ world the swing that reaches the unaware afraid Greydwarf within 6 s is the tower shield's bash "
                    + $"(swing started {bash != null}, its weapon is the shield {ReferenceEquals(bashWeapon, shield)}, hit {w.Met})"))
            {
                SelfTest.Note(TowerNgName, $"X05: the bash took {F(health - g.m_character.GetHealth())} health off it in the world level 1 world");
                yield return Until(() => Provoked(g, player) && g.m_targetCreature == player && FearOf(g) == null, 1.5f, hold, w);
                c.Check(w.Met, $"X05: in a New Game+ world a bash on an afraid Greydwarf provokes it too: it fights the player "
                               + $"(provoked {Provoked(g, player)}, target {Who(g.m_targetCreature)}, runs from {Who(FearOf(g))})");
                c.Check(g.m_character.m_backstabTime == backstab, "X05: in the New Game+ world the bash itself is no sneak attack (a sneak attack by the bash would also rule out the knife's)");
                // The knife right after the bash: the tower shield go away when the knife come out.
                yield return Wait(2.2f, hold);
                var knife = s.EquipWeapon("KnifeFlint");
                if (c.Check(knife != null && player.GetCurrentWeapon() == knife && knife.m_shared.m_backstabBonus > 1f, "X05: a flint knife (sneak-attack bonus) equipped after the bash"))
                {
                    yield return Wait(0.6f, hold);
                    ClearHit(g);
                    // The swing whose hit lands (the press is kept until then, like a held attack button).
                    Attack stab = null;
                    yield return Until(() => WasHit(g), 5f, () =>
                    {
                        hold();
                        if (!WasHit(g))
                        {
                            PressAttack(player);
                        }
                        else if (stab == null)
                        {
                            stab = player.m_currentAttack;
                        }
                    }, w);
                    ReleaseAttack(player);
                    var stabWeapon = stab != null ? stab.m_weapon : null;
                    c.Check(w.Met && ReferenceEquals(stabWeapon, knife) && g.m_character.m_backstabTime == backstab,
                        $"X05: a knife hit right after the bash is not a sneak attack (hit landed {w.Met}, by the knife's swing {ReferenceEquals(stabWeapon, knife)}, "
                        + $"sneak attack {g.m_character.m_backstabTime != backstab})");
                }
            }
            s.Destroy(g);
            c.Report();
        }
        finally
        {
            ReleaseAttack(Player.m_localPlayer);
            Finish(s);
            if (changed && zones != null)
            {
                if (hadLevel)
                {
                    zones.SetGlobalKey(GlobalKeys.WorldLevel, oldLevel);
                }
                else
                {
                    zones.RemoveGlobalKey(GlobalKeys.WorldLevel);
                }
            }
        }
    }

    // ================================================================ morale.x.dual-wield

    private static IEnumerator RunDualWield()
    {
        if (!OtherModOn(DualWieldName, DualWieldGuid, "MC Dual Wielding", "X07"))
        {
            yield break;
        }
        var c = new Checks(DualWieldName);
        Stage s = null;
        var w = new Waiter();
        try
        {
            s = Stage.Begin(DualWieldName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            ServerRules.TestRules = NewRules(r => r.CorneredRange = 0f);
            Standing.TestOverride = new Standing.Override(4);
            var sword = s.EquipWeapon("SwordIron");
            var mace = s.EquipWeapon("MaceIron");
            yield return Wait(0.6f, null);
            if (!c.Check(sword != null && mace != null && ReferenceEquals(player.m_rightItem, sword) && ReferenceEquals(player.m_leftItem, mace)
                         && sword.m_shared.m_skillType != mace.m_shared.m_skillType,
                    "X07: an iron sword in the main hand and an iron mace in the off hand (Dual Wielding pairs the second weapon)"))
            {
                c.Report();
                yield break;
            }
            var farSpot = s.Spot(here, s.Right, 6f, true);
            var nearSpot = Stage.Ground(Ahead(s, 1.3f));
            var g = s.Spawn("Greydwarf", farSpot, here - farSpot);
            if (!c.Check(g != null, "X07: could not spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            Tough(g.m_character);
            var near = false;
            Action hold = () =>
            {
                s.Noise();
                var spot = near ? nearSpot : farSpot;
                Stage.Place(g.m_character, spot, player.transform.position - spot);
            };
            yield return Until(() => FearOf(g) == player && g.IsAlerted(), 4f, hold, w);
            c.Check(w.Met && !Provoked(g, player), $"X07 setup: the afraid Greydwarf 6 m to the side runs from the rank 4 player (runs from {Who(FearOf(g))})");

            // First swing in the air (the creature stay 6 m away), the second swing of the combo hits it.
            ClearHit(g);
            var idle = player.m_currentAttack;
            Attack first = null;
            Attack second = null;
            Attack hitBy = null;
            var firstTouched = false;
            var secondLevel = -1;
            var skill = Skills.SkillType.None;
            yield return Until(() => hitBy != null, 10f, () =>
            {
                PressAttack(player);
                var current = player.m_currentAttack;
                if (first == null && current != null && !ReferenceEquals(current, idle))
                {
                    first = current;
                }
                else if (first != null && second == null && current != null && !ReferenceEquals(current, first))
                {
                    second = current;
                    secondLevel = current.m_currentAttackCainLevel;
                    firstTouched = WasHit(g);
                    ClearHit(g);
                    near = true;
                }
                hold();
                if (second != null && hitBy == null && WasHit(g))
                {
                    hitBy = player.m_currentAttack;
                    var last = g.m_character.m_lastHit;
                    skill = last != null ? last.m_skill : Skills.SkillType.None;
                    ReleaseAttack(player);
                }
            }, w);
            ReleaseAttack(player);
            c.Check(first != null && second != null && !firstTouched && secondLevel == 1,
                $"X07: the first swing of the pair hits nothing and the next swing is the second of the combo (first swing {first != null}, it touched the Greydwarf {firstTouched}, "
                + $"second swing {second != null}, its place in the combo {secondLevel + 1})");
            if (c.Check(w.Met && ReferenceEquals(hitBy, second) && skill == mace.m_shared.m_skillType,
                    $"X07: that second swing hits the Greydwarf with the off-hand weapon only (hit {w.Met}, by the second swing {ReferenceEquals(hitBy, second)}, "
                    + $"skill of the hit {skill}, off-hand weapon's skill {mace.m_shared.m_skillType}, main-hand weapon's skill {sword.m_shared.m_skillType})"))
            {
                yield return Until(() => Provoked(g, player) && g.m_targetCreature == player && FearOf(g) == null && g.IsAlerted(), 1.5f, hold, w);
                c.Check(w.Met, $"X07: the off-hand hit makes the afraid Greydwarf fight the player, like a main-hand hit "
                               + $"(provoked {Provoked(g, player)}, target {Who(g.m_targetCreature)}, runs from {Who(FearOf(g))})");
            }
            s.Destroy(g);
            c.Report();
        }
        finally
        {
            ReleaseAttack(Player.m_localPlayer);
            Finish(s);
        }
    }

    // ================================================================ morale.x.moveset

    // A swing other than `before` started by the held attack press; `how` get the frame it started in.
    private static IEnumerator SwingAfter(Player player, Attack before, float timeout, Action eachFrame, Action<Attack> how, Waiter w)
    {
        Attack started = null;
        yield return Until(() => started != null, timeout, () =>
        {
            eachFrame?.Invoke();
            PressAttack(player);
            var current = player.m_currentAttack;
            if (started == null && current != null && !ReferenceEquals(current, before))
            {
                started = current;
                how(current);
                ReleaseAttack(player);
            }
        }, w);
        ReleaseAttack(player);
    }

    // Weapon Moveset turn the swing's own copy of the attack into a move: its animation is a single named swing of
    // the combo (no combo steps left on the copy), while the weapon's attack stay a combo.
    private static bool IsMove(Attack swing, ItemDrop.ItemData weapon) =>
        swing != null && weapon != null && swing.m_attackChainLevels == 0 && weapon.m_shared.m_attack.m_attackChainLevels > 1
        && swing.m_attackAnimation != weapon.m_shared.m_attack.m_attackAnimation;

    private static IEnumerator RunMoveset()
    {
        if (!OtherModOn(MovesetName, MovesetGuid, "MC Weapon Moveset", "X08"))
        {
            yield break;
        }
        var c = new Checks(MovesetName);
        Stage s = null;
        var w = new Waiter();
        SneakSkill sneak = null;
        var local = Player.m_localPlayer;
        try
        {
            s = Stage.Begin(MovesetName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            sneak = new SneakSkill(player);
            // The item also asks what MC Sneak Ambush pays: without that mod the run proves nothing about its XP, so
            // the test fails (the sneak attack steps still run and report).
            var ambush = ModOn(SneakAmbushGuid);
            c.Check(ambush, "X08: MC Sneak Ambush is not loaded and active in this run: its Sneak XP (none for the roll attack it watches, paid for the one from behind) "
                            + "was not checked");
            Standing.TestOverride = new Standing.Override(3);
            var sword = s.EquipWeapon("SwordIron");
            yield return Wait(0.6f, null);
            if (!c.Check(sword != null && player.GetCurrentWeapon() == sword && sword.m_shared.m_backstabBonus > 1f,
                    "X08: an iron sword (it has a sneak-attack bonus) added and equipped"))
            {
                c.Report();
                yield break;
            }

            // ----- a jump attack on an afraid Greyling that runs: it fight -----
            ServerRules.TestRules = NewRules(r => r.CorneredRange = 0f);
            var runner = SpawnMeadows(s, c, "Greyling", Stage.Ground(Ahead(s, 1.3f)), -s.Forward);
            if (runner != null)
            {
                Tough(runner.m_character);
                // Held at arm's length in front of the player, at the player's own height (also in the air).
                Action hold = () =>
                {
                    s.Noise();
                    Stage.Place(runner.m_character, Ahead(s, 1.3f), -s.Forward);
                };
                yield return Until(() => FearOf(runner) == player && runner.IsAlerted(), 4f, hold, w);
                c.Check(w.Met && !Provoked(runner, player), $"X08 setup: the afraid Greyling runs from the rank 3 player (runs from {Who(FearOf(runner))})");
                ClearHit(runner);
                var before = player.m_currentAttack;
                player.Jump();
                yield return Until(() => !player.IsOnGround(), 1.5f, hold, w);
                var jumped = w.Met;
                Attack move = null;
                var inAir = false;
                yield return SwingAfter(player, before, 2f, hold, a =>
                {
                    move = a;
                    inAir = !player.IsOnGround();
                }, w);
                var animation = move != null ? move.m_attackAnimation : "none";
                c.Check(jumped && move != null && inAir && IsMove(move, sword),
                    $"X08: attack pressed during a jump starts Weapon Moveset's jump attack (jumped {jumped}, swing started {move != null}, in the air {inAir}, "
                    + $"its animation {animation}, the sword's combo {sword.m_shared.m_attack.m_attackAnimation})");
                yield return Until(() => WasHit(runner), 3f, hold, w);
                if (c.Check(w.Met, "X08: the jump attack hits the afraid Greyling within 3 s"))
                {
                    yield return Until(() => Provoked(runner, player) && runner.m_targetCreature == player && FearOf(runner) == null, 1.5f, hold, w);
                    c.Check(w.Met, $"X08: a jump attack on an afraid Greyling makes it fight the player (provoked {Provoked(runner, player)}, "
                                   + $"target {Who(runner.m_targetCreature)}, runs from {Who(FearOf(runner))})");
                }
                yield return Until(() => player.IsOnGround() && !player.InAttack(), 4f, hold, w);
                s.Destroy(runner);
            }

            // ----- sneak roll attacks: the Greyling watches the player, then it no see them -----
            // Fear off for these two (FearRange 0): what the hit do is asked, not the running.
            ServerRules.TestRules = NewRules(r =>
            {
                r.CorneredRange = 0f;
                r.FearRange = 0f;
            });
            foreach (var seen in new[] { true, false })
            {
                var what = seen ? "a sneak roll attack on an afraid Greyling that watches the player" : "a sneak roll attack from behind an afraid Greyling that does not see the player";
                yield return Wait(1.2f, null); // Weapon Moveset: two moves at least a second apart
                var facing = seen ? -s.Forward : s.Forward;
                var grey = SpawnMeadows(s, c, "Greyling", Stage.Ground(Ahead(s, 1.6f)), facing);
                if (grey == null)
                {
                    continue;
                }
                Tough(grey.m_character);
                Action hold = () =>
                {
                    s.Silence();
                    Stage.Place(grey.m_character, Stage.Ground(Ahead(s, 1.6f)), facing);
                };
                yield return Wait(1.2f, hold);
                hold();
                c.Check(Attitudes.Judge(grey, player) == Attitude.Afraid && !grey.IsAlerted() && grey.CanSeeTarget(player) == seen,
                    $"X08 setup ({(seen ? "seen" : "unseen")}): an afraid, unalerted Greyling 1.6 m in front that {(seen ? "sees" : "does not see")} the player "
                    + $"(alerted {grey.IsAlerted()}, sees the player {grey.CanSeeTarget(player)})");
                player.SetCrouch(true);
                yield return Until(() => player.IsCrouching(), 2f, hold, w);
                var crouched = w.Met;
                var backstab = grey.m_character.m_backstabTime;
                var xp = sneak.Xp;
                ClearHit(grey);
                var before = player.m_currentAttack;
                player.Dodge(s.Forward);
                yield return Until(() => player.InDodge(), 1.5f, hold, w);
                var rolled = w.Met;
                Attack move = null;
                yield return SwingAfter(player, before, 3f, hold, a => move = a, w);
                var animation = move != null ? move.m_attackAnimation : "none";
                c.Check(crouched && rolled && move != null && IsMove(move, sword),
                    $"X08: {what}: attack pressed during the roll starts Weapon Moveset's roll attack (crouched {crouched}, rolled {rolled}, swing started {move != null}, "
                    + $"its animation {animation})");
                yield return Until(() => WasHit(grey), 3f, hold, w);
                if (c.Check(w.Met, $"X08: {what}: the roll attack hits it within 3 s"))
                {
                    yield return Wait(0.3f, hold); // the XP comes over the game's own message path
                    var sneakAttack = grey.m_character.m_backstabTime != backstab;
                    var gained = sneak.Xp - xp;
                    if (seen)
                    {
                        c.Check(!sneakAttack, $"X08: {what} is no sneak attack");
                        c.Check(gained < SneakXpPaid, $"X08: {what} pays no Sneak Ambush XP (Sneak XP gained {F(gained)})");
                    }
                    else
                    {
                        c.Check(sneakAttack, $"X08: {what} is a sneak attack");
                        if (ambush)
                        {
                            c.Check(gained >= SneakXpPaid, $"X08: {what} pays Sneak Ambush's XP (Sneak XP gained {F(gained)})");
                        }
                    }
                    c.Check(Provoked(grey, player), $"X08: {what} provokes it (provoked {Provoked(grey, player)})");
                }
                player.SetCrouch(false);
                yield return Until(() => !player.InDodge() && !player.InAttack(), 4f, hold, w);
                s.Destroy(grey);
            }
            c.Report();
        }
        finally
        {
            ReleaseAttack(local);
            if (local != null)
            {
                local.SetCrouch(false);
            }
            if (sneak != null)
            {
                sneak.Restore();
            }
            Finish(s);
        }
    }

    // ================================================================ morale.x.harpoon-hook

    private static IEnumerator RunHarpoonHook()
    {
        if (!OtherModOn(HarpoonHookName, HarpoonGuid, "MC Harpoon Hooks Tames", "X06"))
        {
            yield break;
        }
        var c = new Checks(HarpoonHookName);
        Stage s = null;
        var w = new Waiter();
        try
        {
            s = Stage.Begin(HarpoonHookName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            ServerRules.TestRules = NewRules(null);
            Standing.TestOverride = new Standing.Override(3);
            var harpoon = ItemOf("SpearChitin");
            var attack = harpoon != null ? harpoon.m_shared.m_attack : null;
            var prefab = attack != null ? attack.m_attackProjectile : null;
            if (prefab == null && harpoon != null && harpoon.m_shared.m_secondaryAttack != null)
            {
                attack = harpoon.m_shared.m_secondaryAttack;
                prefab = attack.m_attackProjectile;
            }
            var effect = harpoon != null ? harpoon.m_shared.m_attackStatusEffect : null;
            if (!c.Check(prefab != null && prefab.GetComponent<Projectile>() != null && effect != null,
                    "X06: the SpearChitin (harpoon) has a projectile and carries the game's hook effect"))
            {
                c.Report();
                yield break;
            }
            var hook = effect.NameHash();

            // ----- a wild afraid Boar: the harpoon hooks it and it fight -----
            var wildSpot = s.Spot(here, s.Forward, 15f, true);
            var wild = SpawnMeadows(s, c, "Boar", wildSpot, here - wildSpot);
            if (wild != null)
            {
                Tough(wild.m_character);
                Action holdWild = () => Stage.Place(wild.m_character, wildSpot, player.transform.position - wildSpot);
                yield return Wait(1.2f, holdWild);
                c.Check(Attitudes.Judge(wild, player) == Attitude.Afraid && !wild.IsAlerted(), "X06 setup: an afraid, calm wild Boar 15 m from the rank 3 player");
                Fire(s, prefab, wild.m_character.GetCenterPoint() + Vector3.up * 2.5f, Vector3.down * 30f, attack.m_attackHitNoise, WeaponHit(player, harpoon, null), null, null);
                yield return Until(() => Provoked(wild, player) && wild.m_targetCreature == player, 2.5f, holdWild, w);
                c.Check(w.Met, $"X06: a harpoon on an afraid wild Boar makes it fight the player within 2.5 s (provoked {Provoked(wild, player)}, target {Who(wild.m_targetCreature)}, "
                               + $"hooked {wild.m_character.GetSEMan().HaveStatusEffect(hook)})");
                s.Destroy(wild);
                yield return Wait(0.5f, null);
            }

            // ----- the player's own tame, afraid Boars 6 m from it -----
            var tameSpot = s.Spot(here, s.Forward, 15f, true);
            var away = (tameSpot - here).normalized;
            var side = Vector3.Cross(Vector3.up, away);
            var spotA = Stage.Ground(tameSpot + away * 6f);
            var spotB = Stage.Ground(tameSpot + side * 6f);
            var tame = SpawnMeadows(s, c, "Boar", tameSpot, here - tameSpot);
            var a = SpawnMeadows(s, c, "Boar", spotA, here - spotA);
            var b = SpawnMeadows(s, c, "Boar", spotB, here - spotB);
            if (tame == null || a == null || b == null)
            {
                c.Report();
                yield break;
            }
            tame.MakeTame();
            Tough(tame.m_character);
            Action holdTame = () =>
            {
                Stage.Place(tame.m_character, tameSpot, player.transform.position - tameSpot);
                Stage.Place(a.m_character, spotA, player.transform.position - spotA);
                Stage.Place(b.m_character, spotB, player.transform.position - spotB);
            };
            yield return Wait(1.2f, holdTame);
            c.Check(tame.m_character.IsTamed() && !tame.m_character.GetSEMan().HaveStatusEffect(hook)
                    && Attitudes.Judge(a, player) == Attitude.Afraid && !a.IsAlerted() && Attitudes.Judge(b, player) == Attitude.Afraid && !b.IsAlerted(),
                "X06 setup: a tame Boar (not hooked) and two afraid, calm wild Boars 6 m from it (beyond NearMissRange)");
            var health = tame.m_character.GetHealth();
            Fire(s, prefab, tame.m_character.GetCenterPoint() + Vector3.up * 2.5f, Vector3.down * 30f, attack.m_attackHitNoise, WeaponHit(player, harpoon, null), null, null);
            var stirred = false;
            Action watch = () =>
            {
                holdTame();
                stirred |= Provoked(a, player) || a.IsAlerted() || a.m_targetCreature != null || Provoked(b, player) || b.IsAlerted() || b.m_targetCreature != null;
            };
            yield return Until(() => tame.m_character.GetSEMan().HaveStatusEffect(hook), 2.5f, watch, w);
            var hooked = w.Met;
            var line = hooked ? tame.m_character.GetSEMan().GetStatusEffect(hook) as SE_Harpooned : null;
            c.Check(hooked && line != null && line.m_attacker == player,
                $"X06: with MC Harpoon Hooks Tames the harpoon hooks the player's own tame within 2.5 s (hooked {hooked}, the line is the player's {line != null && line.m_attacker == player})");
            yield return Wait(1.5f, watch);
            c.Check(tame.m_character.IsTamed() && tame.m_character.GetHealth() >= health && tame.m_targetCreature != player && !Provoked(tame, player),
                $"X06: the hooked tame is unhurt, stays tame and does not turn on the player (health {F(health)} -> {F(tame.m_character.GetHealth())}, target {Who(tame.m_targetCreature)})");
            c.Check(!stirred, "X06: the afraid Boars 6 m from the hooked tame do not come for the player: never alerted, never provoked, no target "
                              + $"(first: alerted {a.IsAlerted()}, target {Who(a.m_targetCreature)}; second: alerted {b.IsAlerted()}, target {Who(b.m_targetCreature)})");
            s.Destroy(tame);
            s.Destroy(a);
            s.Destroy(b);
            c.Report();
        }
        finally
        {
            Finish(s);
        }
    }

    // ================================================================ morale.x.encyclopedia

    // Lines one log source write during a call (BepInEx call every listener inside the log call).
    private sealed class LineCatcher : ILogListener
    {
        internal readonly List<string> Lines = new List<string>();
        private readonly string _source;

        internal LineCatcher(string source)
        {
            _source = source;
        }

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            try
            {
                if (eventArgs != null && eventArgs.Source != null && eventArgs.Source.SourceName == _source && eventArgs.Data != null)
                {
                    Lines.Add(eventArgs.Data.ToString());
                }
            }
            catch
            {
                // Me never throw inside the logger.
            }
        }

        public void Dispose()
        {
        }
    }

    // What MC Encyclopedia prints for that creature: its Debug console command write the entry as its window shows
    // it ("<title> | <subtitle>", then the detail lines). Title = the creature's name when known, "???" when not.
    // Null = no entry text came (raw say what did).
    private static string EncyclopediaTitle(string prefab, out string raw)
    {
        raw = "";
        var console = global::Console.instance;
        var view = FeatureRegistry.Find(EncyclopediaGuid);
        if (console == null || view == null)
        {
            raw = "no console or no Encyclopedia";
            return null;
        }
        var catcher = new LineCatcher(view.Value.Name);
        BepInEx.Logging.Logger.Listeners.Add(catcher);
        try
        {
            console.TryRunCommand(EncyclopediaCommand + " " + prefab, true, true);
        }
        finally
        {
            BepInEx.Logging.Logger.Listeners.Remove(catcher);
        }
        raw = catcher.Lines.Count > 0 ? catcher.Lines[catcher.Lines.Count - 1] : "the command wrote nothing";
        var first = raw.Replace("\r", "").Split('\n')[0];
        var bar = first.IndexOf(" | ", StringComparison.Ordinal);
        return bar > 0 ? first.Substring(0, bar).Trim() : null;
    }

    private static IEnumerator RunEncyclopedia()
    {
        if (!OtherModOn(EncyclopediaName, EncyclopediaGuid, "MC Encyclopedia", "X02"))
        {
            yield break;
        }
        var c = new Checks(EncyclopediaName);
        Stage s = null;
        var w = new Waiter();
        var plate = new PlateRef();
        var local = Player.m_localPlayer;
        string record = null;
        string token = null;
        try
        {
            s = Stage.Begin(EncyclopediaName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            const string prefab = "Boar";
            token = TokenOf(prefab);
            if (!c.Check(!string.IsNullOrEmpty(token) && Localization.instance != null && Terminal.commands.ContainsKey(EncyclopediaCommand),
                    $"X02: the Boar has a name and MC Encyclopedia's Debug console command {EncyclopediaCommand} exists"))
            {
                c.Report();
                yield break;
            }
            var normal = Localization.instance.Localize(token);
            ServerRules.TestRules = NewRules(null);
            Standing.TestOverride = new Standing.Override(3);

            // Never met: the record of this character forgets the Boar (put back at the end if it be not met again).
            record = ForgetInEncyclopedia(player, token);
            string raw = null;
            string title = null;
            // Its catalog is built in the background after the world loads: asked again for a few seconds.
            for (var tries = 0; tries < 12 && title == null; tries++)
            {
                title = EncyclopediaTitle(prefab, out raw);
                if (title == null)
                {
                    yield return Wait(0.5f, null);
                }
            }
            if (!c.Check(title != null, $"X02: MC Encyclopedia has an entry for the Boar (its command answered: \"{Short(raw)}\")"))
            {
                c.Report();
                yield break;
            }
            var hidden = !string.Equals(title, normal, StringComparison.OrdinalIgnoreCase);
            SelfTest.Note(EncyclopediaName, hidden
                ? $"X02: before the meeting the Encyclopedia lists the never-met Boar as \"{title}\""
                : "X02: the Encyclopedia shows the Boar by name before the meeting (it shows every entry in this game: reveal-all setting); only the 'met' record tells the meeting");
            c.Check(!MetInEncyclopedia(player, token), "X02 setup: the Boar is not met for MC Encyclopedia before the test shows it");

            var distance = 8f;
            var spot = s.Spot(here, s.Forward, distance, true);
            var boar = SpawnMeadows(s, c, prefab, spot, here - spot);
            if (boar == null)
            {
                c.Report();
                yield break;
            }
            var direction = (spot - here).normalized;
            Action hold = () =>
            {
                s.Noise();
                var at = Stage.Ground(here + direction * distance);
                Stage.Place(boar.m_character, at, player.transform.position - at);
            };
            yield return Until(() => FearOf(boar) == player && boar.IsAlerted(), 4f, hold, w);
            c.Check(w.Met, $"X02: the afraid Boar 8 m away runs from the rank 3 player (runs from {Who(FearOf(boar))})");
            yield return PlateShows(boar.m_character, true, 3f, hold, w, plate);
            c.Check(w.Met, $"X02: looked at while it runs, its health bar shows with the alert icon ({DescribeIcons(plate.Data)})");
            yield return Until(() => MetInEncyclopedia(player, token), 2f, hold, w);
            title = EncyclopediaTitle(prefab, out raw);
            c.Check(w.Met && string.Equals(title, normal, StringComparison.OrdinalIgnoreCase),
                $"X02: seen while it runs, the afraid Boar counts as met and the Encyclopedia shows its normal name \"{normal}\" (met {w.Met}, it shows \"{title}\")");
            c.Check(PlateClean(plate.Data, boar.m_character), $"X02: its health bar reads the normal name only (texts \"{PlateWords(plate.Data)}\")");

            distance = 18f;
            yield return Until(() => FearOf(boar) == null && !boar.IsAlerted(), 6f, hold, w);
            c.Check(w.Met, $"X02: 18 m away it calms down (runs from {Who(FearOf(boar))}, alerted {boar.IsAlerted()})");
            yield return PlateShows(boar.m_character, false, 3f, hold, w, plate);
            title = EncyclopediaTitle(prefab, out raw);
            c.Check(w.Met && MetInEncyclopedia(player, token) && string.Equals(title, normal, StringComparison.OrdinalIgnoreCase),
                $"X02: looked at when calm (no alert icon: {DescribeIcons(plate.Data)}), it is still met and listed as \"{title}\"");
            s.Destroy(boar);
            c.Report();
        }
        finally
        {
            // Not met again (test cut short): the character's record as it was.
            if (record != null && token != null && local != null && !MetInEncyclopedia(local, token) && local.m_customData != null)
            {
                local.m_customData[EncyclopediaSeenKey] = record;
            }
            Finish(s);
        }
    }

    // ================================================================ morale.x.kill-counts

    // The Compendium's "Player Statistics" text as the game build it when the Compendium opens (TextsDialog list;
    // MC Creature Kill and Tame Counts put its Creatures section on top of it). The dialog's own list is put back.
    private static string PlayerStatisticsPage(out string problem)
    {
        problem = null;
        var gui = InventoryGui.instance;
        var dialog = gui != null ? gui.m_textsDialog : null;
        if (dialog == null || Localization.instance == null || Player.m_localPlayer == null)
        {
            problem = "no Compendium dialog";
            return null;
        }
        var kept = new List<TextsDialog.TextInfo>(dialog.m_texts);
        try
        {
            dialog.UpdateTextsList();
            var topic = Localization.instance.Localize("$inventory_stats");
            for (var i = dialog.m_texts.Count - 1; i >= 0; i--)
            {
                var entry = dialog.m_texts[i];
                if (entry != null && entry.m_topic == topic)
                {
                    return entry.m_text ?? "";
                }
            }
            problem = $"no \"{topic}\" entry in the Compendium";
            return null;
        }
        finally
        {
            dialog.m_texts.Clear();
            dialog.m_texts.AddRange(kept);
        }
    }

    private static IEnumerator RunKillCounts()
    {
        if (!OtherModOn(KillCountsName, KillCountsGuid, "MC Creature Kill and Tame Counts", "X03"))
        {
            yield break;
        }
        var c = new Checks(KillCountsName);
        Stage s = null;
        var w = new Waiter();
        try
        {
            s = Stage.Begin(KillCountsName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            var profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
            var table = profile != null ? Standing.LifetimeKills(profile) : null;
            var greyling = TokenOf("Greyling");
            if (!c.Check(table != null && !string.IsNullOrEmpty(greyling) && MessageHud.instance != null && !Hud.IsUserHidden() && Localization.instance != null,
                    "X03: the HUD is hidden, the profile has no kill table or the Greyling has no name: nothing to compare"))
            {
                c.Report();
                yield break;
            }
            var name = Localization.instance.Localize(greyling);
            var n = table.TryGetValue(greyling, out var had) ? (int)had : 0;

            // The kill step of T05: the next Greyling kill reach it, and its message name the count it used.
            Standing.TestMessages = true;
            ServerRules.TestRules = NewRules(r => r.KillSteps = (n + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Standing.TestOverride = new Standing.Override(2) { ProfileKills = true };
            var text = KillStepText(greyling, n + 1);
            var shown = Shown(text);
            var spot = s.Spot(here, s.Forward, 5f, true);
            var victim = SpawnMeadows(s, c, "Greyling", spot, here - spot);
            if (victim == null)
            {
                c.Report();
                yield break;
            }
            yield return Wait(0.3f, null);
            yield return KillCredited(s, victim, table, greyling, n + 1, w);
            yield return null;
            var count = table.TryGetValue(greyling, out var now) ? (int)now : 0;
            if (!c.Check(w.Met && count == n + 1 && Shown(text) == shown + 1,
                    $"X03 setup (T05): the Greyling kill is credited and the kill step message names the count it used: \"{text}\" "
                    + $"(count {n} -> {count}, message shown {Shown(text) - shown} time(s))"))
            {
                c.Report();
                yield break;
            }
            var page = PlayerStatisticsPage(out var problem);
            if (!c.Check(page != null, $"X03: the Compendium's Player Statistics page could be built ({problem})"))
            {
                c.Report();
                yield break;
            }
            // Its line: "    Greyling: 12 killed (melee 11, other 1)".
            var match = Regex.Match(page, @"(?m)^\s*" + Regex.Escape(name) + @":\s*([0-9][0-9\s.,]*)\s+killed");
            var digits = match.Success ? Regex.Replace(match.Groups[1].Value, @"[^0-9]", "") : "";
            var shownCount = digits.Length > 0 && int.TryParse(digits, out var parsed) ? parsed : -1;
            c.Check(match.Success && shownCount == n + 1,
                $"X03: MC Creature Kill and Tame Counts shows the Greyling kill count the kill step used: its line on the Player Statistics page reads "
                + $"\"{(match.Success ? match.Value : "no Greyling line")}\" ({shownCount}), the kill step used {n + 1}");
            c.Report();
        }
        finally
        {
            Finish(s);
        }
    }

    // ================================================================ morale.x.sneak-xp

    private static IEnumerator RunSneakXp()
    {
        if (!OtherModOn(SneakXpName, SneakAmbushGuid, "MC Sneak Ambush", "X01"))
        {
            yield break;
        }
        var c = new Checks(SneakXpName);
        Stage s = null;
        var w = new Waiter();
        SneakSkill sneak = null;
        var local = Player.m_localPlayer;
        try
        {
            s = Stage.Begin(SneakXpName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            sneak = new SneakSkill(player);
            ServerRules.TestRules = NewRules(null);
            Standing.TestOverride = new Standing.Override(6);
            var bow = ItemOf("Bow");
            var arrow = ItemOf("ArrowWood");
            var arrowPrefab = arrow != null && arrow.m_shared.m_attack != null && arrow.m_shared.m_attack.m_attackProjectile != null
                ? arrow.m_shared.m_attack.m_attackProjectile
                : bow != null && bow.m_shared.m_attack != null ? bow.m_shared.m_attack.m_attackProjectile : null;
            if (!c.Check(bow != null && arrow != null && arrowPrefab != null && arrowPrefab.GetComponent<Projectile>() != null && bow.m_shared.m_backstabBonus > 1f,
                    "X01: the Bow (it has a sneak-attack bonus) and the ArrowWood projectile exist"))
            {
                c.Report();
                yield break;
            }

            // ----- standing, a shot at an afraid Greydwarf that faces the player from 17 m -----
            var farSpot = s.Spot(here, s.Forward, 17f, true);
            var watcher = SpawnMeadows(s, c, "Greydwarf", farSpot, here - farSpot);
            if (watcher != null)
            {
                Tough(watcher.m_character);
                Action hold = () => Stage.Place(watcher.m_character, farSpot, player.transform.position - farSpot);
                yield return Wait(1.2f, hold);
                hold();
                c.Check(Attitudes.Judge(watcher, player) == Attitude.Afraid && !watcher.IsAlerted() && FearOf(watcher) == null && watcher.CanSeeTarget(player),
                    $"X01 setup: an afraid Greydwarf 17 m away sees the standing rank 6 player and does not run yet (alerted {watcher.IsAlerted()}, "
                    + $"sees the player {watcher.CanSeeTarget(player)}, runs from {Who(FearOf(watcher))})");
                var backstab = watcher.m_character.m_backstabTime;
                var xp = sneak.Xp;
                ClearHit(watcher);
                var noise = bow.m_shared.m_attack.m_attackHitNoise + arrow.m_shared.m_attack.m_attackHitNoise;
                Fire(s, arrowPrefab, watcher.m_character.GetCenterPoint() + Vector3.up * 2.5f, Vector3.down * 30f, noise, WeaponHit(player, bow, arrow), bow, arrow);
                yield return Until(() => WasHit(watcher), 2.5f, hold, w);
                if (c.Check(w.Met, "X01: the arrow hits the afraid Greydwarf within 2.5 s"))
                {
                    yield return Wait(0.3f, hold);
                    c.Check(watcher.m_character.m_backstabTime == backstab, "X01: a shot at an afraid Greydwarf that watches the player is no sneak attack");
                    c.Check(sneak.Xp - xp < SneakXpPaid, $"X01: that shot pays no Sneak XP (gained {F(sneak.Xp - xp)})");
                    c.Check(Provoked(watcher, player) && watcher.m_targetCreature == player, $"X01: it fights the player (provoked {Provoked(watcher, player)}, target {Who(watcher.m_targetCreature)})");
                }
                s.Destroy(watcher);
            }

            // ----- crouched behind it, out of its sight: a stab is a sneak attack and pays -----
            var knife = s.EquipWeapon("KnifeFlint");
            if (c.Check(knife != null && player.GetCurrentWeapon() == knife && knife.m_shared.m_backstabBonus > 1f, "X01: a flint knife (sneak-attack bonus) added and equipped"))
            {
                yield return Wait(0.6f, null);
                var pos = Stage.Ground(Ahead(s, 1.3f));
                var victim = SpawnMeadows(s, c, "Greydwarf", pos, s.Forward);
                if (victim != null)
                {
                    Tough(victim.m_character);
                    Action hold = () =>
                    {
                        s.Silence();
                        Stage.Place(victim.m_character, pos, s.Forward);
                    };
                    player.SetCrouch(true);
                    yield return Until(() => player.IsCrouching(), 2f, hold, w);
                    yield return Wait(1.2f, hold);
                    hold();
                    c.Check(w.Met && Attitudes.Judge(victim, player) == Attitude.Afraid && !victim.IsAlerted() && !victim.CanSeeTarget(player) && FearOf(victim) == null,
                        $"X01 setup: the crouched player is behind an afraid Greydwarf that has not noticed them (crouching {w.Met}, alerted {victim.IsAlerted()}, "
                        + $"sees the player {victim.CanSeeTarget(player)})");
                    var backstab = victim.m_character.m_backstabTime;
                    var xp = sneak.Xp;
                    ClearHit(victim);
                    yield return Until(() => WasHit(victim), 5f, () =>
                    {
                        hold();
                        PressAttack(player);
                    }, w);
                    ReleaseAttack(player);
                    if (c.Check(w.Met, "X01: the stab from behind lands within 5 s"))
                    {
                        yield return Wait(0.3f, hold);
                        c.Check(victim.m_character.m_backstabTime != backstab, "X01: a hit from behind an afraid Greydwarf, crouched and out of its sight, is a sneak attack");
                        c.Check(sneak.Xp - xp >= SneakXpPaid, $"X01: that sneak attack pays MC Sneak Ambush's Sneak XP (gained {F(sneak.Xp - xp)})");
                        c.Check(Provoked(victim, player), $"X01: it is provoked by the stab (provoked {Provoked(victim, player)})");
                    }
                    player.SetCrouch(false);
                    s.Destroy(victim);
                }
            }
            c.Report();
        }
        finally
        {
            ReleaseAttack(local);
            if (local != null)
            {
                local.SetCrouch(false);
            }
            if (sneak != null)
            {
                sneak.Restore();
            }
            Finish(s);
        }
    }
}
#endif
