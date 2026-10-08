#if DEBUG
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.TrinketsOnDemandMod;

// Debug build only. In-world self tests of fight (same class as SelfTests.cs):
//   trinkets.block-fight  T02: real blocks with ShieldWood (each pays shield's amount, income on top, Debug line);
//                         T03: fight ends about 6 s after last hit (Debug line, bar keeps its value), new hit
//                         starts new one; T08: block and parry on full bar; T07: unblocked hit and missed
//                         swing take nothing; X05: tower shield block right as hit lands
//   trinkets.no-fight     T05: sword swings on training dummy pay sword's amount and start no fight; tamed
//                         creature; real fall
//   trinkets.targeting    T04: one hit, then hunted (call hunting creature's AI makes): income goes on past
//                         linger, stops about 20 s after hit; stops soon after hunt ends
//   trinkets.dodge-fight  perfect dodge of hunting creature's attack: game's dodge amount in one call, fight starts
//   trinkets.bug.pvp-dodge  one check alone (real bug, TESTING.md M10): perfect dodge with no creature hunting
//                         (dodged attack of another player) must start no fight. Fails until mod or its README
//                         is changed
internal static partial class SelfTests
{
    private const string BlockFightName = "trinkets.block-fight";
    private const string NoFightName = "trinkets.no-fight";
    private const string TargetingName = "trinkets.targeting";
    private const string DodgeFightName = "trinkets.dodge-fight";
    private const string PvpDodgeBugName = "trinkets.bug.pvp-dodge";

    // ---------- trinkets.block-fight ----------

    // T02: creature's hits land on raised shield, one each second: each block pays shield's normal amount in
    // one call, fight pays income between them, fight's Debug line comes once. Also run by multiplayer
    // test with server's rules. Trinket worn, shield in left hand, foe in front.
    private static IEnumerator BlockFightCore(Checks c, Rig rig, TrinketRules rules, Character foe, ItemDrop.ItemData shield, int hits)
    {
        var p = rig.P;
        // Quiet start: Debug lines come once per fight, older fight must be over for income first.
        CombatState.Reset();
        yield return new WaitForSeconds(1.2f);
        var mark = LogMark();
        rig.Block(true);
        yield return Until(() => p.IsBlocking(), 2f);
        yield return new WaitForSeconds(0.4f); // past parry window: plain blocks
        if (!c.Check(p.IsBlocking() && ReferenceEquals(p.GetCurrentBlocker(), shield), "the shield is not raised"))
        {
            yield break;
        }
        p.m_adrenaline = 0f;
        var blockPay = shield.m_shared.m_blockAdrenaline;
        var tick = rules.IncomePerSecond * Income.TickSeconds;
        var wrongBlocks = new List<string>();
        var wrongTicks = new List<string>();
        var blocks = 0;
        var ticks = 0;
        var last = p.m_adrenaline;
        var t0 = Time.time;
        var nextHit = t0;
        while (Time.time - t0 < hits + 0.2f)
        {
            var now = p.m_adrenaline;
            if (now > last + 0.0001f)
            {
                ticks++;
                var wantTick = GainAt(p, tick, last);
                if (!Near(now - last, wantTick, 0.001f))
                {
                    wrongTicks.Add($"{F(now - last)} instead of {F(wantTick)}");
                }
            }
            else if (now < last - 0.0001f)
            {
                wrongTicks.Add($"the bar fell from {F(last)} to {F(now)}");
            }
            last = now;
            if (blocks < hits && Time.time >= nextHit && p.IsBlocking())
            {
                var want = Gain(p, blockPay);
                var calls = FullBar.Calls;
                HitPlayerFront(p, foe, true);
                var got = p.m_adrenaline - last;
                if (FullBar.Calls != calls + 1 || !Near(FullBar.LastAmount, blockPay) || !Near(got, want, 0.001f))
                {
                    wrongBlocks.Add($"block {blocks + 1} added {F(got)} in {FullBar.Calls - calls} call(s), expected {F(want)} in one");
                }
                blocks++;
                last = p.m_adrenaline;
                nextHit += 1f;
            }
            yield return null;
        }
        var elapsed = Time.time - t0;
        c.Check(blocks == hits && wrongBlocks.Count == 0,
            $"{blocks} of {hits} hits blocked; {wrongBlocks.Count} did not add the shield's normal amount ({F(blockPay)}): "
            + string.Join("; ", wrongBlocks.Take(2).ToArray()));
        c.Check(CombatState.LastExchange >= t0, "hits of a creature on the raised shield did not start a fight");
        var lines = LogCount(mark, FightOnLine);
        c.Check(lines == 1, $"Debug line '{FightOnLine}' logged {lines} time(s), expected once");
        c.Check(ticks >= hits - 1 && ticks <= hits + 1 && wrongTicks.Count == 0,
            $"{ticks} income tick(s) in {F(elapsed)} s of fight (expected about one per second, {F(tick)} each); wrong ones: "
            + string.Join("; ", wrongTicks.Take(2).ToArray()));
        c.Note($"{blocks} blocks of {F(blockPay)} and {ticks} income ticks of {F(tick)} in {F(elapsed)} s: bar {F(p.m_adrenaline)}");
    }

