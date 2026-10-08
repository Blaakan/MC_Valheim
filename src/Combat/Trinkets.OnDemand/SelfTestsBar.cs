#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MC.Combat.TrinketsOnDemandMod.Patches;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Combat.TrinketsOnDemandMod;

// Debug build only. In-world self tests of bar and key, one per TESTING.md item (same class as SelfTests.cs):
//   trinkets.vanilla-parity  T01: mod off (patches gone) against mod on: tooltip = game's + one line right after
//                            full-adrenaline line, same cost, same bar capacity and HUD width, key pop = game's pop
//   trinkets.drain           T06: bar 20 out of fight for 60 s never lower; trinket off = game drains and hides it
//   trinkets.full-bar        T08: full bar waits through missed swing, hit pay, hit taken, dodge and stagger credit
//                            and one minute; key fires it (effect, HUD icon, pop object, Debug line). Then loss
//                            from full bar (no pop), effect with up-front gain (no loop), remote stagger credit
//   trinkets.answers         T09, T10: center texts of press that does not fire; refresh; refused while active, then
//                            fired once effect ran out
//   trinkets.swap            T16: costlier -> cheaper (stays full, number drops, cheaper one fires) and back (not full);
//                            RefuseWhileActive only looks at trinket worn now
//   trinkets.feedback        T15: flash and message on rising edge, flash again every 4 s, message at most every
//                            20 s, ShowFullMessage off, FullFlashInterval 0
//   trinkets.keys            T14: other key (label, tooltip, message), unreadable key (one warning, gamepad label),
//                            no input at all
//   trinkets.gates           T11: key ignored with chat, inventory, map, console, menu, radial, hammer; fires mid-swing
//                            and mid-roll; Music Instruments holding keys
//   trinkets.toggle          L01: off = game's drain after about 1 s, full bar pops on next hit or drains
//                            without firing; on again = key, no drain, tooltip line
//   trinkets.clean-log       L03: no warning or error from mod since game started (F13 one aside), no Unity
//                            warning about Flash parameter. Registered last.
internal static partial class SelfTests
{
    private const string ParityName = "trinkets.vanilla-parity";
    private const string DrainName = "trinkets.drain";
    private const string FullBarName = "trinkets.full-bar";
    private const string AnswersName = "trinkets.answers";
    private const string SwapName = "trinkets.swap";
    private const string FeedbackName = "trinkets.feedback";
    private const string KeysName = "trinkets.keys";
    private const string GatesName = "trinkets.gates";
    private const string ToggleName = "trinkets.toggle";
    private const string CleanLogName = "trinkets.clean-log";

    private const string TriggeredLine = "Trinket triggered by the player: bar emptied, effects added or refreshed.";
    private const string FightOnLine = "In a fight: adrenaline income on.";
    private const string FightOffLine = "Fight over: adrenaline income off.";
    private const string F13Warning = "Controls.TriggerKey = F13";

    // Line README and TESTING.md show for default key.
    private static string TriggerLine(string label) =>
        "\nTrigger: <color=orange>" + label + "</color> when the adrenaline bar is full";

    private static string FullMessage(string label) => "Adrenaline full: press " + label + " to trigger your trinket";

    private static float BarWidth()
    {
        var hud = Hud.instance;
        return hud != null && hud.m_adrenalineBarRoot != null ? hud.m_adrenalineBarRoot.rect.width : -1f;
    }

    // "adrenaline <n>" of console: one AddAdrenaline call for difference (Terminal).
    private static void ConsoleAdrenaline(Player p, float value) => p.AddAdrenaline(value - p.GetAdrenaline());

    // Trigger pressed right now, in this frame (what Player.Update postfix calls): for states that last one
    // frame only.
    private static void PressNow(Player p)
    {
        FullBar.LastResult = TriggerResult.None;
        Controls.TestPress = true;
        Trigger.Tick(p);
        Controls.TestPress = false;
    }

    // Bar from under max to max: rising edge Feedback.Tick sees in next frames.
    private static IEnumerator FullEdge(Player p, float max, bool clearCooldown)
    {
        p.m_adrenaline = Mathf.Max(0f, max - 1f);
        yield return Frames(2);
        if (clearCooldown)
        {
            Feedback.ClearMessageCooldown();
        }
        p.m_adrenaline = max;
        yield return Frames(3);
    }

    // ---------- trinkets.vanilla-parity (T01) ----------

