#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Combat.TrinketsOnDemandMod;

// Debug build only. Self tests with another MC mod doing its real thing (same class as SelfTests.cs; other mod found
// by GUID, its code only through the game's own effect or reflection), and one with a real arrow:
//   trinkets.dual-wield-creature  X01: two KnifeCopper dual wielded on a Greydwarf, Dual Wielding's hit pattern set
//                                 to both hands (its own test hook, reflection): two knife strikes = one paying
//                                 adrenaline call of the swing's own amount, a fight hit; full bar held
//   trinkets.moveset              X02: Weapon Moveset's jump attack and roll attack with SwordIron on a Greydwarf:
//                                 each pays like a normal sword swing (one call, same amount), counts as a fight,
//                                 and the fight pays income
//   trinkets.smoke-screen         X06: fight with a hunting Greydwarf (AI on), real Smoke Screen of Sneak Ambush
//                                 thrown at the feet: it loses the player, fight over by the mod's rule (about 6 s
//                                 after the last hit)
//   trinkets.morale-flee          X07: Creature Morale routs a Greyling (its Greydwarf Shaman killed by the player):
//                                 it flees, a hit on it starts a fight that ends about 6 s later while it still flees,
//                                 the next hit starts a new one
//   trinkets.morale-afraid        X07: Creature Morale's own setup (player with the last boss's standing, its test
//                                 hook by reflection): an afraid Greyling runs and never hunts; a fight next to it
//                                 (hit on a wild Boar) is over about 6 s after the hit; a hit on the Greyling starts
//                                 a new one
//   trinkets.pvp-dodge-arrow      M10: arrow of another player (stand-in, PvP on) meets the rolling player through
//                                 the game's own projectile hit code: game's perfect-dodge pay, fight on, income for
//                                 about 6 s, then over; other player's PvP off = arrow never counts
internal static partial class SelfTests
{
    private const string DualCreatureName = "trinkets.dual-wield-creature";
    private const string MovesetName = "trinkets.moveset";
    private const string SmokeName = "trinkets.smoke-screen";
    private const string MoraleFleeName = "trinkets.morale-flee";
    private const string MoraleAfraidName = "trinkets.morale-afraid";
    private const string DodgeArrowName = "trinkets.pvp-dodge-arrow";

    private const string MovesetGuid = "MC.Combat.Weapons.Moveset";
    private const string SneakGuid = "MC.Combat.Sneak.Ambush";
    private const string MoraleGuid = "MC.Combat.Creatures.Morale";

    // Names of other mods' things (their code: SmokeContent of Sneak Ambush; Packs default of Creature Morale).
    private const string SmokeItemName = "MC_SmokeScreen";
    private const string SmokeCloudName = "MC_SmokeScreen_cloud";
    private const string SmokeRadiusKey = SneakGuid + ".CloudRadius";
    private const string LeaderName = "Greydwarf_Shaman";

    // ---------- helpers ----------

    // Creature kept right in front of the player, at the player's own height (jump, roll: swing meets it wherever
    // the player is). AI off creature only. Called every physics tick while a swing is on its way.
    private static void HoldInFront(Player p, Character foe, Vector3 fwd, float distance)
    {
        var pos = p.transform.position + fwd * distance;
        foe.transform.position = pos;
        foe.m_body.position = pos;
        foe.m_body.linearVelocity = Vector3.zero;
    }

    private static void KillByPlayer(Player p, Character victim)
    {
        var hit = new HitData();
        hit.m_damage.m_damage = 1e7f;
        hit.m_point = victim.GetCenterPoint();
        hit.m_dir = Flat(victim.transform.position - p.transform.position);
        hit.SetAttacker(p);
        victim.Damage(hit);
    }

    // Live objects of a prefab that were not there before (other mod's cloud...).
    private static List<GameObject> NewObjects(string prefabName, HashSet<GameObject> before)
    {
        var list = new List<GameObject>();
        var scene = ZNetScene.instance;
        if (scene == null)
        {
            return list;
        }
        foreach (var view in scene.m_instances.Values)
        {
            if (view != null && view.gameObject.name.StartsWith(prefabName, StringComparison.Ordinal) && (before == null || !before.Contains(view.gameObject)))
            {
                list.Add(view.gameObject);
            }
        }
        return list;
    }

    // No real creature of the world hunts or fights the player right now (else fight timing proves nothing).
    private static IEnumerator QuietMoment(bool[] quiet)
    {
        quiet[0] = false;
        for (var attempt = 0; attempt < 10 && !quiet[0]; attempt++)
        {
            CombatState.Reset();
            yield return FixedTicks(2);
            quiet[0] = float.IsNegativeInfinity(CombatState.LastTargeted) && float.IsNegativeInfinity(CombatState.LastExchange);
            if (!quiet[0])
            {
                yield return new WaitForSeconds(1f);
            }
        }
    }

    // ---------- trinkets.dual-wield-creature (X01) ----------

    // Dual Wielding's rules with the hit pattern "both hands" (every swing event strikes with both weapons), put in
    // ITS memory through its own self-test hook (ServerRules.TestRules of a Debug build), by reflection: no type of
    // theirs here, no setting written. Restore puts back what was there.
    private sealed class BothHandsRules
    {
        private FieldInfo _testRules;
        private object _before;
        private bool _forced;

        internal string Problem = "";