    private static IEnumerator RunBlockFight()
    {
        var rig = Rig.Create(BlockFightName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(BlockFightName);
        var mark = LogMark();
        try
        {
            var p = rig.P;
            var rules = new TrinketRules();
            ServerRules.TestRules = rules;
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            var fwd = Flat(p.transform.forward);
            var foe = rig.Tough(FoeName, fwd * 2f);
            var shield = rig.Give(ShieldName);
            var sword = rig.Give(SwordName);
            var tower = rig.Give(TowerShieldName);
            if (!c.Check(item != null && foe != null && shield != null && sword != null,
                    $"could not equip a trinket, give {ShieldName} and {SwordName} or spawn {FoeName}"))
            {
                c.Report();
                yield break;
            }
            var hash = item.m_shared.m_fullAdrenalineSE.NameHash();
            rig.Effect(hash);
            if (p.m_perfectBlockStatusEffect != null)
            {
                rig.Effect(p.m_perfectBlockStatusEffect.NameHash());
            }
            if (shield.m_shared.m_perfectBlockStatusEffect != null)
            {
                rig.Effect(shield.m_shared.m_perfectBlockStatusEffect.NameHash());
            }
            rig.EmptyHands();
            c.Check(p.EquipItem(sword, false) && p.EquipItem(shield, false) && ReferenceEquals(p.m_leftItem, shield),
                $"could not hold {SwordName} and {ShieldName}");
            rig.TakeControls();
            Face(p, fwd);
            yield return FixedTicks(3);
            var max = p.GetMaxAdrenaline();

            yield return BlockFightCore(c, rig, rules, foe, shield, 4);

            // T03: no more hits. About 6 s after last one fight over, bar stops and keeps its value.
            var lastHit = CombatState.LastExchange;
            var linger = rules.CombatLingerSeconds;
            rig.Block(false);
            var endMark = LogMark();
            var overAt = -1f;
            var barAtOver = 0f;
            var lastGainAt = lastHit;
            var prev = p.m_adrenaline;
            while (Time.time - lastHit < linger + 6f)
            {
                var now = p.m_adrenaline;
                if (now > prev + 0.0001f)
                {
                    lastGainAt = Time.time;
                }
                prev = now;
                if (overAt < 0f && LogCount(endMark, FightOffLine) > 0)
                {
                    overAt = Time.time;
                    barAtOver = now;
                }
                yield return null;
            }
            c.Check(CombatState.LastExchange == lastHit, "a real creature joined the fight during the wait: its end could not be timed");
            c.Check(overAt >= 0f && overAt - lastHit >= linger - 0.05f && overAt - lastHit <= linger + 1.3f,
                $"Debug line '{FightOffLine}' came {(overAt >= 0f ? F(overAt - lastHit) + " s" : "never")} after the last hit (expected about {F(linger)} s)");
            c.Check(lastGainAt - lastHit <= linger + 0.15f, $"the bar still rose {F(lastGainAt - lastHit)} s after the last hit");
            c.Check(overAt >= 0f && Time.time - overAt >= 4f && Near(p.m_adrenaline, barAtOver) && p.m_adrenaline > 0f,
                $"after the fight the bar went from {F(barAtOver)} to {F(p.m_adrenaline)} (it should keep its value)");

            // New hit starts new fight.
            var againMark = LogMark();
            var bar = p.m_adrenaline;
            HitFromPlayer(p, foe);
            yield return new WaitForSeconds(1.3f);
            c.Check(LogCount(againMark, FightOnLine) == 1 && p.m_adrenaline > bar,
                $"hit after the fight ended: '{FightOnLine}' {LogCount(againMark, FightOnLine)} time(s), bar {F(bar)} -> {F(p.m_adrenaline)}");

            // T08: on full bar block pays nothing and fires nothing.
            rig.Block(true);
            yield return Until(() => p.IsBlocking(), 2f);
            yield return new WaitForSeconds(0.4f);
            p.m_adrenaline = max;
            var calls = FullBar.Calls;
            HitPlayerFront(p, foe, true);
            c.Check(FullBar.Calls == calls + 1 && Near(FullBar.LastAmount, shield.m_shared.m_blockAdrenaline)
                    && Near(p.m_adrenaline, max) && !rig.Has(hash),
                $"full bar + block: bar {F(p.m_adrenaline)} of {F(max)}, effect {rig.Has(hash)}");

            // Parry (hit right as shield goes up): game's parry amount, added on bar of 20, held on full bar.
            // Fight still on here (income tick each second, 50 Hz): bar set to 20 in same frame as hit, never
            // before wait. Set before wait, tick landed in those two physics ticks once: bar 21, then 26, not 25.
            rig.Block(false);
            yield return FixedTicks(3);
            rig.Block(true);
            yield return FixedTicks(2);
            var parry = p.IsBlocking() && shield.m_shared.m_timedBlockBonus > 1f && p.m_blockTimer >= 0f && p.m_blockTimer < 0.25f;
            var parryPay = shield.m_shared.m_perfectBlockAdrenaline;
            p.m_adrenaline = 20f;
            var wantParry = Gain(p, parryPay);
            calls = FullBar.Calls;
            HitPlayerFront(p, foe, true);
            c.Check(parry && FullBar.Calls == calls + 1 && Near(FullBar.LastAmount, parryPay) && Near(p.m_adrenaline, 20f + wantParry),
                $"parry on a bar of 20: timed {parry}, {FullBar.Calls - calls} call(s) of {F(FullBar.LastAmount)}, bar {F(p.m_adrenaline)} "
                + $"(expected {F(20f + wantParry)})");
            rig.Block(false);
            yield return FixedTicks(3);
            p.m_adrenaline = max;
            rig.Block(true);
            yield return FixedTicks(2);
            calls = FullBar.Calls;
            HitPlayerFront(p, foe, true);
            c.Check(FullBar.Calls > calls && Near(p.m_adrenaline, max) && !rig.Has(hash),
                $"full bar + parry: bar {F(p.m_adrenaline)} of {F(max)}, effect {rig.Has(hash)}");

            // T07: unblocked hit and swing that misses take nothing (game's losses, 0 in 1.0.16).
            rig.Block(false);
            yield return FixedTicks(3);
            c.Note($"the normal game's loss for an unblocked hit is {F(p.m_nonBlockDamageAdrenaline)}, for a melee miss {F(p.m_attackMissAdrenaline)}");
            p.m_adrenaline = 20f;
            calls = FullBar.Calls;
            HitPlayerFront(p, foe, true);
            c.Check(!p.IsBlocking() && FullBar.Calls == calls + 1 && Near(FullBar.LastAmount, p.m_nonBlockDamageAdrenaline)
                    && Near(p.m_adrenaline, 20f + p.m_nonBlockDamageAdrenaline),
                $"unblocked hit on a bar of 20: {FullBar.Calls - calls} call(s) of {F(FullBar.LastAmount)}, bar {F(p.m_adrenaline)}");
            Face(p, -fwd); // away from creature: swing hits nothing
            yield return FixedTicks(2);
            var before = p.m_adrenaline;
            var zero = FullBar.ZeroCalls;
            calls = FullBar.Calls;
            var started = new bool[1];
            yield return Swing(p, false, started);
            var missSeen = p.m_attackMissAdrenaline == 0f ? FullBar.ZeroCalls > zero : FullBar.Calls > calls;
            c.Check(started[0] && missSeen, "the sword swing at nothing did not reach the game's miss call");
            c.Check(p.m_adrenaline >= before + p.m_attackMissAdrenaline - 0.0001f,
                $"swing that misses: the bar went from {F(before)} to {F(p.m_adrenaline)} (only the fight's income may add)");

            // X05: tower shield, block right as hit lands: normal block amount (tower shield never parries);
            // Full bar: nothing fires.
            Face(p, fwd);
            yield return Until(() => Calm(p), 3f);
            p.UnequipItem(shield, false);
            if (c.Check(tower != null && p.EquipItem(tower, false) && ReferenceEquals(p.m_leftItem, tower), $"could not hold {TowerShieldName}"))
            {
                if (tower.m_shared.m_perfectBlockStatusEffect != null)
                {
                    rig.Effect(tower.m_shared.m_perfectBlockStatusEffect.NameHash());
                }
                c.Check(ModActive(TowerWallGuid), "Tower Shield Wall is not loaded and active: only the plain game's tower shield was checked");
                yield return FixedTicks(3);
                rig.Block(true);
                yield return Until(() => p.IsBlocking() && ReferenceEquals(p.GetCurrentBlocker(), tower), 3f);
                yield return FixedTicks(2);
                var timed = p.IsBlocking() && p.m_blockTimer >= 0f && p.m_blockTimer < 0.25f;
                var towerPay = tower.m_shared.m_blockAdrenaline;
                p.m_adrenaline = 20f; // same frame as hit: fight's income tick cannot land in between (see parry)
                var wantTower = Gain(p, towerPay);
                calls = FullBar.Calls;
                HitPlayerFront(p, foe, true);
                c.Check(timed && FullBar.Calls == calls + 1 && Near(FullBar.LastAmount, towerPay) && Near(p.m_adrenaline, 20f + wantTower),
                    $"tower shield, hit right as it goes up (bar 20): in time {timed}, {FullBar.Calls - calls} call(s) of {F(FullBar.LastAmount)}, "
                    + $"bar {F(p.m_adrenaline)} (expected {F(20f + wantTower)}: the normal block amount {F(towerPay)}; parry bonus of the shield x{F(tower.m_shared.m_timedBlockBonus)})");
                p.m_adrenaline = max;
                calls = FullBar.Calls;
                HitPlayerFront(p, foe, true);
                c.Check(FullBar.Calls > calls && Near(p.m_adrenaline, max) && !rig.Has(hash),
                    $"tower shield, full bar + block: bar {F(p.m_adrenaline)} of {F(max)}, effect {rig.Has(hash)}");
                rig.Block(false);
            }
            CheckCleanLog(c, mark);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.no-fight (T05) ----------

    private static IEnumerator RunNoFight()
    {
        var rig = Rig.Create(NoFightName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(NoFightName);
        var mark = LogMark();
        try
        {
            var p = rig.P;
            ServerRules.TestRules = new TrinketRules();
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            var fwd = Flat(p.transform.forward);
            var dummy = rig.Creature(DummyName, fwd * 1.6f);
            var boar = rig.Tough(BoarName, -fwd * 4f);
            var sword = rig.Give(SwordName);
            if (!c.Check(item != null && dummy != null && boar != null && sword != null,
                    $"could not equip a trinket, give {SwordName} or spawn {DummyName} and {BoarName}"))
            {
                c.Report();
                yield break;
            }
            var hash = item.m_shared.m_fullAdrenalineSE.NameHash();
            rig.Effect(hash);
            rig.EmptyHands();
            c.Check(p.EquipItem(sword, false), $"could not equip {SwordName}");
            rig.TakeControls();
            rig.Moves();
            var max = p.GetMaxAdrenaline();
            CombatState.Reset();
            yield return new WaitForSeconds(1.2f);
            var fightMark = LogMark();

            // (a) Training dummy: each sword hit pays sword's normal amount, no fight, no income.
            // Game marks creature its local player hit (Character.RPC_Damage): that is how me know swing landed.
            p.m_adrenaline = 0f;
            var pay = sword.m_shared.m_attack.m_attackAdrenaline * dummy.m_enemyAdrenalineMultiplier;
            c.Note($"{SwordName} pays {F(sword.m_shared.m_attack.m_attackAdrenaline)} per enemy hit, the dummy's multiplier is {F(dummy.m_enemyAdrenalineMultiplier)}");
            var started = new bool[1];
            var hits = 0;
            for (var i = 0; i < 3; i++)
            {
                Face(p, dummy.transform.position - p.transform.position);
                yield return FixedTicks(2);
                var before = p.m_adrenaline;
                var want = Gain(p, pay);
                var calls = FullBar.Calls;
                dummy.m_localPlayerHasHit = false;
                yield return Swing(p, false, started);
                if (started[0] && dummy.m_localPlayerHasHit)
                {
                    hits++;
                    c.Check(FullBar.Calls == calls + 1 && Near(FullBar.LastAmount, pay) && Near(p.m_adrenaline - before, want, 0.001f),
                        $"dummy hit {i + 1} added {F(p.m_adrenaline - before)} in {FullBar.Calls - calls} call(s) (last amount {F(FullBar.LastAmount)}), "
                        + $"the sword's normal amount is {F(want)}");
                }
            }
            c.Check(hits >= 2, $"only {hits} of 3 sword swings hit the training dummy");
            var after = p.m_adrenaline;
            yield return new WaitForSeconds(2.3f);
            c.Check(Near(p.m_adrenaline, after) && float.IsNegativeInfinity(CombatState.LastExchange) && LogCount(fightMark, FightOnLine) == 0,
                $"after hitting only the dummy: bar {F(after)} -> {F(p.m_adrenaline)} (no income), '{FightOnLine}' logged {LogCount(fightMark, FightOnLine)} time(s)");

            // Real hit on full bar held too (T08).
            p.m_adrenaline = max;
            Face(p, dummy.transform.position - p.transform.position);
            yield return FixedTicks(2);
            var fullCalls = FullBar.Calls;
            dummy.m_localPlayerHasHit = false;
            yield return Swing(p, false, started);
            c.Check(started[0] && dummy.m_localPlayerHasHit && FullBar.Calls > fullCalls, "the sword swing on a full bar did not hit the dummy");
            c.Check(Near(p.m_adrenaline, max) && !rig.Has(hash), $"full bar + a sword hit: bar {F(p.m_adrenaline)} of {F(max)}, effect {rig.Has(hash)}");

            // (b) Tamed creature.
            boar.SetTamed(true);
            CombatState.Reset();
            p.m_adrenaline = 5f;
            HitFromPlayer(p, boar);
            yield return new WaitForSeconds(2.3f);
            c.Check(boar.IsTamed() && float.IsNegativeInfinity(CombatState.LastExchange) && Near(p.m_adrenaline, 5f)
                    && LogCount(fightMark, FightOnLine) == 0,
                $"hit on a tamed {BoarName}: bar 5 -> {F(p.m_adrenaline)} (no income), fight started {!float.IsNegativeInfinity(CombatState.LastExchange)}");

            // (c) fall (9 m: game's fall damage, hit with no attacker).
            p.SetHealth(p.GetMaxHealth());
            var health = p.GetHealth();
            var home = p.transform.position;
            CombatState.Reset();
            p.m_adrenaline = 5f;
            Put(p, home + Vector3.up * 9f);
            yield return new WaitForSeconds(0.5f);
            yield return Until(() => p.IsOnGround(), 6f);
            yield return FixedTicks(5);
            if (p.GetHealth() >= health - 0.5f)
            {
                c.Note("the 9 m drop did no fall damage here: a hit without attacker, as the game's fall damage is, was sent instead");
                var hit = new HitData();
                hit.m_damage.m_damage = 5f;
                hit.m_point = p.transform.position;
                hit.m_dir = Vector3.up;
                hit.m_hitType = HitData.HitType.Fall;
                p.Damage(hit);
            }
            yield return new WaitForSeconds(2.3f);
            c.Check(p.GetHealth() < health - 0.5f && float.IsNegativeInfinity(CombatState.LastExchange) && Near(p.m_adrenaline, 5f)
                    && LogCount(fightMark, FightOnLine) == 0,
                $"fall: health {F(health)} -> {F(p.GetHealth())}, bar 5 -> {F(p.m_adrenaline)} (no income), "
                + $"fight started {!float.IsNegativeInfinity(CombatState.LastExchange)}");
            CheckCleanLog(c, mark);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.targeting (T04) ----------

    private static IEnumerator RunTargeting()
    {
        var rig = Rig.Create(TargetingName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(TargetingName);
        var mark = LogMark();
        try
        {
            var p = rig.P;
            var rules = new TrinketRules();
            ServerRules.TestRules = rules;
            var linger = rules.CombatLingerSeconds;
            var cap = CombatState.EngagedWindowSeconds;
            var tick = rules.IncomePerSecond * Income.TickSeconds;
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            var foe = rig.Tough(FoeName, Flat(p.transform.forward) * 8f);
            if (!c.Check(item != null && foe != null && MusicMan.instance != null, $"could not equip a trinket or spawn {FoeName}"))
            {
                c.Report();
                yield break;
            }
            rig.Effect(item.m_shared.m_fullAdrenalineSE.NameHash());
            var max = p.GetMaxAdrenaline();
            if (!c.Check(max > (cap + 2f) * tick, $"the trinket's bar ({F(max)}) is too small for {F(cap)} s of income"))
            {
                c.Report();
                yield break;
            }

            // One hit from afar, then creature hunts player and nobody lands hit. Hunting creature's AI
            // calls OnTargeted(sensed, alerted) on its target every AI update; game passes it on about twice each second.
            CombatState.Reset();
            yield return new WaitForSeconds(1.2f);
            var huntMark = LogMark();
            p.m_adrenaline = 0f;
            HitFromPlayer(p, foe);
            var hitAt = CombatState.LastExchange;
            var gains = new List<float>();
            var overAt = -1f;
            var prev = p.m_adrenaline;
            while (Time.time - hitAt < cap + 5f)
            {
                p.OnTargeted(true, true);
                if (p.m_adrenaline > prev + 0.0001f)
                {
                    gains.Add(Time.time - hitAt);
                }
                prev = p.m_adrenaline;
                if (overAt < 0f && LogCount(huntMark, FightOffLine) > 0)
                {
                    overAt = Time.time - hitAt;
                }
                yield return null;
            }
            c.Check(CombatState.LastExchange == hitAt, "a real creature joined the fight during the hunt: its length could not be timed");
            c.Check(CombatState.LastTargeted >= hitAt + cap + 4f, "the hunt signal did not reach the mod until the end");
            var beyond = gains.Count(g => g > linger + 1.1f && g <= cap + 0.1f);
            c.Check(beyond >= Mathf.FloorToInt(cap - linger) - 3,
                $"hunted: {beyond} income tick(s) between {F(linger + 1.1f)} s and {F(cap)} s after the hit (expected about one per second)");
            var lastGain = gains.Count > 0 ? gains[gains.Count - 1] : -1f;
            c.Check(lastGain >= cap - 1.2f && lastGain <= cap + 0.15f,
                $"hunted: the last income tick came {F(lastGain)} s after the hit (expected within the last second before {F(cap)} s)");
            c.Check(overAt >= cap - 0.05f && overAt <= cap + 1.3f,
                $"hunted: '{FightOffLine}' came {(overAt >= 0f ? F(overAt) + " s" : "never")} after the hit (expected about {F(cap)} s), still hunted then");
            c.Check(Near(p.m_adrenaline, gains.Count * GainAt(p, tick, 0f), 0.01f),
                $"hunted: bar {F(p.m_adrenaline)} after {gains.Count} income ticks of {F(tick)}");

            // New fight, hunted for 9 s (past linger), then creature gives up: income stops soon after.
            CombatState.Reset();
            yield return new WaitForSeconds(1.2f);
            var stopMark = LogMark();
            p.m_adrenaline = 0f;
            HitFromPlayer(p, foe);
            hitAt = CombatState.LastExchange;
            gains.Clear();
            prev = p.m_adrenaline;
            while (Time.time - hitAt < linger + 3f)
            {
                p.OnTargeted(true, true);
                if (p.m_adrenaline > prev + 0.0001f)
                {
                    gains.Add(Time.time - hitAt);
                }
                prev = p.m_adrenaline;
                yield return null;
            }
            var stopAt = Time.time - hitAt;
            var lastTargeted = CombatState.LastTargeted - hitAt;
            c.Check(gains.Count(g => g > linger + 1.1f) >= 1, $"hunted past the linger: no income tick after {F(linger + 1.1f)} s");
            overAt = -1f;
            while (Time.time - hitAt < stopAt + 5f)
            {
                if (p.m_adrenaline > prev + 0.0001f)
                {
                    gains.Add(Time.time - hitAt);
                }
                prev = p.m_adrenaline;
                if (overAt < 0f && LogCount(stopMark, FightOffLine) > 0)
                {
                    overAt = Time.time - hitAt;
                }
                yield return null;
            }
            lastGain = gains.Count > 0 ? gains[gains.Count - 1] : -1f;
            var window = CombatState.TargetedWindowSeconds;
            c.Check(CombatState.LastExchange == hitAt, "a real creature joined the second fight: its end could not be timed");
            c.Check(lastGain <= lastTargeted + window + 0.15f,
                $"hunt over at {F(lastTargeted)} s: the last income tick came at {F(lastGain)} s (expected none later than {F(window)} s after the hunt)");
            c.Check(overAt >= lastTargeted + window - 0.05f && overAt <= lastTargeted + window + 1.3f,
                $"hunt over at {F(lastTargeted)} s: '{FightOffLine}' came at {(overAt >= 0f ? F(overAt) + " s" : "never")}");
            CheckCleanLog(c, mark);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.dodge-fight ----------

    // Creature's attack dodged: that creature alerted and targets player (its game says so about twice each
    // second), then attacker's game sends perfect-dodge message. Game pays its dodge amount, fight starts.
    private static IEnumerator RunDodgeFight()
    {
        var rig = Rig.Create(DodgeFightName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(DodgeFightName);
        var mark = LogMark();
        try
        {
            var p = rig.P;
            var rules = new TrinketRules();
            ServerRules.TestRules = rules;
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            if (!c.Check(item != null && MusicMan.instance != null,
                    "could not equip a trinket, or no music manager (the game's targeting call needs it)"))
            {
                c.Report();
                yield break;
            }
            rig.Effect(item.m_shared.m_fullAdrenalineSE.NameHash());

            // Two physics ticks out of roll first: game takes one dodge message per roll, older one forgotten now.
            CombatState.Reset();
            yield return FixedTicks(2);
            p.m_adrenaline = 0f;
            var pay = p.m_perfectDodgeAdrenaline;
            var want = Gain(p, pay);
            var calls = FullBar.Calls;
            p.RPC_OnTargeted(0L, true, true);
            var t0 = Time.time;
            p.RPC_HitWhileDodging(0L);
            c.Check(CombatState.LastExchange >= t0 && CombatState.InCombat(rules.CombatLingerSeconds),
                "a perfect dodge of a hunting creature's attack did not start a fight");
            c.Check(FullBar.Calls == calls + 1 && Near(FullBar.LastAmount, pay) && Near(p.m_adrenaline, want, 0.001f),
                $"perfect dodge: {FullBar.Calls - calls} adrenaline call(s) of {F(FullBar.LastAmount)}, bar 0 -> {F(p.m_adrenaline)} "
                + $"(the game's amount is {F(pay)}, adding {F(want)})");
            CheckCleanLog(c, mark);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.bug.pvp-dodge (real bug, M10) ----------

    // One check, alone. Another player's attack dodged (PvP): same perfect-dodge message arrives, and no creature
    // hunts player. Message carries no attacker, so this is all difference player's game can see. README and
    // design doc say other players do not count; mod 0.1.0 counts every perfect dodge: this fails until fixed.
    private static IEnumerator RunPvpDodgeBug()
    {
        var rig = Rig.Create(PvpDodgeBugName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var p = rig.P;
            ServerRules.TestRules = new TrinketRules();

            // Quiet moment: no real creature of world may hunt or fight player right now (else not PvP case).
            var quiet = false;
            for (var attempt = 0; attempt < 10 && !quiet; attempt++)
            {
                CombatState.Reset();
                yield return FixedTicks(2);
                quiet = float.IsNegativeInfinity(CombatState.LastTargeted) && float.IsNegativeInfinity(CombatState.LastExchange);
                if (!quiet)
                {
                    yield return new WaitForSeconds(1f);
                }
            }
            if (!quiet)
            {
                SelfTest.Fail(PvpDodgeBugName,
                    "a real creature hunts or fights the player: the PvP case (no creature hunting) could not be set up");
                yield break;
            }
            p.RPC_HitWhileDodging(0L);
            if (float.IsNegativeInfinity(CombatState.LastExchange))
            {
                SelfTest.Pass(PvpDodgeBugName, "1 checks OK");
            }
            else
            {
                SelfTest.Fail(PvpDodgeBugName,
                    "a perfect dodge with no creature hunting the player (what a dodged attack of another player looks like) "
                    + "started a fight: the README says other players (PvP) do not count");
            }
        }
        finally
        {
            rig.Restore();
        }
    }
}
#endif