    private static IEnumerator RunVanillaParity()
    {
        var rig = Rig.Create(ParityName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(ParityName);
        var mark = LogMark();
        try
        {
            var p = rig.P;
            var plugin = Self();
            if (!c.Check(plugin != null && plugin.IsActive && !plugin.TestOff, "the mod is not active"))
            {
                c.Report();
                yield break;
            }
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            if (!c.Check(item != null, "could not equip a trinket"))
            {
                c.Report();
                yield break;
            }
            var se = item.m_shared.m_fullAdrenalineSE;
            var hash = se.NameHash();
            rig.Effect(hash);
            var cost = item.m_shared.m_maxAdrenaline;
            var block = "\n$item_fulladrenaline: <color=orange>" + se.GetTooltipString() + "</color>";

            // Mod off (patches gone): normal game.
            plugin.TestTurnOff();
            yield return FixedTicks(2);
            p.UpdateModifiers();
            var maxOff = p.GetMaxAdrenaline();
            var tipOff = TipOf(item);
            c.Check(tipOff.IndexOf(block, StringComparison.Ordinal) >= 0 && !tipOff.Contains(TooltipMark),
                "mod off: the tooltip has the game's full-adrenaline line and no trigger line");
            p.m_adrenaline = 0.4f * maxOff; // some bar, so HUD sizes it
            p.m_adrenalineDegenTimer = 5f;
            yield return Frames(3);
            var widthOff = BarWidth();
            p.m_adrenaline = maxOff;
            p.AddAdrenaline(0f);
            var popOff = p.GetSEMan().GetStatusEffect(hash);
            c.Check(popOff != null && Near(p.m_adrenaline, 0f), "mod off: a full bar fires the trinket at once (normal game)");
            var ttlOff = popOff != null ? popOff.m_ttl : -1f;
            var textOff = popOff != null ? popOff.GetTooltipString() : null;
            var typeOff = popOff != null ? popOff.GetType() : null;
            rig.Effect(hash);

            // Mod on.
            plugin.TestTurnOn();
            ServerRules.TestRules = new TrinketRules();
            Controls.TestKey = KeyCode.Y; // item names [Y], default key, whatever player's own setting
            Controls.CacheKeys();
            yield return FixedTicks(2);
            p.UpdateModifiers();
            c.Check(Near(p.GetMaxAdrenaline(), maxOff) && Near(item.m_shared.m_maxAdrenaline, cost)
                    && ReferenceEquals(item.m_shared.m_fullAdrenalineSE, se),
                $"mod on: bar capacity {F(p.GetMaxAdrenaline())} (off {F(maxOff)}), cost and effect of the trinket unchanged");
            var line = Feedback.TooltipLine();
            var tipOn = TipOf(item);
            c.Check(tipOn == ItemDataPatches.Insert(tipOff, se, line) && tipOn.Replace(line, "") == tipOff,
                "mod on: the tooltip is the game's tooltip plus the one trigger line");
            var at = tipOn.IndexOf(line, StringComparison.Ordinal);
            var blockAt = tipOff.IndexOf(block, StringComparison.Ordinal);
            c.Check(blockAt >= 0 && at == blockAt + block.Length, "mod on: the trigger line comes right after the full-adrenaline line");
            if (c.Check(!ZInput.IsGamepadActive(), "a gamepad is in use: the keyboard text of the line cannot be checked"))
            {
                c.Check(line == TriggerLine("[Y]"), $"trigger line is '{line.Trim()}', expected 'Trigger: [Y] when the adrenaline bar is full'");
            }
            p.m_adrenaline = 0.4f * maxOff;
            p.m_adrenalineDegenTimer = 5f;
            yield return Frames(3);
            var widthOn = BarWidth();
            var hud = Hud.instance;
            var widthWant = hud != null ? maxOff / 25f * 64f + hud.m_staminaBarBorderBuffer : -1f;
            c.Check(widthOff > 0f && Near(widthOn, widthOff, 0.01f) && Near(widthOn, widthWant, 0.5f),
                $"HUD bar width {F(widthOn)} with the mod on, {F(widthOff)} with it off (the game's size for this capacity: {F(widthWant)})");

            p.m_adrenaline = maxOff;
            yield return Press();
            var popOn = p.GetSEMan().GetStatusEffect(hash);
            c.Check(FullBar.LastResult == TriggerResult.Fired && popOn != null && Near(p.m_adrenaline, 0f),
                $"mod on: the key fires the trinket (answer {FullBar.LastResult})");
            c.Check(popOn != null && typeOff != null && popOn.GetType() == typeOff && Near(popOn.m_ttl, ttlOff)
                    && popOn.GetTooltipString() == textOff && popOn.m_time < 1f,
                $"fired effect: duration {(popOn != null ? F(popOn.m_ttl) : "none")} s and numbers as the normal game's pop ({F(ttlOff)} s)");

            // Nothing of mod travels with item (hand-off, M03).
            var prefabShared = item.m_dropPrefab != null ? PrefabShared(item.m_dropPrefab.name) : null;
            c.Check(!item.m_customData.Keys.Any(k => k.StartsWith(ModInfo.Guid, StringComparison.Ordinal))
                    && prefabShared != null && ReferenceEquals(prefabShared.m_fullAdrenalineSE, se)
                    && Near(prefabShared.m_maxAdrenaline, cost),
                "after firing, the item carries no data of this mod and its prefab is untouched");
            CheckCleanLog(c, mark);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.drain (T06) ----------

    private static IEnumerator RunDrain()
    {
        var rig = Rig.Create(DrainName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(DrainName);
        var mark = LogMark();
        try
        {
            var p = rig.P;
            ServerRules.TestRules = new TrinketRules();
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            if (!c.Check(item != null, "could not equip a trinket"))
            {
                c.Report();
                yield break;
            }
            rig.Effect(item.m_shared.m_fullAdrenalineSE.NameHash());
            var max = p.GetMaxAdrenaline();
            if (!c.Check(max > 25f, $"the trinket's bar ({F(max)}) is too small for a bar of 20"))
            {
                c.Report();
                yield break;
            }

            // "adrenaline 20", then 60 seconds out of fight.
            CombatState.Reset();
            p.m_adrenaline = 0f;
            ConsoleAdrenaline(p, 20f);
            var start = p.m_adrenaline;
            var delay = p.m_adrenalineDegenTimer;
            c.Check(start > 0f && start < max && delay >= 5f && delay <= 10.5f,
                $"bar {F(start)} of {F(max)}; the normal game would start draining it {F(delay)} s after this gain");
            var since = Time.time;
            var low = start;
            var lowTimer = float.MaxValue;
            var t = 0f;
            while (t < 60f)
            {
                t += Time.deltaTime;
                low = Mathf.Min(low, p.m_adrenaline);
                if (t > delay + 1f)
                {
                    lowTimer = Mathf.Min(lowTimer, p.m_adrenalineDegenTimer);
                }
                yield return null;
            }
            c.Check(low >= start - 0.0001f, $"the bar drained in 60 s out of a fight (lowest {F(low)}, started at {F(start)})");
            c.Check(lowTimer >= Income.DegenFloor - 0.1f, $"the drain timer ran down to {F(lowTimer)} (kept at {F(Income.DegenFloor)} or more)");
            if (CombatState.LastExchange >= since || CombatState.LastTargeted >= since)
            {
                c.Check(p.m_adrenaline >= start - 0.0001f, "a real creature fought or hunted the player during the wait and the bar went down");
                c.Note($"a real creature fought or hunted the player during the wait: the bar is {F(p.m_adrenaline)} (income), 'never lower' was checked");
            }
            else
            {
                c.Check(Near(p.m_adrenaline, start), $"after 60 s out of a fight the bar is {F(p.m_adrenaline)}, expected {F(start)}");
            }

            // Trinket off: game drains bar and hides it.
            p.m_adrenaline = 6f;
            p.m_adrenalineDegenTimer = 0f;
            yield return FixedTicks(3);
            yield return Frames(2);
            var animator = Hud.instance != null ? Hud.instance.m_adrenalineAnimator : null;
            c.Check(Near(p.m_adrenaline, 6f) && animator != null && animator.GetBool("Visible"),
                $"with the trinket on, a bar of 6 stays and shows ({F(p.m_adrenaline)})");
            p.UnequipItem(item, false);
            p.UpdateModifiers();
            c.Check(p.GetMaxAdrenaline() <= 0f, $"no trinket: the bar still has capacity ({F(p.GetMaxAdrenaline())})");
            var before = p.m_adrenaline;
            yield return new WaitForSeconds(2.5f);
            c.Check(p.m_adrenaline < before - 0.3f, $"trinket unequipped: the bar went from {F(before)} to {F(p.m_adrenaline)} in 2.5 s (the game should drain it)");
            yield return Until(() => p.m_adrenaline <= 0f, 20f);
            yield return Frames(3);
            c.Check(p.m_adrenaline <= 0f && animator != null && !animator.GetBool("Visible"),
                $"trinket unequipped: bar {F(p.m_adrenaline)} after the drain, hidden {(animator != null && !animator.GetBool("Visible"))}");
            CheckCleanLog(c, mark);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.full-bar (T08, loss from full bar, up-front gain, remote credit) ----------

    // T08 on worn trinket: full bar waits whatever game sends, for waitSeconds more, then key fires it.
    // Also run by multiplayer test with server's rules. Sword in hand, foe behind player.
    private static IEnumerator FullBarCore(Checks c, Rig rig, ItemDrop.ItemData item, ItemDrop.ItemData sword,
        Character foe, float waitSeconds)
    {
        var p = rig.P;
        var se = item.m_shared.m_fullAdrenalineSE;
        var hash = se.NameHash();
        var max = p.GetMaxAdrenaline();
        bool Held() => Near(p.m_adrenaline, max) && !rig.Has(hash);

        // "adrenaline 999".
        p.m_adrenaline = 0f;
        ConsoleAdrenaline(p, 999f);
        c.Check(Held() && FullBar.IsFull(p), $"'adrenaline 999': the bar is {F(p.m_adrenaline)} of {F(max)}, no effect may start");

        // Swing that misses: game sends its miss amount (0).
        var zero = FullBar.ZeroCalls;
        var calls = FullBar.Calls;
        var started = new bool[1];
        yield return Swing(p, false, started);
        c.Check(started[0] && (p.m_attackMissAdrenaline == 0f ? FullBar.ZeroCalls > zero : FullBar.Calls > calls),
            "the sword swing at nothing did not reach the game's miss call");
        c.Check(Held(), $"full bar + a swing that misses: bar {F(p.m_adrenaline)}, effect {rig.Has(hash)}");

        // Hit on creature: game pays weapon's amount (call of Attack.DoMeleeAttack).
        calls = FullBar.Calls;
        HitFromPlayer(p, foe);
        p.AddAdrenaline(sword.m_shared.m_attack.m_attackAdrenaline * foe.m_enemyAdrenalineMultiplier);
        c.Check(FullBar.Calls > calls && Held(), $"full bar + the pay of a hit: bar {F(p.m_adrenaline)}, effect {rig.Has(hash)}");

        // Hit taken (no shield up), perfect dodge, stagger credited by another game.
        calls = FullBar.Calls;
        HitPlayerFront(p, foe, true, 0.1f);
        c.Check(FullBar.Calls > calls && Held(), $"full bar + a hit taken: bar {F(p.m_adrenaline)}, effect {rig.Has(hash)}");
        yield return Fixed;
        calls = FullBar.Calls;
        p.RPC_HitWhileDodging(0L);
        c.Check(FullBar.Calls > calls && Held(), $"full bar + a perfect dodge: bar {F(p.m_adrenaline)}, effect {rig.Has(hash)}");
        calls = FullBar.Calls;
        p.RPC_AddAdrenaline(0L, p.m_staggerEnemyAdrenaline);
        c.Check(FullBar.Calls > calls && Held(), $"full bar + stagger credit from another game: bar {F(p.m_adrenaline)}, effect {rig.Has(hash)}");
        c.Check(ReferenceEquals(item.m_shared.m_fullAdrenalineSE, se) && FullBar.Depth == 0 && FullBar.HiddenCount == 0,
            "effect put back after every call, no scope left open");

        // Out of fight (hits above end after linger), with game's drain due at once.
        p.m_adrenalineDegenTimer = 0f;
        var low = max;
        var popped = false;
        var t = 0f;
        while (t < waitSeconds)
        {
            t += Time.deltaTime;
            low = Mathf.Min(low, p.m_adrenaline);
            popped |= rig.Has(hash);
            yield return null;
        }
        c.Check(low >= max - 0.0001f && !popped && Held(),
            $"the full bar did not stay full for {F(waitSeconds)} s (lowest {F(low)} of {F(max)}, effect started {popped})");

        // Key.
        var pop = PopEffectName(p);
        var pops = CountObjects(pop);
        var mark = LogMark();
        yield return Press();
        var effect = p.GetSEMan().GetStatusEffect(hash);
        c.Check(FullBar.LastResult == TriggerResult.Fired && Near(p.m_adrenaline, 0f) && effect != null,
            $"the key did not fire the full bar (answer {FullBar.LastResult}, bar {F(p.m_adrenaline)})");
        c.Check(HudShows(p, hash), "the effect's icon is not in the HUD status effect row");
        c.Check(pop != null && CountObjects(pop) > pops, $"no new {pop ?? "pop effect"} object in the world after the key");
        c.Check(LogCount(mark, TriggeredLine) == 1, "Debug line 'Trinket triggered by the player...' not logged once");
    }

    private static IEnumerator RunFullBar()
    {
        var rig = Rig.Create(FullBarName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(FullBarName);
        var mark = LogMark();
        try
        {
            var p = rig.P;
            ServerRules.TestRules = new TrinketRules();
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            var fwd = Flat(p.transform.forward);
            var foe = rig.Tough(FoeName, -fwd * 5f); // behind: swing ahead hits nothing
            var sword = rig.Give(SwordName);
            if (!c.Check(item != null && foe != null && sword != null, $"could not equip a trinket, give {SwordName} or spawn {FoeName}"))
            {
                c.Report();
                yield break;
            }
            var se = item.m_shared.m_fullAdrenalineSE;
            var hash = se.NameHash();
            rig.Effect(hash);
            rig.EmptyHands();
            p.EquipItem(sword, false);
            rig.TakeControls();
            Face(p, fwd);
            yield return FixedTicks(3);
            var max = p.GetMaxAdrenaline();

            yield return FullBarCore(c, rig, item, sword, foe, 60f);

            // Loss never fires and not swallowed ("adrenaline <less>" on full bar, future loss on hit).
            rig.Effect(hash);
            p.m_adrenaline = max;
            p.AddAdrenaline(-5f);
            c.Check(Near(p.m_adrenaline, max - 5f) && !rig.Has(hash) && ReferenceEquals(item.m_shared.m_fullAdrenalineSE, se)
                    && FullBar.Depth == 0 && FullBar.HiddenCount == 0,
                $"loss of 5 from a full bar: bar {F(p.m_adrenaline)} (expected {F(max - 5f)}), effect {rig.Has(hash)}");
            p.m_adrenaline = 0.5f * max;
            p.AddAdrenaline(-5f);
            c.Check(Near(p.m_adrenaline, 0.5f * max - 5f) && !rig.Has(hash), $"loss of 5 from half a bar: bar {F(p.m_adrenaline)}");
            var gameLoss = p.m_nonBlockDamageAdrenaline; // game's own field: 0 in 1.0.16
            p.m_nonBlockDamageAdrenaline = -5f;
            p.m_adrenaline = max;
            var calls = FullBar.Calls;
            HitPlayerFront(p, foe, true, 0.1f);
            p.m_nonBlockDamageAdrenaline = gameLoss;
            c.Check(FullBar.Calls > calls && Near(p.m_adrenaline, max - 5f) && !rig.Has(hash),
                $"unblocked hit with a loss of 5 on a full bar: bar {F(p.m_adrenaline)} (expected {F(max - 5f)}), effect {rig.Has(hash)}");
            CombatState.Reset(); // that hit started fight: no income tick may land in checks below

            // Stagger credit from another game on bar that not full: paid as usual.
            p.m_adrenaline = 10f;
            var want = Gain(p, 3f);
            p.RPC_AddAdrenaline(0L, 3f);
            c.Check(Near(p.m_adrenaline, 10f + want), $"stagger credit of 3 on a bar of 10: bar {F(p.m_adrenaline)}");

            // Modded trinket whose effect gives adrenaline up front: one pop, no loop (normal game would call
            // itself without end here; if me ever lets that through, game stops instead of reporting).
            var stats = se as SE_Stats;
            p.m_adrenaline = max;
            p.AddAdrenaline(1f);
            var safe = Near(p.m_adrenaline, max) && !rig.Has(hash);
            if (c.Check(stats != null && safe, "the full bar is not held on a gain: up-front effect check not run"))
            {
                var clone = Object.Instantiate(stats);
                clone.name = "MC_TrinketsOnDemand_TestUpFront";
                clone.m_nameHash = 0;
                clone.m_adrenalineUpFront = 5f;
                var cloneHash = clone.NameHash();
                var shared = item.m_shared;
                try
                {
                    shared.m_fullAdrenalineSE = clone;
                    p.m_adrenaline = max;
                    yield return Press();
                    var running = p.GetSEMan().GetStatusEffect(cloneHash);
                    c.Check(FullBar.LastResult == TriggerResult.Fired && Near(p.m_adrenaline, 0f) && running != null
                            && FullBar.Depth == 0 && FullBar.HiddenCount == 0 && ReferenceEquals(shared.m_fullAdrenalineSE, clone),
                        $"up-front effect: first press {FullBar.LastResult}, bar {F(p.m_adrenaline)}, depth {FullBar.Depth}");
                    yield return new WaitForSeconds(0.4f);
                    var timeBefore = running != null ? running.m_time : 0f;
                    p.m_adrenaline = max;
                    yield return Press();
                    running = p.GetSEMan().GetStatusEffect(cloneHash);
                    c.Check(FullBar.LastResult == TriggerResult.Fired && Near(p.m_adrenaline, 0f) && running != null
                            && running.m_time < timeBefore && FullBar.Depth == 0 && FullBar.HiddenCount == 0
                            && ReferenceEquals(shared.m_fullAdrenalineSE, clone),
                        $"up-front effect: second press (refresh) {FullBar.LastResult}, bar {F(p.m_adrenaline)}, depth {FullBar.Depth}");
                }
                finally
                {
                    // Effect off player first: its copy shares clone's native object.
                    p.GetSEMan().RemoveStatusEffect(cloneHash, true);
                    shared.m_fullAdrenalineSE = se;
                    Object.Destroy(clone);
                }
            }
            CheckCleanLog(c, mark);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.answers (T09, T10) ----------

    private static IEnumerator RunAnswers()
    {
        var rig = Rig.Create(AnswersName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(AnswersName);
        var mark = LogMark();
        try
        {
            var p = rig.P;
            ServerRules.TestRules = new TrinketRules();
            var item = EquipNamedTrinket(c, rig, IronTrinket);
            if (!c.Check(item != null, "could not equip a trinket"))
            {
                c.Report();
                yield break;
            }
            var hash = item.m_shared.m_fullAdrenalineSE.NameHash();
            rig.Effect(hash);
            var max = p.GetMaxAdrenaline();
            c.Check(Feedback.NotFullText == "Adrenaline not full yet" && Feedback.NoTrinketText == "No trinket to trigger"
                    && Feedback.StillActiveText == "Trinket effect still active",
                "the three answers have the texts TESTING.md and the README give");

            // T09 (a): "adrenaline 10" with costlier trinket.
            p.m_adrenaline = 0f;
            ConsoleAdrenaline(p, 10f);
            var some = p.m_adrenaline;
            rig.MarkCenter();
            yield return Press();
            c.Check(FullBar.LastResult == TriggerResult.NotFull && Near(p.m_adrenaline, some) && !rig.Has(hash),
                $"bar {F(some)} of {F(max)}: answer {FullBar.LastResult}, bar now {F(p.m_adrenaline)} (nothing may be spent)");
            c.Check(Rig.Center() == Feedback.NotFullText, $"center message '{Rig.Center()}', expected '{Feedback.NotFullText}'");

            // T10: fire, fill at once, press again: effect starts over and bar empties.
            ConsoleAdrenaline(p, 999f);
            yield return Press();
            var running = p.GetSEMan().GetStatusEffect(hash);
            c.Check(FullBar.LastResult == TriggerResult.Fired && running != null && Near(p.m_adrenaline, 0f),
                $"full bar: the key fires (answer {FullBar.LastResult})");
            yield return new WaitForSeconds(0.6f);
            var timeBefore = running != null ? running.m_time : 0f;
            ConsoleAdrenaline(p, 999f);
            c.Check(Near(p.m_adrenaline, max), $"filled again while the effect runs: bar {F(p.m_adrenaline)} of {F(max)}");
            yield return Press();
            running = p.GetSEMan().GetStatusEffect(hash);
            c.Check(FullBar.LastResult == TriggerResult.Fired && Near(p.m_adrenaline, 0f) && running != null
                    && timeBefore > 0.3f && running.m_time < 0.2f,
                $"press while the effect runs: answer {FullBar.LastResult}, effect time {F(timeBefore)} -> "
                + $"{(running != null ? F(running.m_time) : "gone")} (starts over), bar {F(p.m_adrenaline)}");

            // RefuseWhileActive: refused with message, bar kept; once effect has run out, key fires.
            ServerRules.TestRules = new TrinketRules { RefuseWhileActive = true };
            ConsoleAdrenaline(p, 999f);
            rig.MarkCenter();
            yield return Press();
            c.Check(FullBar.LastResult == TriggerResult.StillActive && Near(p.m_adrenaline, max) && rig.Has(hash),
                $"RefuseWhileActive: answer {FullBar.LastResult}, bar {F(p.m_adrenaline)} of {F(max)}");
            c.Check(Rig.Center() == Feedback.StillActiveText, $"center message '{Rig.Center()}', expected '{Feedback.StillActiveText}'");
            running = p.GetSEMan().GetStatusEffect(hash);
            if (running != null && running.m_ttl > 0f)
            {
                running.m_time = running.m_ttl - 0.2f; // effect's own clock near its end: game ends it itself
            }
            yield return Until(() => !rig.Has(hash), 3f);
            c.Check(!rig.Has(hash) && Near(p.m_adrenaline, max), "the effect did not run out by itself, or the bar moved meanwhile");
            yield return Press();
            c.Check(FullBar.LastResult == TriggerResult.Fired && Near(p.m_adrenaline, 0f) && rig.Has(hash),
                $"RefuseWhileActive, effect over: answer {FullBar.LastResult} (the key should fire again)");

            // T09 (b): no trinket worn.
            ServerRules.TestRules = new TrinketRules();
            rig.Effect(hash);
            p.UnequipItem(item, false);
            p.UpdateModifiers();
            rig.MarkCenter();
            yield return Press();
            c.Check(FullBar.LastResult == TriggerResult.NoTrinket, $"no trinket: answer {FullBar.LastResult}");
            c.Check(Rig.Center() == Feedback.NoTrinketText, $"center message '{Rig.Center()}', expected '{Feedback.NoTrinketText}'");
            CheckCleanLog(c, mark);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.swap (T16, RefuseWhileActive across swap) ----------

    private static IEnumerator RunSwap()
    {
        var rig = Rig.Create(SwapName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(SwapName);
        var mark = LogMark();
        try
        {
            var p = rig.P;
            ServerRules.TestRules = new TrinketRules();
            if (p.m_trinketItem != null)
            {
                p.UnequipItem(p.m_trinketItem, false);
            }
            var iron = rig.Give(IronTrinket);
            var bronze = rig.Give(BronzeTrinket);
            if (!c.Check(iron != null && bronze != null && iron.m_shared.m_maxAdrenaline > bronze.m_shared.m_maxAdrenaline,
                    $"need {IronTrinket} (costlier) and {BronzeTrinket} (cheaper)"))
            {
                c.Report();
                yield break;
            }
            var ironHash = iron.m_shared.m_fullAdrenalineSE.NameHash();
            var bronzeHash = bronze.m_shared.m_fullAdrenalineSE.NameHash();
            rig.Effect(ironHash);
            rig.Effect(bronzeHash);

            // Full bar with costlier one, swap to cheaper one.
            c.Check(p.EquipItem(iron, false), $"could not equip {IronTrinket}");
            p.UpdateModifiers();
            var maxIron = p.GetMaxAdrenaline();
            p.m_adrenaline = 0f;
            ConsoleAdrenaline(p, 999f);
            c.Check(Near(p.m_adrenaline, maxIron) && FullBar.IsFull(p), $"full bar with {IronTrinket}: {F(p.m_adrenaline)} of {F(maxIron)}");
            c.Check(p.EquipItem(bronze, false) && ReferenceEquals(p.m_trinketItem, bronze), $"could not swap to {BronzeTrinket}");
            yield return FixedTicks(3);
            yield return Frames(3);
            var maxBronze = p.GetMaxAdrenaline();
            c.Check(maxBronze < maxIron && Near(p.m_adrenaline, maxBronze) && FullBar.IsFull(p) && !rig.Has(ironHash) && !rig.Has(bronzeHash),
                $"after the swap the bar is {F(p.m_adrenaline)} of {F(maxBronze)} (was {F(maxIron)}): it should be full at the cheaper cost, nothing fired");
            var number = Hud.instance != null && Hud.instance.m_adrenalineText != null ? Hud.instance.m_adrenalineText.text : null;
            c.Check(number == Mathf.FloorToInt(maxBronze).ToString(), $"the HUD number is '{number}', expected '{Mathf.FloorToInt(maxBronze)}'");
            yield return Press();
            c.Check(FullBar.LastResult == TriggerResult.Fired && rig.Has(bronzeHash) && !rig.Has(ironHash) && Near(p.m_adrenaline, 0f),
                $"key after the swap: answer {FullBar.LastResult}, cheaper effect {rig.Has(bronzeHash)}, costlier effect {rig.Has(ironHash)}");

            // Full bar with cheaper one, swap to costlier one.
            rig.Effect(bronzeHash);
            ConsoleAdrenaline(p, 999f);
            c.Check(Near(p.m_adrenaline, maxBronze), $"full bar with {BronzeTrinket}: {F(p.m_adrenaline)} of {F(maxBronze)}");
            c.Check(p.EquipItem(iron, false) && ReferenceEquals(p.m_trinketItem, iron), $"could not swap to {IronTrinket}");
            yield return FixedTicks(3);
            yield return Frames(3);
            var hud = Hud.instance;
            var widthWant = hud != null ? maxIron / 25f * 64f + hud.m_staminaBarBorderBuffer : -1f;
            c.Check(Near(p.m_adrenaline, maxBronze) && Near(p.GetMaxAdrenaline(), maxIron) && !FullBar.IsFull(p)
                    && !rig.Has(ironHash) && !rig.Has(bronzeHash),
                $"after the swap back the bar is {F(p.m_adrenaline)} of {F(p.GetMaxAdrenaline())}: same amount, no longer full");
            c.Check(Near(BarWidth(), widthWant, 0.5f), $"HUD bar width {F(BarWidth())}, the wider bar of the costlier trinket is {F(widthWant)}");
            rig.MarkCenter();
            yield return Press();
            c.Check(FullBar.LastResult == TriggerResult.NotFull && Near(p.m_adrenaline, maxBronze) && Rig.Center() == Feedback.NotFullText,
                $"key after the swap back: answer {FullBar.LastResult}, message '{Rig.Center()}'");

            // RefuseWhileActive looks at trinket worn now: other trinket fires while first one's effect
            // still runs; back on first one, key refused.
            ServerRules.TestRules = new TrinketRules { RefuseWhileActive = true };
            c.Check(p.EquipItem(bronze, false), $"could not swap to {BronzeTrinket}");
            yield return FixedTicks(3);
            p.m_adrenaline = 0f;
            ConsoleAdrenaline(p, 999f);
            yield return Press();
            c.Check(FullBar.LastResult == TriggerResult.Fired && rig.Has(bronzeHash), $"refuse rule: first press with {BronzeTrinket}: {FullBar.LastResult}");
            c.Check(p.EquipItem(iron, false), $"could not swap to {IronTrinket}");
            yield return FixedTicks(3);
            ConsoleAdrenaline(p, 999f);
            yield return Press();
            c.Check(FullBar.LastResult == TriggerResult.Fired && rig.Has(bronzeHash) && rig.Has(ironHash) && Near(p.m_adrenaline, 0f),
                $"refuse rule: {IronTrinket} worn while the {BronzeTrinket} effect runs: answer {FullBar.LastResult} (it should fire)");
            c.Check(p.EquipItem(bronze, false), $"could not swap back to {BronzeTrinket}");
            yield return FixedTicks(3);
            ConsoleAdrenaline(p, 999f);
            var full = p.m_adrenaline;
            rig.MarkCenter();
            yield return Press();
            c.Check(FullBar.LastResult == TriggerResult.StillActive && Near(p.m_adrenaline, full) && Near(full, maxBronze)
                    && Rig.Center() == Feedback.StillActiveText,
                $"refuse rule: back on {BronzeTrinket} while its effect runs: answer {FullBar.LastResult}, bar {F(p.m_adrenaline)}, message '{Rig.Center()}'");
            CheckCleanLog(c, mark);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.feedback (T15) ----------

    private static IEnumerator RunFeedback()
    {
        var rig = Rig.Create(FeedbackName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(FeedbackName);
        var mark = LogMark();
        try
        {
            var p = rig.P;
            ServerRules.TestRules = new TrinketRules();
            // Defaults item names, whatever player's own settings.
            Feedback.TestShowFullMessage = true;
            Feedback.TestFlashInterval = 4f;
            Controls.TestKey = KeyCode.Y;
            Controls.CacheKeys();
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            if (!c.Check(item != null, "could not equip a trinket"))
            {
                c.Report();
                yield break;
            }
            rig.Effect(item.m_shared.m_fullAdrenalineSE.NameHash());
            var max = p.GetMaxAdrenaline();
            var hud = Hud.instance;
            c.Check(hud != null && Feedback.CanFlash(hud), "the HUD adrenaline bar has no Flash trigger: the bar cannot flash");
            var keyboard = c.Check(!ZInput.IsGamepadActive(), "a gamepad is in use: the [Y] text of the message cannot be checked");
            var text = FullMessage("[Y]");

            // Bar becomes full in fight: last income tick takes it to max.
            p.m_adrenaline = max - 0.5f;
            yield return Frames(2);
            Feedback.ClearMessageCooldown();
            var flashes = Feedback.FlashCount;
            var messages = Feedback.FullMessageCount;
            CombatState.MarkExchange();
            yield return Until(() => FullBar.IsFull(p), 2.5f);
            yield return Frames(2);
            var t0 = Time.time;
            c.Check(FullBar.IsFull(p) && Feedback.FlashCount == flashes + 1, $"bar full by income: {Feedback.FlashCount - flashes} flash(es), expected 1");
            c.Check(Feedback.FullMessageCount == messages + 1, $"bar full by income: {Feedback.FullMessageCount - messages} message(s), expected 1");
            if (keyboard)
            {
                c.Check(Feedback.LastFullMessage == text && TopLeftHas(text),
                    $"top-left message '{Feedback.LastFullMessage}' (in the game's message line: {TopLeftHas(text)}), expected '{text}'");
            }
            SelfTest.Screenshot(FeedbackName, "full-bar");
            yield return Frames(2);

            // It stays full: one more flash every 4 seconds, no more message.
            var times = new List<float>();
            var seen = Feedback.FlashCount;
            while (Time.time - t0 < 8.7f)
            {
                p.m_adrenaline = max;
                if (Feedback.FlashCount != seen)
                {
                    seen = Feedback.FlashCount;
                    times.Add(Time.time - t0);
                }
                yield return null;
            }
            c.Check(times.Count == 2 && Mathf.Abs(times[0] - 4f) < 0.35f && Mathf.Abs(times[1] - 8f) < 0.35f,
                $"while full the bar flashed again at {string.Join(", ", times.Select(F).ToArray())} s (expected at 4 and 8 s)");
            c.Check(Feedback.FullMessageCount == messages + 1, "the message came again while the bar stayed full");

            // Full again 9 s after first message: flash, no message (at most every 20 s).
            flashes = Feedback.FlashCount;
            yield return FullEdge(p, max, false);
            c.Check(Feedback.FlashCount == flashes + 1 && Feedback.FullMessageCount == messages + 1,
                $"full again {F(Time.time - t0)} s after the message: {Feedback.FlashCount - flashes} flash(es) (expected 1), "
                + $"{Feedback.FullMessageCount - messages - 1} new message(s) (expected 0)");
            while (Time.time - t0 < Feedback.MessageCooldown + 0.3f)
            {
                p.m_adrenaline = max;
                yield return null;
            }
            flashes = Feedback.FlashCount;
            yield return FullEdge(p, max, false);
            c.Check(Feedback.FlashCount == flashes + 1 && Feedback.FullMessageCount == messages + 2,
                $"full again {F(Time.time - t0)} s after the message: {Feedback.FullMessageCount - messages - 1} new message(s) (expected 1)");

            // ShowFullMessage = false: no message, flash stays.
            Feedback.TestShowFullMessage = false;
            flashes = Feedback.FlashCount;
            messages = Feedback.FullMessageCount;
            yield return FullEdge(p, max, true);
            c.Check(Feedback.FlashCount == flashes + 1 && Feedback.FullMessageCount == messages,
                $"ShowFullMessage off: {Feedback.FlashCount - flashes} flash(es) (expected 1), {Feedback.FullMessageCount - messages} message(s) (expected 0)");

            // FullFlashInterval = 0: one flash only.
            Feedback.TestShowFullMessage = true;
            Feedback.TestFlashInterval = 0f;
            flashes = Feedback.FlashCount;
            yield return FullEdge(p, max, false);
            var t1 = Time.time;
            while (Time.time - t1 < 5f)
            {
                p.m_adrenaline = max;
                yield return null;
            }
            c.Check(Feedback.FlashCount == flashes + 1, $"FullFlashInterval 0: {Feedback.FlashCount - flashes} flash(es) in 5 s on a full bar (expected 1)");
            CheckCleanLog(c, mark);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.keys (T14, gamepad label of T12) ----------

    private static IEnumerator RunKeys()
    {
        var rig = Rig.Create(KeysName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(KeysName);
        var mark = LogMark();
        var warned = Controls.TestWarned;
        try
        {
            var p = rig.P;
            ServerRules.TestRules = new TrinketRules();
            Feedback.TestShowFullMessage = true;
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            if (!c.Check(item != null, "could not equip a trinket"))
            {
                c.Report();
                yield break;
            }
            rig.Effect(item.m_shared.m_fullAdrenalineSE.NameHash());
            var max = p.GetMaxAdrenaline();
            var keyboard = c.Check(!ZInput.IsGamepadActive(), "a gamepad is in use: the keyboard labels cannot be checked");

            // TriggerKey = U (gamepad pair at its default).
            Controls.TestPadModifier = GamepadModifier.LeftTrigger;
            Controls.TestPadButton = GamepadButton.RightStick;
            Controls.TestKey = KeyCode.U;
            Controls.CacheKeys();
            c.Check(Controls.Key == KeyCode.U && Controls.KeyLabel(KeyCode.U) == "[U]", $"TriggerKey U: key {Controls.Key}, label {Controls.KeyLabel(KeyCode.U)}");
            if (keyboard)
            {
                var tip = TipOf(item);
                c.Check(Controls.Label() == "[U]" && tip.Contains(TriggerLine("[U]")) && !tip.Contains("[Y]"),
                    "TriggerKey U: the tooltip line does not say [U]");
                yield return FullEdge(p, max, true);
                c.Check(Feedback.LastFullMessage == FullMessage("[U]") && TopLeftHas(FullMessage("[U]")),
                    $"TriggerKey U: full-bar message '{Feedback.LastFullMessage}'");
            }

            // TriggerKey = F13: one warning, keyboard trigger off, texts name gamepad pair.
            Controls.TestWarned = KeyCode.None;
            var keyMark = LogMark();
            Controls.TestKey = KeyCode.F13;
            Controls.CacheKeys();
            Controls.CacheKeys();
            var lines = LogLines(keyMark, F13Warning);
            c.Check(!Controls.IsUsableKey(KeyCode.F13) && Controls.Key == KeyCode.None, $"TriggerKey F13: key in use is {Controls.Key} (expected none)");
            c.Check(lines.Count == 1 && lines[0].StartsWith(
                        "Controls.TriggerKey = F13: the game cannot read this key, so the keyboard trigger is off.", StringComparison.Ordinal),
                $"TriggerKey F13: {lines.Count} warning line(s): {(lines.Count > 0 ? lines[0] : "none")}");
            var pad = Controls.PadLabel();
            c.Check(pad != null && pad.Contains(" + ") && Controls.Label() == pad,
                $"TriggerKey F13: label '{Controls.Label()}', expected the gamepad pair '{pad}'");
            c.Check(Controls.PadModifierName == "JoyLTrigger" && Controls.PadButtonName == "JoyRStick",
                $"default gamepad pair reads {Controls.PadModifierName} + {Controls.PadButtonName}");
            if (pad != null)
            {
                yield return FullEdge(p, max, true);
                c.Check(Feedback.LastFullMessage == FullMessage(pad) && TopLeftHas(FullMessage(pad)),
                    $"TriggerKey F13: full-bar message '{Feedback.LastFullMessage}', expected the gamepad buttons");
                c.Check(TipOf(item).Contains(TriggerLine(pad)), "TriggerKey F13: the tooltip line does not name the gamepad buttons");
            }

            // TriggerKey = None and GamepadButton = None.
            Controls.TestKey = KeyCode.None;
            Controls.TestPadButton = GamepadButton.None;
            Controls.CacheKeys();
            c.Check(Controls.Label() == null && Controls.Key == KeyCode.None && Controls.PadButtonName == null && !Controls.Pressed(),
                $"no key and no gamepad button: label '{Controls.Label()}'");
            yield return FullEdge(p, max, true);
            var noKey = "Adrenaline full: set a trigger key for " + ModInfo.Name + " to trigger your trinket";
            c.Check(Feedback.LastFullMessage == noKey && TopLeftHas(noKey), $"no input: full-bar message '{Feedback.LastFullMessage}'");
            c.Check(TipOf(item).Contains("\nTrigger: no key set (" + ModInfo.Name + " settings)"), "no input: the tooltip does not say 'no key set'");

            // Back to player's own inputs.
            ClearOverrides();
            ServerRules.TestRules = new TrinketRules();
            c.Check(Controls.Key != KeyCode.F13, "the test key stayed after the overrides were cleared");
            CheckCleanLog(c, mark, F13Warning);
        }
        finally
        {
            Controls.TestWarned = warned;
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.gates (T11, Music Instruments keys) ----------

    private static IEnumerator RunGates()
    {
        var rig = Rig.Create(GatesName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(GatesName);
        var mark = LogMark();
        var inventoryOpened = false;
        var menuOpened = false;
        var mapOpened = false;
        var mapMode = Minimap.MapMode.Small;
        var consoleOpened = false;
        var chatOpened = false;
        try
        {
            var p = rig.P;
            ServerRules.TestRules = new TrinketRules();
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            var sword = rig.Give(SwordName);
            var hammer = rig.Give(HammerName);
            if (!c.Check(item != null && sword != null && hammer != null, $"could not equip a trinket or give {SwordName} and {HammerName}"))
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
            yield return FixedTicks(2);

            void Arm()
            {
                rig.Effect(hash);
                p.m_adrenaline = max;
                rig.MarkCenter();
            }

            void Ignored(string what, bool open)
            {
                c.Check(open, $"{what}: the test could not open it");
                c.Check(FullBar.LastResult == TriggerResult.Blocked && Near(p.m_adrenaline, max) && !rig.Has(hash)
                        && Rig.Center() == Rig.NoMessage,
                    $"{what}: the key was not ignored (answer {FullBar.LastResult}, bar {F(p.m_adrenaline)} of {F(max)}, "
                    + $"effect {rig.Has(hash)}, message '{Rig.Center()}')");
            }

            // Sanity: with nothing open key works (else every "ignored" below proves nothing).
            Arm();
            yield return Press();
            c.Check(FullBar.LastResult == TriggerResult.Fired, $"nothing open: answer {FullBar.LastResult} (expected Fired)");

            // (a) Chat focused (what typing message does).
            var chat = Chat.instance;
            if (c.Check(chat != null && chat.m_input != null && chat.m_chatWindow != null, "no chat window"))
            {
                chatOpened = true;
                chat.m_hideTimer = 0f;
                chat.m_chatWindow.gameObject.SetActive(true);
                chat.m_input.gameObject.SetActive(true);
                chat.m_input.ActivateInputField();
                yield return Until(() => chat.HasFocus(), 1.5f);
                Arm();
                var focused = chat.HasFocus();
                yield return Press();
                Ignored("chat open", focused && chat.HasFocus());
                CloseChat(chat);
                chatOpened = false;
                yield return Until(() => !chat.HasFocus(), 1.5f);
                yield return Frames(3);
            }

            // (b) Inventory, map, console, Esc menu.
            if (c.Check(InventoryGui.instance != null, "no inventory window"))
            {
                inventoryOpened = true;
                InventoryGui.instance.Show(null);
                yield return Frames(2);
                Arm();
                var open = InventoryGui.IsVisible();
                yield return Press();
                Ignored("inventory open", open && InventoryGui.IsVisible());
                InventoryGui.instance.Hide();
                inventoryOpened = false;
                yield return Until(() => !InventoryGui.IsVisible(), 2f);
                yield return Frames(3);
            }
            if (c.Check(Minimap.instance != null, "no map"))
            {
                mapMode = Minimap.instance.m_mode;
                mapOpened = true;
                Minimap.instance.SetMapMode(Minimap.MapMode.Large);
                yield return Frames(2);
                Arm();
                var open = Minimap.IsOpen() && Minimap.instance.m_mode == Minimap.MapMode.Large;
                yield return Press();
                Ignored("map open", open && Minimap.IsOpen());
                Minimap.instance.SetMapMode(mapMode);
                mapOpened = false;
                yield return Until(() => !Minimap.IsOpen(), 2f);
                yield return Frames(3);
            }
            if (c.Check(Console.instance != null && Console.instance.m_chatWindow != null, "no console window"))
            {
                consoleOpened = true;
                Console.instance.m_chatWindow.gameObject.SetActive(true);
                yield return Frames(2);
                Arm();
                var open = Console.IsVisible();
                yield return Press();
                Ignored("console open", open && Console.IsVisible());
                Console.instance.m_chatWindow.gameObject.SetActive(false);
                consoleOpened = false;
                yield return Frames(3);
            }
            if (c.Check(Menu.instance != null, "no Esc menu"))
            {
                menuOpened = true;
                Menu.instance.Show();
                yield return Frames(3);
                Arm();
                var open = Menu.IsVisible();
                yield return Press();
                Ignored("Esc menu open", open && Menu.IsVisible());
                Menu.instance.Hide();
                menuOpened = false;
                yield return Until(() => !Menu.IsVisible(), 2f);
                yield return Frames(3);
            }

            // (c) Radial menu (opened way game opens it; it may close itself next frame, so press is
            // made in same frame).
            Arm();
            var radialOpen = false;
            string radialError = null;
            try
            {
                var radial = Hud.instance != null ? Hud.instance.m_radialMenu : null;
                if (radial != null)
                {
                    radial.CanOpen = true;
                    radial.Open(Hud.instance.m_config);
                    radialOpen = Hud.InRadial();
                    PressNow(p);
                    radial.Close(true);
                }
            }
            catch (Exception e)
            {
                radialError = e.GetType().Name + ": " + e.Message;
            }
            Ignored("radial menu open" + (radialError != null ? " (" + radialError + ")" : ""), radialOpen);
            c.Check(!Hud.InRadial(), "the radial menu stayed open after the test closed it");
            yield return Frames(3);

            // (d) Build mode (hammer in hand).
            c.Check(p.EquipItem(hammer, false), $"could not equip {HammerName}");
            yield return FixedTicks(2);
            Arm();
            var building = p.InPlaceMode();
            yield return Press();
            Ignored("build mode", building);
            p.UnequipItem(hammer, false);
            yield return FixedTicks(2);
            c.Check(!p.InPlaceMode(), "still in build mode after the hammer was put away");

            // (e) Mid-swing: fires at once, swing goes on.
            c.Check(p.EquipItem(sword, false), $"could not equip {SwordName}");
            Face(p, Flat(p.transform.forward));
            yield return Until(() => Calm(p), 3f);
            var swung = p.StartAttack(null, false);
            yield return Until(() => p.InAttack(), 1.5f);
            var attack = p.m_currentAttack;
            Arm();
            var inSwing = p.InAttack();
            yield return Press();
            c.Check(swung && inSwing, "the sword swing did not start");
            c.Check(FullBar.LastResult == TriggerResult.Fired && rig.Has(hash) && Near(p.m_adrenaline, 0f),
                $"mid-swing: answer {FullBar.LastResult} (expected Fired)");
            c.Check(p.InAttack() && ReferenceEquals(p.m_currentAttack, attack), "mid-swing: the swing stopped when the trinket fired");
            yield return Until(() => !p.InAttack(), 4f);

            // (e) Mid-roll.
            yield return Until(() => Calm(p), 3f);
            p.Dodge(Flat(p.transform.forward));
            yield return Until(() => p.InDodge(), 2f);
            Arm();
            var inRoll = p.InDodge();
            yield return Press();
            c.Check(inRoll, "the roll did not start");
            c.Check(FullBar.LastResult == TriggerResult.Fired && rig.Has(hash) && Near(p.m_adrenaline, 0f),
                $"mid-roll: answer {FullBar.LastResult} (expected Fired)");
            c.Check(p.InDodge(), "mid-roll: the roll stopped when the trinket fired");
            yield return Until(() => !p.InDodge(), 4f);

            // Music Instruments holds keyboard while its free play runs (Y and U are piano keys there).
            if (ModActive(MusicGuid) && Chat.instance != null)
            {
                var capture = AccessTools.TypeByName("MC.Exploration.MusicInstrumentsMod.KeyCapture");
                var hold = capture != null ? AccessTools.Method(capture, "Hold") : null;
                var release = capture != null ? AccessTools.Method(capture, "Release") : null;
                if (c.Check(hold != null && release != null, "Music Instruments is active but its key capture was not found"))
                {
                    Arm();
                    hold.Invoke(null, null);
                    var held = Chat.instance.HasFocus();
                    PressNow(p);
                    release.Invoke(null, null);
                    Ignored("Music Instruments holding the keys", held);
                    yield return Frames(3);
                    Arm();
                    yield return Press();
                    c.Check(FullBar.LastResult == TriggerResult.Fired, $"after Music Instruments let go of the keys: answer {FullBar.LastResult}");
                }
            }
            else
            {
                c.Note("Music Instruments is not loaded or not active: its key check was skipped");
            }
            CheckCleanLog(c, mark);
        }
        finally
        {
            try
            {
                if (chatOpened && Chat.instance != null)
                {
                    CloseChat(Chat.instance);
                }
                if (inventoryOpened && InventoryGui.instance != null)
                {
                    InventoryGui.instance.Hide();
                }
                if (mapOpened && Minimap.instance != null)
                {
                    Minimap.instance.SetMapMode(mapMode);
                }
                if (consoleOpened && Console.instance != null)
                {
                    Console.instance.m_chatWindow.gameObject.SetActive(false);
                }
                if (menuOpened && Menu.instance != null)
                {
                    Menu.instance.Hide();
                }
                var radial = Hud.instance != null ? Hud.instance.m_radialMenu : null;
                if (radial != null && Hud.InRadial())
                {
                    radial.Close(true);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up (windows) failed: {e}");
            }
            rig.Restore();
        }
        c.Report();
    }

    // What game does when Esc leaves chat line.
    private static void CloseChat(Chat chat)
    {
        if (UnityEngine.EventSystems.EventSystem.current != null)
        {
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        }
        chat.m_input.DeactivateInputField();
        chat.m_input.gameObject.SetActive(false);
        chat.Hide();
    }

    // ---------- trinkets.toggle (L01, off state of L02) ----------

    private static IEnumerator RunToggle()
    {
        var rig = Rig.Create(ToggleName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(ToggleName);
        var mark = LogMark();
        try
        {
            var p = rig.P;
            var plugin = Self();
            if (!c.Check(plugin != null && plugin.IsActive && !plugin.TestOff, "the mod is not active"))
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = new TrinketRules();
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            var fwd = Flat(p.transform.forward);
            var foe = rig.Tough(FoeName, fwd * 3f);
            if (!c.Check(item != null && foe != null, $"could not equip a trinket or spawn {FoeName}"))
            {
                c.Report();
                yield break;
            }
            var hash = item.m_shared.m_fullAdrenalineSE.NameHash();
            rig.Effect(hash);
            var max = p.GetMaxAdrenaline();
            var hitPay = foe.m_enemyAdrenalineMultiplier; // one-handed weapon pays 1 per enemy hit (Attack default)

            // (a) Bar 20 held for long time (game's drain delay long over: timer at my floor), then off.
            p.m_adrenaline = 0f;
            ConsoleAdrenaline(p, 20f);
            var start = p.m_adrenaline;
            p.m_adrenalineDegenTimer = 0f;
            yield return FixedTicks(5);
            c.Check(Near(p.m_adrenaline, start) && p.m_adrenalineDegenTimer >= Income.DegenFloor - 0.1f,
                $"mod on: bar {F(p.m_adrenaline)} (expected {F(start)}), drain timer {F(p.m_adrenalineDegenTimer)}");
            plugin.TestTurnOff();
            var t0 = Time.time;
            while (Time.time - t0 < 3f && p.m_adrenaline >= start - 0.0001f)
            {
                yield return null;
            }
            var took = Time.time - t0;
            c.Check(p.m_adrenaline < start && took >= 0.5f && took <= 1.6f,
                $"mod turned off: the bar started draining after {F(took)} s (expected about 1 s), bar {F(p.m_adrenaline)}");

            // Off: key does nothing, no tooltip line.
            var bar = p.m_adrenaline;
            rig.MarkCenter();
            yield return Press();
            c.Check(FullBar.LastResult == TriggerResult.None && !rig.Has(hash) && p.m_adrenaline <= bar && Rig.Center() == Rig.NoMessage,
                $"mod off: the key did something (answer {FullBar.LastResult}, message '{Rig.Center()}')");
            c.Check(!TipOf(item).Contains(TooltipMark), "mod off: the tooltip still has the trigger line");

            // (b) On, "adrenaline 0" then "adrenaline 999" (held full, game's drain delay at its longest), off,
            // then hit: game's full-bar rule fires trinket on that hit.
            plugin.TestTurnOn();
            ServerRules.TestRules = new TrinketRules();
            yield return FixedTicks(2);
            ConsoleAdrenaline(p, 0f);
            ConsoleAdrenaline(p, 999f);
            var delay = p.m_adrenalineDegenTimer;
            c.Check(Near(p.m_adrenaline, max) && !rig.Has(hash) && delay > 5f, $"mod on: held full ({F(p.m_adrenaline)} of {F(max)}), drain delay {F(delay)} s");
            plugin.TestTurnOff();
            yield return new WaitForSeconds(1f);
            c.Check(Near(p.m_adrenaline, max) && !rig.Has(hash), $"mod off for 1 s: the held bar changed before any hit ({F(p.m_adrenaline)})");
            HitFromPlayer(p, foe);
            p.AddAdrenaline(hitPay); // what game's melee hit pays
            c.Check(Near(p.m_adrenaline, 0f) && rig.Has(hash), $"mod off: the next hit did not fire the held bar (bar {F(p.m_adrenaline)}, effect {rig.Has(hash)})");
            rig.Effect(hash);

            // On, held full same way, off, nothing: game drains it from full without firing.
            plugin.TestTurnOn();
            ServerRules.TestRules = new TrinketRules();
            yield return FixedTicks(2);
            ConsoleAdrenaline(p, 0f);
            ConsoleAdrenaline(p, 999f);
            delay = p.m_adrenalineDegenTimer;
            c.Check(Near(p.m_adrenaline, max) && !rig.Has(hash), $"mod on: held full again ({F(p.m_adrenaline)} of {F(max)})");
            plugin.TestTurnOff();
            var fired = false;
            t0 = Time.time;
            while (Time.time - t0 < delay + 3f && p.m_adrenaline >= max - 1f)
            {
                fired |= rig.Has(hash);
                yield return null;
            }
            took = Time.time - t0;
            c.Check(p.m_adrenaline < max - 1f + 0.0001f && p.m_adrenaline > 0.5f * max && !fired && !rig.Has(hash)
                    && took <= delay + 1.5f && took >= delay - 1f,
                $"mod off, nothing done: after {F(took)} s (drain delay {F(delay)} s) the bar is {F(p.m_adrenaline)} of {F(max)}, fired {fired || rig.Has(hash)}");

            // (c) On again: tooltip line, no drain, key.
            plugin.TestTurnOn();
            ServerRules.TestRules = new TrinketRules();
            yield return FixedTicks(2);
            c.Check(plugin.IsActive && !plugin.TestOff && TipOf(item).Contains(TooltipMark), "mod on again: the tooltip line is not back");
            bar = p.m_adrenaline;
            p.m_adrenalineDegenTimer = 0f;
            yield return new WaitForSeconds(2f);
            c.Check(Near(p.m_adrenaline, bar), $"mod on again: the bar went from {F(bar)} to {F(p.m_adrenaline)} in 2 s (it should stop draining)");
            p.m_adrenaline = max;
            p.AddAdrenaline(1f);
            c.Check(Near(p.m_adrenaline, max) && !rig.Has(hash), "mod on again: the full bar does not wait");
            yield return Press();
            c.Check(FullBar.LastResult == TriggerResult.Fired && rig.Has(hash) && Near(p.m_adrenaline, 0f),
                $"mod on again: the key answered {FullBar.LastResult}");
            CheckCleanLog(c, mark);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.clean-log (L03) ----------

    private static IEnumerator RunCleanLog()
    {
        var c = new Checks(CleanLogName);
        var problems = LogProblems(0, F13Warning);
        foreach (var problem in problems.Take(10))
        {
            c.Note("log: " + problem);
        }
        c.Check(problems.Count == 0,
            $"{problems.Count} warning or error line(s) from {ModInfo.Name} (the F13 warning of the keys test aside) or about "
            + "a missing Flash animator parameter since the game started");
        c.Report();
        yield break;
    }
}
#endif