        internal bool Force()
        {
            try
            {
                var serverRules = AccessTools.TypeByName("MC.Combat.WeaponsDualWieldMod.ServerRules");
                var rulesType = AccessTools.TypeByName("MC.Combat.WeaponsDualWieldMod.DualRules");
                _testRules = serverRules != null ? AccessTools.Field(serverRules, "TestRules") : null;
                var current = serverRules != null ? AccessTools.PropertyGetter(serverRules, "Current") : null;
                var pattern = rulesType != null ? AccessTools.Field(rulesType, "HitPattern") : null;
                var clone = AccessTools.Method(typeof(object), "MemberwiseClone");
                if (_testRules == null || current == null || pattern == null || clone == null)
                {
                    Problem = "its rules (ServerRules.TestRules, Current, DualRules.HitPattern of a Debug build) were not found";
                    return false;
                }
                var rules = current.Invoke(null, null);
                if (rules == null)
                {
                    Problem = "it has no rules in force";
                    return false;
                }
                var copy = clone.Invoke(rules, null);
                pattern.SetValue(copy, Enum.Parse(pattern.FieldType, "BothHands"));
                _before = _testRules.GetValue(null);
                _testRules.SetValue(null, copy);
                _forced = true;
                return true;
            }
            catch (Exception e)
            {
                Problem = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        internal void Restore()
        {
            if (_forced)
            {
                _forced = false;
                _testRules.SetValue(null, _before);
            }
        }
    }

    private static IEnumerator RunDualWieldCreature()
    {
        if (!ModActive(DualWieldGuid))
        {
            SelfTest.Fail(DualCreatureName, "Dual Wielding is not loaded and active: nothing of TESTING.md X01 was checked");
            yield break;
        }
        var rig = Rig.Create(DualCreatureName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(DualCreatureName);
        var mark = LogMark();
        var both = new BothHandsRules();
        try
        {
            var p = rig.P;
            // No income: every adrenaline call of this test is a swing's own.
            ServerRules.TestRules = new TrinketRules { IncomePerSecond = 0f };
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            var fwd = Flat(p.transform.forward);
            var foe = rig.Tough(FoeName, fwd * 1.2f);
            var first = rig.Give(KnifeName);
            var second = rig.Give(KnifeName);
            if (!c.Check(item != null && foe != null && first != null && second != null,
                    $"could not equip a trinket, give two {KnifeName} or spawn {FoeName}"))
            {
                c.Report();
                yield break;
            }
            var hash = item.m_shared.m_fullAdrenalineSE.NameHash();
            rig.Effect(hash);
            var max = p.GetMaxAdrenaline();
            rig.EmptyHands();
            rig.TakeControls();
            rig.Moves();
            p.EquipItem(first, false);
            p.EquipItem(second, false);
            yield return FixedTicks(3);
            if (!c.Check(ReferenceEquals(p.m_rightItem, first) && ReferenceEquals(p.m_leftItem, second),
                    $"two {KnifeName} are not held one in each hand (Dual Wielding active)"))
            {
                c.Report();
                yield break;
            }
            if (!c.Check(both.Force(), $"could not set Dual Wielding's hit pattern to both hands for the test: {both.Problem}"))
            {
                c.Report();
                yield break;
            }
            yield return Frames(3); // Dual Wielding takes new rules in its next frame
            // The game tells each strike that did damage to the creature: me count the player's.
            var struck = 0;
            Action<float, Character> count = (damage, attacker) =>
            {
                if (ReferenceEquals(attacker, p))
                {
                    struck++;
                }
            };
            foe.m_onDamaged += count;
            var started = new bool[1];

            // Both-hands swings on the creature from an empty bar: two knife strikes = ONE paying adrenaline call (the
            // second strike's call asks for 0), the bar gets exactly what that call gives, the hit counts as a fight.
            var hits = 0;
            for (var i = 0; i < 3; i++)
            {
                HoldInFront(p, foe, fwd, 1.2f);
                Face(p, fwd);
                yield return FixedTicks(2);
                p.m_adrenaline = 0f;
                CombatState.Reset();
                var calls = FullBar.Calls;
                var zeros = FullBar.ZeroCalls;
                struck = 0;
                var t0 = Time.time;
                yield return Swing(p, false, started);
                if (!started[0] || struck == 0)
                {
                    continue;
                }
                hits++;
                var paying = FullBar.Calls - calls - (FullBar.ZeroCalls - zeros);
                c.Check(struck >= 2 && struck % 2 == 0 && paying == struck / 2,
                    $"both-hands swing {i + 1} on a {FoeName}: {struck} knife strike(s), {paying} paying adrenaline call(s) out of {FullBar.Calls - calls} "
                    + "(expected one paying call for each two strikes of a both-hands hit)");
                var attack = p.m_currentAttack ?? p.m_previousAttack;
                if (attack != null && paying > 0)
                {
                    var want = 0f;
                    for (var n = 0; n < paying; n++)
                    {
                        want += GainAt(p, attack.m_attackAdrenaline * foe.m_enemyAdrenalineMultiplier, want);
                    }
                    c.Check(Near(p.m_adrenaline, want, 0.001f),
                        $"both-hands swing {i + 1}: bar 0 -> {F(p.m_adrenaline)}, expected {F(want)} ({paying} time(s) the swing's own {F(attack.m_attackAdrenaline)}, nothing on top)");
                }
                c.Check(CombatState.LastExchange >= t0, $"both-hands swing {i + 1} that hit a {FoeName} did not count as a fight hit");
            }
            c.Check(hits >= 2, $"only {hits} of 3 dual-wield swings hit the {FoeName}");

            // Full bar: a both-hands swing that hits is held.
            HoldInFront(p, foe, fwd, 1.2f);
            Face(p, fwd);
            yield return FixedTicks(2);
            p.m_adrenaline = max;
            var fullCalls = FullBar.Calls;
            struck = 0;
            yield return Swing(p, false, started);
            c.Check(started[0] && struck > 0 && FullBar.Calls > fullCalls, $"the dual-wield swing on a full bar did not hit the {FoeName}");
            c.Check(Near(p.m_adrenaline, max) && !rig.Has(hash), $"full bar + a both-hands swing that hits a {FoeName}: bar {F(p.m_adrenaline)} of {F(max)}, effect {rig.Has(hash)}");
            if (foe != null)
            {
                foe.m_onDamaged -= count;
            }
            CheckCleanLog(c, mark);
        }
        finally
        {
            both.Restore();
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.moveset (X02) ----------

    // What one driven move (jump attack or roll attack) left behind.
    private sealed class MoveRun
    {
        internal bool Started;     // a new attack started
        internal string Kind = ""; // Weapon Moveset's own name of the move it made of it: Jump, Roll, None
        internal bool Hit;         // the swing landed on the creature
        internal int Calls;        // adrenaline calls from attack start to the hit
        internal float Amount;     // last amount asked
        internal float Bar;        // bar right after the hit (emptied before)
        internal float StartAt;
    }

    // Weapon Moveset's last move through reflection (its Debug record MoveTracker.LastMove / LastStartAt).
    private sealed class MovesetProbe
    {
        private readonly FieldInfo _lastMove;
        private readonly FieldInfo _lastStartAt;
        private readonly FieldInfo _kind;

        internal MovesetProbe()
        {
            var tracker = AccessTools.TypeByName("MC.Combat.WeaponsMovesetMod.MoveTracker");
            _lastMove = tracker != null ? AccessTools.Field(tracker, "LastMove") : null;
            _lastStartAt = tracker != null ? AccessTools.Field(tracker, "LastStartAt") : null;
            _kind = _lastMove != null ? AccessTools.Field(_lastMove.FieldType, "Kind") : null;
        }

        internal bool Found => _lastMove != null && _lastStartAt != null && _kind != null;

        // Physics time the last move started (-1 none yet).
        internal float StartAt => (float)_lastStartAt.GetValue(null);

        internal string Kind => _kind.GetValue(_lastMove.GetValue(null)).ToString();
    }

    // Jump (the game's jump call, as the jump button makes it), buffered attack press in the air (what the attack
    // button leaves in the game for half a second), creature kept in front until the swing lands or is over.
    private static IEnumerator JumpAttack(Rig rig, Character foe, Vector3 fwd, MovesetProbe probe, MoveRun run)
    {
        var p = rig.P;
        yield return Until(() => Calm(p), 4f);
        HoldInFront(p, foe, fwd, 1.3f);
        Face(p, fwd);
        yield return FixedTicks(2);
        p.m_adrenaline = 0f;
        foe.m_localPlayerHasHit = false;
        var before = p.m_currentAttack;
        var moveBefore = probe.StartAt;
        var calls = FullBar.Calls;
        run.StartAt = Time.time;
        p.Jump();
        yield return FixedTicks(5);
        p.m_queuedAttackTimer = 0.5f;
        for (var t = 0f; t < 2.5f; t += Time.fixedDeltaTime)
        {
            HoldInFront(p, foe, fwd, 1.3f);
            yield return Fixed;
            var current = p.m_currentAttack;
            if (!run.Started && current != null && !ReferenceEquals(current, before))
            {
                run.Started = true;
                run.Kind = probe.StartAt != moveBefore ? probe.Kind : "None";
            }
            if (foe.m_localPlayerHasHit)
            {
                break;
            }
            if (run.Started && !p.InAttack() && p.IsOnGround() && t > 1f)
            {
                break;
            }
        }
        run.Hit = foe.m_localPlayerHasHit;
        run.Calls = FullBar.Calls - calls;
        run.Amount = FullBar.LastAmount;
        run.Bar = p.m_adrenaline;
        p.m_queuedAttackTimer = 0f;
        yield return Until(() => !p.InAttack() && p.IsOnGround(), 4f);
    }

    // Roll (the game's dodge call), attack pressed again and again while rolling (buffered press), creature kept in
    // front until the swing lands or is over.
    private static IEnumerator RollAttack(Rig rig, Character foe, Vector3 fwd, MovesetProbe probe, MoveRun run)
    {
        var p = rig.P;
        yield return Until(() => Calm(p), 4f);
        HoldInFront(p, foe, fwd, 1.3f);
        Face(p, fwd);
        yield return FixedTicks(2);
        p.m_adrenaline = 0f;
        foe.m_localPlayerHasHit = false;
        var before = p.m_currentAttack;
        var moveBefore = probe.StartAt;
        var calls = FullBar.Calls;
        run.StartAt = Time.time;
        p.Dodge(fwd);
        var rolled = false;
        for (var t = 0f; t < 4f; t += Time.fixedDeltaTime)
        {
            HoldInFront(p, foe, fwd, 1.3f);
            yield return Fixed;
            rolled |= p.InDodge();
            var current = p.m_currentAttack;
            if (!run.Started && current != null && !ReferenceEquals(current, before))
            {
                run.Started = true;
                run.Kind = probe.StartAt != moveBefore ? probe.Kind : "None";
            }
            if (!run.Started && p.InDodge())
            {
                p.m_queuedAttackTimer = 0.5f; // pressed inside the roll only: before it, the press would start a plain swing
            }
            if (!rolled && t > 1.5f)
            {
                break; // no roll came
            }
            if (foe.m_localPlayerHasHit)
            {
                break;
            }
            if (run.Started && !p.InAttack() && !p.InDodge() && t > 1.5f)
            {
                break;
            }
        }
        run.Hit = foe.m_localPlayerHasHit;
        run.Calls = FullBar.Calls - calls;
        run.Amount = FullBar.LastAmount;
        run.Bar = p.m_adrenaline;
        p.m_queuedAttackTimer = 0f;
        yield return Until(() => !p.InAttack() && !p.InDodge() && p.IsOnGround(), 4f);
    }

    private static IEnumerator RunMoveset()
    {
        if (!ModActive(MovesetGuid))
        {
            SelfTest.Fail(MovesetName, "Weapon Moveset is not loaded and active: nothing of TESTING.md X02 was checked");
            yield break;
        }
        var rig = Rig.Create(MovesetName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(MovesetName);
        var mark = LogMark();
        try
        {
            var p = rig.P;
            var probe = new MovesetProbe();
            if (!c.Check(probe.Found, "Weapon Moveset is active but its move record (MoveTracker.LastMove of a Debug build) was not found"))
            {
                c.Report();
                yield break;
            }
            // No income first: every adrenaline call is the swing's own.
            ServerRules.TestRules = new TrinketRules { IncomePerSecond = 0f };
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            var fwd = Flat(p.transform.forward);
            var foe = rig.Tough(FoeName, fwd * 1.3f);
            var sword = rig.Give(SwordName);
            if (!c.Check(item != null && foe != null && sword != null, $"could not equip a trinket, give {SwordName} or spawn {FoeName}"))
            {
                c.Report();
                yield break;
            }
            rig.Effect(item.m_shared.m_fullAdrenalineSE.NameHash());
            rig.EmptyHands();
            c.Check(p.EquipItem(sword, false), $"could not equip {SwordName}");
            rig.TakeControls();
            rig.Moves();
            yield return FixedTicks(3);
            var pay = sword.m_shared.m_attack.m_attackAdrenaline * foe.m_enemyAdrenalineMultiplier;
            c.Note($"a normal {SwordName} swing pays {F(pay)} per hit on a {FoeName}");

            // Each move, up to three tries (a swing can pass the creature): pays like a normal swing, counts as fight.
            foreach (var kind in new[] { "Jump", "Roll" })
            {
                MoveRun good = null;
                var tries = new List<string>();
                for (var i = 0; i < 3 && good == null; i++)
                {
                    CombatState.Reset();
                    // Weapon Moveset's own pause between two moves.
                    yield return new WaitForSeconds(1.2f);
                    var run = new MoveRun();
                    if (kind == "Jump")
                    {
                        yield return JumpAttack(rig, foe, fwd, probe, run);
                    }
                    else
                    {
                        yield return RollAttack(rig, foe, fwd, probe, run);
                    }
                    tries.Add($"started {run.Started}, move '{run.Kind}', hit {run.Hit}");
                    if (run.Started && run.Kind == kind && run.Hit)
                    {
                        good = run;
                    }
                }
                var what = kind == "Jump" ? "jump attack" : "roll attack";
                if (!c.Check(good != null, $"no {what} of Weapon Moveset landed on the {FoeName} in 3 tries ({string.Join("; ", tries.ToArray())})"))
                {
                    continue;
                }
                c.Check(good.Calls == 1 && Near(good.Amount, pay) && Near(good.Bar, GainAt(p, pay, 0f), 0.001f),
                    $"{what} on a {FoeName}: {good.Calls} adrenaline call(s), last amount {F(good.Amount)}, bar 0 -> {F(good.Bar)} "
                    + $"(a normal swing pays {F(pay)} in one call, adding {F(GainAt(p, pay, 0f))})");
                c.Check(CombatState.LastExchange >= good.StartAt && CombatState.InCombat(new TrinketRules().CombatLingerSeconds),
                    $"{what} that hit a {FoeName} did not count as a fight hit");
            }

            // With the income on: a jump attack that hits starts the fight's income.
            ServerRules.TestRules = new TrinketRules();
            var quiet = new bool[1];
            yield return QuietMoment(quiet);
            yield return new WaitForSeconds(1.2f);
            var fightMark = LogMark();
            MoveRun paid = null;
            for (var i = 0; i < 3 && paid == null; i++)
            {
                var run = new MoveRun();
                yield return JumpAttack(rig, foe, fwd, probe, run);
                if (run.Started && run.Kind == "Jump" && run.Hit)
                {
                    paid = run;
                }
                else
                {
                    yield return new WaitForSeconds(1.2f);
                }
            }
            if (c.Check(quiet[0] && paid != null, $"no jump attack landed for the income check, or a real creature hunts the player (quiet {quiet[0]})"))
            {
                var bar = p.m_adrenaline;
                yield return new WaitForSeconds(2.4f);
                var lines = LogCount(fightMark, FightOnLine);
                c.Check(lines == 1 && p.m_adrenaline > bar,
                    $"after a jump attack that hit: '{FightOnLine}' logged {lines} time(s) (expected once), bar {F(bar)} -> {F(p.m_adrenaline)} in 2.4 s (expected income)");
            }
            CheckCleanLog(c, mark);
        }
        finally
        {
            rig.P.m_queuedAttackTimer = 0f;
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.smoke-screen (X06) ----------

    private static IEnumerator RunSmokeScreen()
    {
        if (!ModActive(SneakGuid))
        {
            SelfTest.Fail(SmokeName, "Sneak Ambush is not loaded and active: nothing of TESTING.md X06 was checked");
            yield break;
        }
        var rig = Rig.Create(SmokeName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(SmokeName);
        var mark = LogMark();
        var stagger = rig.P.m_staggerDamageFactor;
        try
        {
            var p = rig.P;
            // Greydwarf's hits must not stagger the player out of the throw (0 = game never staggers this character).
            p.m_staggerDamageFactor = 0f;
            var rules = new TrinketRules();
            ServerRules.TestRules = rules;
            var linger = rules.CombatLingerSeconds;
            var window = CombatState.TargetedWindowSeconds;
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            var fwd = Flat(p.transform.forward);
            var foe = rig.Tough(FoeName, fwd * 3f);
            var ai = foe != null ? foe.GetComponent<MonsterAI>() : null;
            var smoke = PrefabShared(SmokeItemName) != null ? rig.Give(SmokeItemName, 2) : null;
            if (!c.Check(item != null && ai != null && smoke != null && MusicMan.instance != null,
                    $"could not equip a trinket, spawn {FoeName} or give {SmokeItemName} (Sneak Ambush's Smoke Screen)"))
            {
                c.Report();
                yield break;
            }
            rig.Effect(item.m_shared.m_fullAdrenalineSE.NameHash());
            rig.EmptyHands();
            rig.TakeControls();
            rig.Moves();
            rig.WatchDrops();
            c.Check(p.EquipItem(smoke, false), "could not take the Smoke Screen in hand");
            var cloudsBefore = new HashSet<GameObject>(NewObjects(SmokeCloudName, null));
            // Sneak Ambush lets a creature that is hit see the player through smoke while any cloud is around.
            c.Check(cloudsBefore.Count == 0, $"{cloudsBefore.Count} smoke cloud(s) of an earlier test are still in the world: the hits before the throw would let the {FoeName} see through the smoke");

            // Quiet start, then the Greydwarf hunts (AI on) and the player hits it: a fight.
            CombatState.Reset();
            yield return new WaitForSeconds(1.2f);
            var fightMark = LogMark();
            p.m_adrenaline = 0f;
            ai.enabled = true;
            ai.SetAlerted(true);
            ai.m_targetCreature = p;
            HitFromPlayer(p, foe);
            var hunted = false;
            for (var t = 0f; t < 2.5f && !hunted; t += Time.deltaTime)
            {
                hunted = ReferenceEquals(ai.m_targetCreature, p) && ai.IsAlerted() && !float.IsNegativeInfinity(CombatState.LastTargeted);
                yield return null;
            }
            yield return new WaitForSeconds(1.2f);
            if (!c.Check(hunted && LogCount(fightMark, FightOnLine) == 1 && p.m_adrenaline > 0f,
                    $"no fight with a hunting {FoeName} before the smoke (hunted {hunted}, '{FightOnLine}' logged {LogCount(fightMark, FightOnLine)} time(s), bar {F(p.m_adrenaline)}): "
                    + "nothing to check (Creature Morale may have made it afraid of this character)"))
            {
                c.Report();
                yield break;
            }

            // One more hit, then the real throw, straight down at the feet (a second try when the first throw was cut).
            p.m_lookPitch = 80f;
            p.SetMouseLook(Vector2.zero);
            HitFromPlayer(p, foe);
            var shot = new ShotResult();
            for (var attempt = 0; attempt < 2 && !shot.Fired; attempt++)
            {
                yield return ThrowShot(rig, smoke, false, shot, null);
            }
            c.Check(shot.Fired, "the Smoke Screen was not thrown");
            GameObject cloud = null;
            for (var t = 0f; t < 6f && cloud == null; t += Time.deltaTime)
            {
                var made = NewObjects(SmokeCloudName, cloudsBefore);
                cloud = made.Count > 0 ? made[0] : null;
                yield return null;
            }
            foreach (var made in NewObjects(SmokeCloudName, cloudsBefore))
            {
                rig.Track(made);
            }
            if (!c.Check(cloud != null, "no smoke cloud within 6 s of the throw"))
            {
                c.Report();
                yield break;
            }
            var cloudAt = Time.time;
            yield return Frames(3); // cloud's own data (radius) is written when it is set up
            if (!c.Check(cloud != null, "the smoke cloud vanished right after it appeared"))
            {
                c.Report();
                yield break;
            }
            var cloudView = cloud.GetComponent<ZNetView>();
            var radius = cloudView != null && cloudView.IsValid() ? cloudView.GetZDO().GetFloat(SmokeRadiusKey.GetStableHashCode(), 0f) : 0f;
            var away = Utils.DistanceXZ(cloud.transform.position, p.transform.position);
            c.Check(radius > 0f && away < radius - 0.5f, $"the player does not stand inside the cloud ({F(away)} m from its middle, radius {F(radius)} m)");

            // It loses the player: no target, not alerted.
            var lostAt = -1f;
            for (var t = 0f; t < 10f && lostAt < 0f; t += Time.deltaTime)
            {
                if (ai.m_targetCreature == null && !ai.IsAlerted())
                {
                    lostAt = Time.time;
                }
                yield return null;
            }
            c.Check(lostAt >= 0f, $"the {FoeName} did not lose the player within 10 s of the smoke");
            if (lostAt >= 0f)
            {
                c.Note($"the {FoeName} lost the player {F(lostAt - cloudAt)} s after the cloud appeared");
            }

            // Nobody hits: fight over by the rule, about 6 s after the last hit; the bar keeps its value after.
            var overAt = -1f;
            var back = false;
            for (var t = 0f; t < linger + 8f && overAt < 0f; t += Time.deltaTime)
            {
                if (LogCount(fightMark, FightOffLine) > 0)
                {
                    overAt = Time.time;
                }
                back |= lostAt >= 0f && ReferenceEquals(ai.m_targetCreature, p);
                yield return null;
            }
            var lastHit = CombatState.LastExchange;
            var lastHunt = CombatState.LastTargeted;
            var expected = Mathf.Max(lastHit + linger, lastHunt + window);
            c.Check(!back, $"the {FoeName} found the player again inside the smoke before the fight was over");
            c.Check(lostAt < 0f || lastHunt <= lostAt + 0.6f, $"the hunt signal still came {F(lastHunt - lostAt)} s after the {FoeName} lost the player");
            c.Check(overAt >= 0f && overAt >= expected - 0.1f && overAt <= expected + 1.3f,
                $"'{FightOffLine}' came {(overAt >= 0f ? F(overAt - lastHit) + " s" : "never")} after the last hit (expected {F(expected - lastHit)} s: "
                + $"{F(linger)} s after the last hit, or {F(window)} s after the hunt ended if that is later)");
            c.Check(overAt >= 0f && overAt - lastHit <= linger + 3f,
                $"the fight ended {F(overAt - lastHit)} s after the last hit (TESTING.md X06: about {F(linger)} s)");
            if (overAt >= 0f)
            {
                c.Note($"fight over {F(overAt - lastHit)} s after the last hit, {F(overAt - lastHunt)} s after the last hunt signal");
                var bar = p.m_adrenaline;
                yield return new WaitForSeconds(1.5f);
                if (CombatState.LastExchange == lastHit)
                {
                    c.Check(Near(p.m_adrenaline, bar) && bar > 0f, $"after the fight the bar went from {F(bar)} to {F(p.m_adrenaline)} (it should keep its value)");
                }
                else
                {
                    c.Note($"the {FoeName} found the player again right after the fight ended: the bar's value after the fight was not compared");
                }
            }
            CheckCleanLog(c, mark);
        }
        finally
        {
            rig.P.m_staggerDamageFactor = stagger;
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.morale-flee (X07) ----------

    private static IEnumerator RunMoraleFlee()
    {
        if (!ModActive(MoraleGuid))
        {
            SelfTest.Fail(MoraleFleeName, "Creature Morale is not loaded and active: nothing of TESTING.md X07 was checked");
            yield break;
        }
        var rig = Rig.Create(MoraleFleeName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(MoraleFleeName);
        var mark = LogMark();
        try
        {
            var p = rig.P;
            var rules = new TrinketRules();
            ServerRules.TestRules = rules;
            var linger = rules.CombatLingerSeconds;
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            var fwd = Flat(p.transform.forward);
            var side = Vector3.Cross(Vector3.up, fwd);
            rig.TakeControls();
            rig.Moves();
            rig.WatchDrops();
            var leader = rig.Creature(LeaderName, fwd * 8f);
            var greyling = rig.Tough(GreylingName, fwd * 6f + side * 3f);
            var ai = greyling != null ? greyling.GetComponent<MonsterAI>() : null;
            if (!c.Check(item != null && leader != null && ai != null && MusicMan.instance != null,
                    $"could not equip a trinket or spawn {LeaderName} and {GreylingName}"))
            {
                c.Report();
                yield break;
            }
            rig.Effect(item.m_shared.m_fullAdrenalineSE.NameHash());
            // Leader leaves nothing behind: no loot, no ragdoll (ragdoll drops loot later). This one creature only.
            var drop = leader.GetComponent<CharacterDrop>();
            if (drop != null)
            {
                drop.SetDropsEnabled(false);
            }
            leader.m_deathEffects = new EffectList();

            // Quiet start; Greyling awake (AI on) long enough for Creature Morale to know it.
            CombatState.Reset();
            yield return new WaitForSeconds(1.2f);
            ai.enabled = true;
            yield return new WaitForSeconds(1f);
            p.m_adrenaline = 0f;

            // Its Greydwarf Shaman dies by the player's hand: Creature Morale routs the Greyling.
            var deathPos = leader.transform.position;
            var start = Utils.DistanceXZ(greyling.transform.position, deathPos);
            KillByPlayer(p, leader);
            var fleeAt = -1f;
            for (var t = 0f; t < 5f && fleeAt < 0f; t += Time.deltaTime)
            {
                if (ai.IsAlerted() && ai.m_targetCreature == null && Utils.DistanceXZ(greyling.transform.position, deathPos) > start + 1.5f)
                {
                    fleeAt = Time.time;
                }
                yield return null;
            }
            if (!c.Check(fleeAt >= 0f,
                    $"the {GreylingName} did not flee within 5 s of the death of its {LeaderName} (alerted {ai.IsAlerted()}, has a target {ai.m_targetCreature != null}, "
                    + $"{F(Utils.DistanceXZ(greyling.transform.position, deathPos) - start)} m further away): nothing to check (Creature Morale's Packs setting?)"))
            {
                c.Report();
                yield break;
            }

            // A hit on the fleeing Greyling: fight. Then nobody hits and it keeps fleeing: over about 6 s later.
            var endMark = LogMark();
            HitFromPlayer(p, greyling);
            var hitAt = CombatState.LastExchange;
            c.Check(hitAt >= fleeAt && CombatState.InCombat(linger), $"the hit on the fleeing {GreylingName} did not count as a fight hit");
            var overAt = -1f;
            var fled = true;
            var targetTime = 0f;
            for (var t = 0f; t < linger + 4f && overAt < 0f; t += Time.deltaTime)
            {
                if (LogCount(endMark, FightOffLine) > 0)
                {
                    overAt = Time.time;
                }
                else
                {
                    // First run said it took a target: me note which one and when, so the fail tell what happened.
                    if (targetTime == 0f && greyling != null && ai.m_targetCreature != null)
                    {
                        c.Note($"the {GreylingName} took the target {ai.m_targetCreature.name} (the player: {ReferenceEquals(ai.m_targetCreature, p)}) "
                               + $"{F(Time.time - hitAt)} s after the hit, alerted {ai.IsAlerted()}, {F(Utils.DistanceXZ(greyling.transform.position, p.transform.position))} m from the player");
                    }
                    // Game make the attacker the target in the tick of the hit (first run: target at 0 s); Creature
                    // Morale take it away again. Me count how long it has a target: more than half a second = it turned.
                    if (greyling == null)
                    {
                        fled = false;
                    }
                    else if (ai.m_targetCreature != null)
                    {
                        targetTime += Time.deltaTime;
                    }
                }
                yield return null;
            }
            c.Check(CombatState.LastExchange == hitAt, "someone hit or was hit during the wait: the fight's end could not be timed");
            c.Check(fled && targetTime <= 0.5f, $"the {GreylingName} had a target for {F(targetTime)} s of the wait (the tick of the hit aside, a routed one has none): it did not flee all the time (Creature Morale's RoutSeconds shorter than its default 15 s?)");
            c.Check(CombatState.LastTargeted < hitAt, $"a hunt signal came {F(CombatState.LastTargeted - hitAt)} s after the hit, while the {GreylingName} fled");
            c.Check(overAt >= 0f && overAt - hitAt >= linger - 0.05f && overAt - hitAt <= linger + 1.3f,
                $"'{FightOffLine}' came {(overAt >= 0f ? F(overAt - hitAt) + " s" : "never")} after the hit on the fleeing {GreylingName} (expected about {F(linger)} s)");
            c.Check(p.m_adrenaline > 0f, "the fight paid no income");

            // Hitting it again starts a new fight.
            var againMark = LogMark();
            var bar = p.m_adrenaline;
            if (greyling != null)
            {
                HitFromPlayer(p, greyling);
            }
            yield return new WaitForSeconds(1.3f);
            c.Check(LogCount(againMark, FightOnLine) == 1 && p.m_adrenaline > bar,
                $"hit after the fight ended: '{FightOnLine}' {LogCount(againMark, FightOnLine)} time(s), bar {F(bar)} -> {F(p.m_adrenaline)}");
            CheckCleanLog(c, mark);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.morale-afraid (X07, fear) ----------

    // Creature Morale's standing of the local player forced to a boss rank, in ITS memory through its own self-test
    // hook (Standing.TestOverride of a Debug build), by reflection: no type of theirs here, no setting written.
    // Restore puts back what was there (null = the character's real standing).
    private sealed class MoraleRank
    {
        private PropertyInfo _override;
        private object _before;
        private bool _forced;

        internal string Problem = "";

        internal bool Force(int bossRank)
        {
            try
            {
                var standing = AccessTools.TypeByName("MC.Combat.CreaturesMoraleMod.Standing");
                var type = standing != null ? AccessTools.Inner(standing, "Override") : null;
                _override = standing != null ? AccessTools.Property(standing, "TestOverride") : null;
                if (type == null || _override == null)
                {
                    Problem = "its standing override (Standing.TestOverride of a Debug build) was not found";
                    return false;
                }
                var value = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                    new object[] { bossRank }, null);
                _before = _override.GetValue(null, null);
                _override.SetValue(null, value, null);
                _forced = true;
                return true;
            }
            catch (Exception e)
            {
                var inner = e.InnerException ?? e;
                Problem = inner.GetType().Name + ": " + inner.Message;
                return false;
            }
        }

        internal void Restore()
        {
            if (_forced)
            {
                _forced = false;
                _override.SetValue(null, _before, null);
            }
        }
    }

    // Creature Morale's own test setup: the player has the standing of the last boss, a Greyling of the Meadows is
    // afraid and runs. A fight (hit on a wild Boar that stands by, AI off) is not kept alive by the afraid Greyling:
    // over about 6 s after the hit. A hit on the Greyling starts a new one.
    private static IEnumerator RunMoraleAfraid()
    {
        if (!ModActive(MoraleGuid))
        {
            SelfTest.Fail(MoraleAfraidName, "Creature Morale is not loaded and active: nothing of TESTING.md X07 was checked");
            yield break;
        }
        var rig = Rig.Create(MoraleAfraidName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(MoraleAfraidName);
        var mark = LogMark();
        var rank = new MoraleRank();
        try
        {
            var p = rig.P;
            var rules = new TrinketRules();
            ServerRules.TestRules = rules;
            var linger = rules.CombatLingerSeconds;
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            var fwd = Flat(p.transform.forward);
            rig.TakeControls();
            rig.Moves();
            rig.WatchDrops();
            var boar = rig.Tough(BoarName, -fwd * 4f);
            if (!c.Check(item != null && boar != null && MusicMan.instance != null, $"could not equip a trinket or spawn {BoarName}"))
            {
                c.Report();
                yield break;
            }
            rig.Effect(item.m_shared.m_fullAdrenalineSE.NameHash());
            if (!c.Check(rank.Force(8), $"could not give the player the standing of the last boss in Creature Morale for the test: {rank.Problem}"))
            {
                c.Report();
                yield break;
            }
            yield return Frames(3);
            var greyling = rig.Tough(GreylingName, fwd * 7f);
            var ai = greyling != null ? greyling.GetComponent<MonsterAI>() : null;
            if (!c.Check(ai != null, $"could not spawn {GreylingName}"))
            {
                c.Report();
                yield break;
            }
            ai.enabled = true;

            // Afraid: it runs from the player (alerted, no target, further away).
            var start = Utils.DistanceXZ(greyling.transform.position, p.transform.position);
            var fleeAt = -1f;
            for (var t = 0f; t < 8f && fleeAt < 0f; t += Time.deltaTime)
            {
                if (ai.IsAlerted() && ai.m_targetCreature == null && Utils.DistanceXZ(greyling.transform.position, p.transform.position) > start + 1.5f)
                {
                    fleeAt = Time.time;
                }
                yield return null;
            }
            if (!c.Check(fleeAt >= 0f,
                    $"the {GreylingName} did not run from a player with the last boss's standing within 8 s (alerted {ai.IsAlerted()}, has a target {ai.m_targetCreature != null}, "
                    + $"{F(Utils.DistanceXZ(greyling.transform.position, p.transform.position) - start)} m further away): nothing to check (Creature Morale's fear settings?)"))
            {
                c.Report();
                yield break;
            }

            // A fight starts next to it (hit on the Boar). The afraid Greyling never hunts: over about 6 s later.
            var quiet = new bool[1];
            yield return QuietMoment(quiet);
            yield return new WaitForSeconds(1.2f);
            var fightMark = LogMark();
            p.m_adrenaline = 0f;
            HitFromPlayer(p, boar);
            var hitAt = CombatState.LastExchange;
            var overAt = -1f;
            var hunted = false;
            for (var t = 0f; t < linger + 4f && overAt < 0f; t += Time.deltaTime)
            {
                if (LogCount(fightMark, FightOffLine) > 0)
                {
                    overAt = Time.time;
                }
                hunted |= greyling != null && ReferenceEquals(ai.m_targetCreature, p);
                yield return null;
            }
            c.Check(quiet[0] && CombatState.LastExchange == hitAt, "a creature hunts or fights the player besides the test's own: the fight's end could not be timed");
            c.Check(LogCount(fightMark, FightOnLine) == 1 && p.m_adrenaline > 0f,
                $"hit on a wild {BoarName}: '{FightOnLine}' logged {LogCount(fightMark, FightOnLine)} time(s), bar {F(p.m_adrenaline)} (expected a fight with income)");
            c.Check(!hunted && CombatState.LastTargeted < hitAt, $"the afraid {GreylingName} hunted the player during the fight (target {hunted}, hunt signal {F(CombatState.LastTargeted - hitAt)} s after the hit)");
            c.Check(overAt >= 0f && overAt - hitAt >= linger - 0.05f && overAt - hitAt <= linger + 1.3f,
                $"with an afraid {GreylingName} around, '{FightOffLine}' came {(overAt >= 0f ? F(overAt - hitAt) + " s" : "never")} after the last hit (expected about {F(linger)} s)");

            // Hitting the afraid Greyling starts a new fight.
            var againMark = LogMark();
            var bar = p.m_adrenaline;
            if (greyling != null)
            {
                HitFromPlayer(p, greyling);
            }
            yield return new WaitForSeconds(1.3f);
            c.Check(LogCount(againMark, FightOnLine) == 1 && p.m_adrenaline > bar,
                $"hit on the afraid {GreylingName} after the fight ended: '{FightOnLine}' {LogCount(againMark, FightOnLine)} time(s), bar {F(bar)} -> {F(p.m_adrenaline)}");
            CheckCleanLog(c, mark);
        }
        finally
        {
            rank.Restore();
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.pvp-dodge-arrow (M10) ----------

    // Stand-in for another player's character, as in trinkets.pvp: player prefab made without network start, own ZDO
    // far outside the loaded area given to a game that is not there, known to the scene. Lives inside one frame only
    // (no physics tick may see it): Make, use, Remove.
    private sealed class OtherPlayer
    {
        internal Player Body;
        private GameObject _go;
        private ZNetView _view;
        private ZDO _zdo;

        internal static OtherPlayer Make(Player local)
        {
            var other = new OtherPlayer();
            ZNetView.m_forceDisableInit = true;
            try
            {
                other._go = Object.Instantiate(Game.instance.m_playerPrefab, local.transform.position + Flat(local.transform.forward) * 3f, Quaternion.identity);
            }
            finally
            {
                ZNetView.m_forceDisableInit = false;
            }
            other.Body = other._go.GetComponent<Player>();
            other._view = other.Body != null ? other.Body.m_nview : null;
            if (other._view == null)
            {
                other.Remove();
                return null;
            }
            other._zdo = ZDOMan.instance.CreateNewZDO(local.transform.position + new Vector3(3000f, 0f, 3000f), 0);
            other._zdo.SetOwner(ZDOMan.GetSessionID() + 1L);
            other._view.m_zdo = other._zdo;
            ZNetScene.instance.m_instances[other._zdo] = other._view;
            return other;
        }

        internal void SetPvp(bool on) => _zdo.Set(ZDOVars.s_pvp, on);

        internal void Remove()
        {
            if (_zdo != null && ZNetScene.instance != null)
            {
                ZNetScene.instance.m_instances.Remove(_zdo);
            }
            if (_view != null)
            {
                _view.m_zdo = null;
            }
            if (_go != null)
            {
                Object.DestroyImmediate(_go);
            }
            if (_zdo != null && ZDOMan.instance != null)
            {
                _zdo.SetOwner(ZDOMan.GetSessionID());
                ZDOMan.instance.DestroyZDO(_zdo);
            }
            _go = null;
            _view = null;
            _zdo = null;
            Body = null;
        }
    }

    // One frame: other player (PvP flag as given) shoots, its arrow meets the local player (the game's own
    // Projectile.OnHit: is the target valid for this shooter, is it rolling through). Arrow and stand-in gone after.
    private static string ArrowFromOtherPlayer(Player p, GameObject arrowPrefab, bool theirPvp)
    {
        OtherPlayer other = null;
        GameObject arrow = null;
        try
        {
            other = OtherPlayer.Make(p);
            if (other == null)
            {
                return "could not make the stand-in for another player";
            }
            other.SetPvp(theirPvp);
            if (other.Body.IsPVPEnabled() != theirPvp || ReferenceEquals(other.Body, Player.m_localPlayer) || other.Body.m_nview.IsOwner())
            {
                return "the stand-in is not another game's player with the PvP flag as set";
            }
            var from = p.GetCenterPoint() - Flat(p.transform.forward) * 2f;
            arrow = Object.Instantiate(arrowPrefab, from, Quaternion.LookRotation(Flat(p.transform.forward)));
            var projectile = arrow.GetComponent<Projectile>();
            if (projectile == null)
            {
                return "the arrow prefab has no projectile";
            }
            var hit = new HitData();
            hit.m_damage.m_pierce = 10f;
            hit.m_dodgeable = true;
            hit.m_blockable = true;
            hit.m_ranged = true;
            projectile.Setup(other.Body, Flat(p.transform.forward) * 40f, -1f, hit, null, null);
            projectile.OnHit(ColliderOf(p), p.GetCenterPoint(), false, Vector3.zero);
            return null;
        }
        finally
        {
            if (arrow != null)
            {
                DestroyObject(arrow);
            }
            if (other != null)
            {
                other.Remove();
            }
        }
    }

    private static IEnumerator RunDodgeArrow()
    {
        var rig = Rig.Create(DodgeArrowName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(DodgeArrowName);
        var mark = LogMark();
        var p = rig.P;
        var pvpBefore = p.m_pvp;
        try
        {
            var rules = new TrinketRules();
            ServerRules.TestRules = rules;
            var linger = rules.CombatLingerSeconds;
            var tick = rules.IncomePerSecond * Income.TickSeconds;
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            var arrowShared = PrefabShared(ArrowName);
            var arrowPrefab = arrowShared != null && arrowShared.m_attack != null ? arrowShared.m_attack.m_attackProjectile : null;
            if (!c.Check(item != null && arrowPrefab != null && Game.instance != null && Game.instance.m_playerPrefab != null
                         && ZDOMan.instance != null && ZNetScene.instance != null,
                    $"could not equip a trinket, or no {ArrowName} projectile or player prefab in this game"))
            {
                c.Report();
                yield break;
            }
            rig.Effect(item.m_shared.m_fullAdrenalineSE.NameHash());
            rig.EmptyHands();
            rig.TakeControls();
            rig.Moves();
            var fwd = Flat(p.transform.forward);
            var max = p.GetMaxAdrenaline();
            var pay = p.m_perfectDodgeAdrenaline;

            // Other player's PvP off: the game never lets their arrow count, also not as a dodged one. No fight.
            var quiet = new bool[1];
            yield return QuietMoment(quiet);
            if (!c.Check(quiet[0], "a real creature hunts or fights the player: the PvP case (no creature nearby) could not be set up"))
            {
                c.Report();
                yield break;
            }
            p.m_pvp = true;
            yield return Until(() => Calm(p), 4f);
            p.m_adrenaline = 0f;
            p.Dodge(fwd);
            yield return Until(() => p.IsDodgeInvincible(), 2f);
            var rolling = p.IsDodgeInvincible();
            var calls = FullBar.Calls;
            var problem = ArrowFromOtherPlayer(p, arrowPrefab, false);
            c.Check(problem == null && rolling, $"PvP off for the other player: {problem ?? "the roll did not start"}");
            c.Check(FullBar.Calls == calls && Near(p.m_adrenaline, 0f) && float.IsNegativeInfinity(CombatState.LastExchange),
                $"PvP off for the other player: their arrow through the rolling player counted ({FullBar.Calls - calls} adrenaline call(s), bar {F(p.m_adrenaline)}, "
                + $"fight {!float.IsNegativeInfinity(CombatState.LastExchange)})");
            yield return Until(() => !p.InDodge() && Calm(p), 4f);

            // PvP on for both: the arrow is dodged. The game's dodge pay in one call, a fight, its Debug line, income.
            yield return QuietMoment(quiet);
            yield return new WaitForSeconds(1.2f);
            var fightMark = LogMark();
            p.m_adrenaline = 0f;
            p.Dodge(fwd);
            yield return Until(() => p.IsDodgeInvincible(), 2f);
            rolling = p.IsDodgeInvincible();
            calls = FullBar.Calls;
            var want = Gain(p, pay);
            var t0 = Time.time;
            problem = ArrowFromOtherPlayer(p, arrowPrefab, true);
            if (!c.Check(quiet[0] && problem == null && rolling, $"PvP on for both: {problem ?? (rolling ? "a real creature hunts the player" : "the roll did not start")}"))
            {
                c.Report();
                yield break;
            }
            c.Check(FullBar.Calls == calls + 1 && Near(FullBar.LastAmount, pay) && Near(p.m_adrenaline, want, 0.001f),
                $"PvP on for both, the other player's arrow dodged: {FullBar.Calls - calls} adrenaline call(s) of {F(FullBar.LastAmount)}, bar 0 -> {F(p.m_adrenaline)} "
                + $"(the game's amount is {F(pay)}, adding {F(want)})");
            c.Check(CombatState.LastExchange >= t0 && CombatState.InCombat(linger), "PvP on for both: the dodged arrow of the other player did not start a fight");

            // Income each second until the linger is over, then the bar keeps its value.
            var gains = new List<float>();
            var wrong = new List<string>();
            var overAt = -1f;
            var prev = p.m_adrenaline;
            while (Time.time - t0 < linger + 3.5f)
            {
                var now = p.m_adrenaline;
                if (now > prev + 0.0001f)
                {
                    gains.Add(Time.time - t0);
                    if (!Near(now - prev, GainAt(p, tick, prev), 0.001f))
                    {
                        wrong.Add($"{F(now - prev)} instead of {F(GainAt(p, tick, prev))}");
                    }
                }
                prev = now;
                if (overAt < 0f && LogCount(fightMark, FightOffLine) > 0)
                {
                    overAt = Time.time - t0;
                }
                yield return null;
            }
            var on = LogCount(fightMark, FightOnLine);
            c.Check(CombatState.LastExchange >= t0 && CombatState.LastExchange <= t0 + 0.05f && float.IsNegativeInfinity(CombatState.LastTargeted),
                "a real creature joined during the wait: the fight's length could not be timed");
            c.Check(on == 1, $"Debug line '{FightOnLine}' logged {on} time(s) after the dodge, expected once");
            var wanted = Mathf.FloorToInt(linger);
            c.Check(gains.Count >= wanted - 1 && gains.Count <= wanted + 1 && wrong.Count == 0,
                $"{gains.Count} income tick(s) after the dodge (expected about {wanted}, {F(tick)} each); wrong ones: {string.Join("; ", wrong.Take(2).ToArray())}");
            var last = gains.Count > 0 ? gains[gains.Count - 1] : -1f;
            c.Check(last <= linger + 0.15f, $"the last income tick came {F(last)} s after the dodge (expected none after {F(linger)} s)");
            c.Check(overAt >= linger - 0.05f && overAt <= linger + 1.3f,
                $"'{FightOffLine}' came {(overAt >= 0f ? F(overAt) + " s" : "never")} after the dodge (expected about {F(linger)} s)");
            c.Check(max > want + (wanted + 1) * tick, $"the trinket's bar ({F(max)}) is too small for the dodge pay and {wanted} s of income");
            CheckCleanLog(c, mark);
        }
        finally
        {
            p.m_pvp = pvpBefore;
            rig.Restore();
        }
        c.Report();
    }
}
#endif
