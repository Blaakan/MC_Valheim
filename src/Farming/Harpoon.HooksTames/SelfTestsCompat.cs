#if DEBUG
using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Farming.HarpoonHooksTamesMod;

// Debug build only. Self tests of the compatibility items C01-C05. Two MC mods are tested for real when they are
// active (Crossbow Stays Loaded, Creature Kill and Tame Counts). The three foreign mods are not here: me play what
// their code do to a harpoon hit (design doc, table "other mods") with small test-owned Harmony patches, on for one
// test only, removed one method at a time (never UnpatchSelf: the real mod may be installed under that id).
internal static partial class SelfTests
{
    private const string CrossbowGuid = "MC.Combat.Crossbow.StaysLoaded";
    private const string CountsGuid = "MC.Exploration.Stats.PerCreature";
    private const string EpicLootId = "randyknapp.mods.epicloot";
    private const string SimId = ModInfo.Guid + ".selftest";

    // ---------- stand-ins for other mods ----------

    private static bool _simStrip;        // HarpoonExtended way: hook hash off the projectile while it hits a creature
    private static Character _simTarget;  // creature the Character.Damage stand-ins act on
    private static Character _simOther;   // second creature an on-hit effect also hits
    private static bool _simInProc;
    private static bool _simScopeSeen;
    private static int _simProcs;

    private static MethodInfo Sim(string name) => AccessTools.Method(typeof(SelfTests), name);

    private static void SimReset()
    {
        _simStrip = false;
        _simTarget = null;
        _simOther = null;
        _simInProc = false;
        _simScopeSeen = false;
        _simProcs = 0;
    }

    // HarpoonExtended (creature pulling on): Projectile.OnHit prefix take the status effect hash off for any creature
    // that is not a player, finalizer put it back.
    private static void SimStrip_Prefix(Projectile __instance, Collider collider, out int __state)
    {
        __state = 0;
        if (!_simStrip || collider == null)
        {
            return;
        }
        var go = Projectile.FindHitObject(collider);
        var character = go != null ? go.GetComponent<Character>() : null;
        if (character == null || character.IsPlayer())
        {
            return;
        }
        __state = __instance.m_statusEffectHash;
        __instance.m_statusEffectHash = 0;
    }

    private static void SimStrip_Finalizer(Projectile __instance, int __state)
    {
        if (__state != 0)
        {
            __instance.m_statusEffectHash = __state;
        }
    }

    // ValheimPlus (immortal tames): Character.Damage prefix, default priority, swap the hit for an empty one.
    private static void SimImmortal_Prefix(Character __instance, ref HitData hit)
    {
        if (ReferenceEquals(__instance, _simTarget))
        {
            hit = new HitData();
        }
    }

    // EpicLoot-like enchantment: Character.Damage prefix that add lightning damage to the hit.
    private static void SimEnchant_Prefix(Character __instance, HitData hit)
    {
        if (ReferenceEquals(__instance, _simTarget) && hit != null && hit.m_statusEffectHash != 0)
        {
            hit.m_damage.m_lightning += 20f;
        }
    }

    // EpicLoot-like on-hit effect: Character.Damage postfix that hit the same creature again (and one more) right
    // away, inside the first call.
    private static void SimProc_Postfix(Character __instance, HitData hit)
    {
        if (_simInProc || !ReferenceEquals(__instance, _simTarget) || hit == null || hit.m_statusEffectHash == 0)
        {
            return;
        }
        _simInProc = true;
        try
        {
            _simProcs++;
            _simScopeSeen = HitScope.DebugIsOpen;
            __instance.Damage(ProcHit(__instance));
            if (_simOther != null)
            {
                _simOther.Damage(ProcHit(_simOther));
            }
        }
        finally
        {
            _simInProc = false;
        }
    }

    // Small extra hit from the local player, no hook on it (lightning proc).
    private static HitData ProcHit(Character target)
    {
        var hit = new HitData();
        hit.m_damage.m_lightning = 3f;
        hit.m_point = target.GetCenterPoint();
        hit.m_dir = Vector3.forward;
        hit.SetAttacker(Player.m_localPlayer);
        return hit;
    }

    // ---------- harpoon.compat.crossbow: C01 ----------

    private static IEnumerator RunCrossbow()
    {
        var c = new Checks(CrossbowName);
        var rig = Rig.Create(CrossbowName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        try
        {
            var h = Harpoon.Resolve(c);
            var other = FeatureRegistry.Find(CrossbowGuid);
            if (h == null || !c.Check(other != null && other.Value.IsActive,
                    $"Crossbow Stays Loaded is not installed or not active ({(other != null ? other.Value.State : "not installed")}): this pair cannot be tested"))
            {
                c.Report();
                yield break;
            }
            var p = rig.P;
            rig.KeepSkill(Skills.SkillType.Spears);
            rig.KeepSkill(Skills.SkillType.Crossbows);
            yield return rig.Stage(11f, 3f, c);
            var crossbow = rig.Give("CrossbowArbalest");
            var weapon = rig.Give(Harpoon.Prefab);
            var spot = rig.At(7f);
            var boar = rig.Spawn("Boar", spot, true);
            if (!c.Check(crossbow != null && weapon != null && boar != null, "could not give CrossbowArbalest and the harpoon, or spawn a Boar"))
            {
                c.Report();
                yield break;
            }
            Action hold = () => Place(boar, spot, -rig.Dir);
            var box = new Box();

            // Crossbow in hand: the game loads it by itself (the normal reload).
            rig.EmptyHands();
            c.Check(p.EquipItem(crossbow, false), "could not equip the crossbow");
            rig.TakeControls();
            var loading = crossbow.GetWeaponLoadingTime();
            yield return Until(() => p.IsWeaponLoaded() && ReferenceEquals(p.m_weaponLoaded, crossbow), loading + 8f, box, hold);
            if (!c.Check(box.Ok, $"setup: the crossbow did not load within {F(loading + 8f)} s (its reload takes {F(loading)} s)"))
            {
                c.Report();
                yield break;
            }
            c.Note($"crossbow loaded after {F(box.Seconds)} s (reload time {F(loading)} s)");

            // Switch to the harpoon, hook the tame with a real throw (Attack.OnAttackTrigger: both mods run there).
            c.Check(p.EquipItem(weapon, false), "could not switch to the harpoon");
            yield return Wait(0.6f, hold);
            c.Check(ReferenceEquals(p.GetCurrentWeapon(), weapon), "the harpoon is in hand");
            var before = Snap.Take(boar);
            ClearCenter();
            var r = new ThrowResult();
            yield return RealThrow(rig, h, weapon, boar, spot, r);
            c.Note($"real throw: {r.Attempts} throw(s), hooked {r.Hooked}; {r.Detail}");
            if (c.Check(r.Hooked, $"C01: the harpoon throw hooks the tame within 5 throws ({r.Detail})"))
            {
                CheckHarmlessHook(c, "C01", rig, h, boar, before,
                    new Shot { Valid = true, Stopped = true, RaiseSkill = r.RaiseSkill, Adrenaline = r.Adrenaline, Push = r.Push });
            }

            // Back to the crossbow: still loaded, no reload.
            yield return Until(() => !p.InAttack(), 3f, box, hold);
            yield return Wait(0.3f, hold);
            c.Check(p.EquipItem(crossbow, false), "could not switch back to the crossbow");
            var limit = Mathf.Clamp(loading * 0.5f, 0.5f, 1.5f);
            yield return Until(() => p.IsWeaponLoaded() && ReferenceEquals(p.m_weaponLoaded, crossbow), limit, box, hold);
            c.Check(box.Ok, $"C01: back in hand the crossbow is still loaded, without a reload (loaded {box.Ok} after {F(box.Seconds)} s; a reload takes {F(loading)} s)");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- harpoon.compat.counts: C02 ----------

    // The line of one creature in the Creatures block that Creature Kill and Tame Counts puts on top of the Player
    // Statistics text, built by the game's own TextsDialog.AddStats (what opening the Compendium does). The entry
    // me add to the dialog's list for that is taken out again. False = no Creatures block at all.
    private static bool ReadCreatureLine(string shownName, out int kills, out int tames, out string line)
    {
        kills = 0;
        tames = 0;
        line = "(no line)";
        var gui = InventoryGui.instance;
        var dialog = gui != null ? gui.m_textsDialog : null;
        if (dialog == null)
        {
            return false;
        }
        var count = dialog.m_texts.Count;
        string text = null;
        try
        {
            dialog.AddStats();
            var topic = Loc("$inventory_stats");
            for (var i = dialog.m_texts.Count - 1; i >= count; i--)
            {
                if (text == null && dialog.m_texts[i].m_topic == topic)
                {
                    text = dialog.m_texts[i].m_text;
                }
            }
        }
        finally
        {
            while (dialog.m_texts.Count > count)
            {
                dialog.m_texts.RemoveAt(dialog.m_texts.Count - 1);
            }
        }
        if (text == null || text.IndexOf("<color=orange>Creatures</color>", StringComparison.Ordinal) < 0)
        {
            return false;
        }
        var end = text.IndexOf("Difficulty Category", StringComparison.Ordinal);
        var block = end > 0 ? text.Substring(0, end) : text;
        foreach (var raw in block.Split('\n'))
        {
            var row = raw.Trim();
            if (!row.StartsWith(shownName + ": ", StringComparison.Ordinal))
            {
                continue;
            }
            line = row;
            var killed = Regex.Match(row, @"(\d+) killed");
            var tamed = Regex.Match(row, @"(\d+) tamed");
            kills = killed.Success ? int.Parse(killed.Groups[1].Value) : 0;
            tames = tamed.Success ? int.Parse(tamed.Groups[1].Value) : 0;
            break;
        }
        return true;
    }

    private static IEnumerator RunCounts()
    {
        var c = new Checks(CountsName);
        var rig = Rig.Create(CountsName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        try
        {
            var h = Harpoon.Resolve(c);
            var other = FeatureRegistry.Find(CountsGuid);
            if (h == null || !c.Check(other != null && other.Value.IsActive,
                    $"Creature Kill and Tame Counts is not installed or not active ({(other != null ? other.Value.State : "not installed")}): this pair cannot be tested"))
            {
                c.Report();
                yield break;
            }
            var p = rig.P;
            yield return rig.Stage(10f, 3f, c);
            var boar = rig.Spawn("Boar", rig.At(8f), true);
            if (!c.Check(boar != null, "could not spawn a Boar"))
            {
                c.Report();
                yield break;
            }
            yield return FixedFor(0.6f);
            var shown = Loc(boar.m_name);
            if (!c.Check(ReadCreatureLine(shown, out var kills, out var tames, out var line),
                    "the Player Statistics text has no Creatures block (Creature Kill and Tame Counts did not add it)"))
            {
                c.Report();
                yield break;
            }
            c.Note($"Creatures block before, {shown} line: '{line}' (killed {kills}, tamed {tames})");
            var tamedStat = Stat(PlayerStatType.CreatureTamed);
            var box = new Box();

            // Fresh tame hooked, then killed by a wild wolf: the line does not move.
            DirectHit(rig, h, boar);
            c.Check(HookOn(boar, h) != null, "the fresh tame is hooked");
            ReadCreatureLine(shown, out var killsNow, out var tamesNow, out line);
            c.Check(killsNow == kills && tamesNow == tames, $"C02: hooking changes neither count ('{line}')");
            Release(boar, h);
            var wolf = rig.Spawn("Wolf", rig.At(8f, 2.5f), false);
            yield return FixedFor(0.3f);
            Kill(boar, wolf);
            yield return Until(() => boar == null, 4f, box);
            yield return FixedFor(0.3f);
            rig.Destroy(wolf);
            ReadCreatureLine(shown, out killsNow, out tamesNow, out line);
            c.Check(box.Ok && killsNow == kills, $"C02: the hooked tame killed by a wild wolf does not change the {shown} kill count ({kills} -> {killsNow}, '{line}')");
            c.Check(tamesNow == tames && Near(Stat(PlayerStatType.CreatureTamed), tamedStat),
                $"C02: the {shown} tamed count did not change ({tames} -> {tamesNow}; the game's own tame statistic {F(tamedStat)} -> {F(Stat(PlayerStatType.CreatureTamed))})");

            // Another tame hooked, then killed with the player's knife hit: +1 killed, once.
            var second = rig.Spawn("Boar", rig.At(8f), true);
            if (c.Check(second != null, "could not spawn the second Boar"))
            {
                yield return FixedFor(0.6f);
                DirectHit(rig, h, second);
                c.Check(HookOn(second, h) != null, "the second tame is hooked");
                Release(second, h);
                Kill(second, p, Skills.SkillType.Knives);
                yield return Until(() => second == null, 4f, box);
                yield return FixedFor(0.3f);
                ReadCreatureLine(shown, out killsNow, out tamesNow, out line);
                c.Check(box.Ok && killsNow == kills + 1 && tamesNow == tames,
                    $"C02: the hooked tame killed with the knife counts once: {shown} killed {kills} -> {killsNow}, tamed {tames} -> {tamesNow} ('{line}')");
            }

            // Hand-off (stand-in: the game that runs the tame has no mod, so the vanilla attacker mark stays): when
            // that tame dies later, the kill shows on the line (documented).
            var third = rig.Spawn("Boar", rig.At(8f), true);
            if (c.Check(third != null, "could not spawn the third Boar"))
            {
                yield return FixedFor(0.6f);
                ReadCreatureLine(shown, out kills, out tames, out line);
                TestSwitches.OwnerSideOff = true;
                DirectHit(rig, h, third);
                TestSwitches.OwnerSideOff = false;
                c.Check(HookOn(third, h) != null, "the third tame is hooked (by a game without the mod on the owner's side)");
                Release(third, h);
                Kill(third, null);
                yield return Until(() => third == null, 4f, box);
                yield return FixedFor(0.3f);
                ReadCreatureLine(shown, out killsNow, out tamesNow, out line);
                c.Check(box.Ok && killsNow == kills + 1 && tamesNow == tames,
                    $"C02 hand-off: hooked on a game without the mod, its later death shows as +1 on the {shown} line ({kills} -> {killsNow}, '{line}')");
            }
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- harpoon.compat.nohook: C03 (HarpoonExtended way) ----------

    private static IEnumerator RunNoHook()
    {
        var c = new Checks(NoHookName);
        var rig = Rig.Create(NoHookName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        var sim = new Harmony(SimId);
        var original = AccessTools.Method(typeof(Projectile), nameof(Projectile.OnHit));
        var patched = false;
        try
        {
            var h = Harpoon.Resolve(c);
            if (h == null)
            {
                c.Report();
                yield break;
            }
            var p = rig.P;
            rig.KeepSkill(Skills.SkillType.Spears);
            yield return rig.Stage(10f, 3f, c);
            var tame = rig.Spawn("Boar", rig.At(8f), true);
            if (!c.Check(tame != null, "could not spawn a Boar"))
            {
                c.Report();
                yield break;
            }
            yield return FixedFor(0.6f);
            HarpoonEffect.Clear(); // like a fresh activation: the once-only log line may come again
            SimReset();
            sim.Patch(original, prefix: new HarmonyMethod(Sim(nameof(SimStrip_Prefix))), finalizer: new HarmonyMethod(Sim(nameof(SimStrip_Finalizer))));
            patched = true;
            _simStrip = true;
            c.Note("stand-in for HarpoonExtended with creature pulling: a Projectile.OnHit prefix takes the hook effect off the projectile for creatures, a finalizer puts it back");

            // PvP off: not hooked, one Info line for two such hits.
            var mark = Tap.Mark();
            var before = Snap.Take(tame);
            ClearCenter();
            var first = DirectHit(rig, h, tame, false);
            var second = DirectHit(rig, h, tame, false);
            c.Check(!first.Stopped && !second.Stopped && HookOn(tame, h) == null && Untouched(tame, before) && Center() == "",
                $"C03 PvP off: the tame is not hooked, the harpoon passes through ({Describe(tame, before)}, message '{Center()}')");
            c.Check(Tap.CountSince(mark, "A harpoon hit a tame without its hook effect") == 1,
                $"C03: one Info line 'A harpoon hit a tame without its hook effect ...' for two such throws (seen {Tap.CountSince(mark, "A harpoon hit a tame without its hook effect")})");

            // PvP on: vanilla hit without our hook: damaged.
            p.SetPVP(true);
            yield return null;
            before = Snap.Take(tame);
            var third = DirectHit(rig, h, tame, false);
            p.SetPVP(false);
            c.Check(third.Stopped && tame != null && tame.GetHealth() < before.Health - 0.01f && HookOn(tame, h) == null,
                $"C03 PvP on: the tame is hit and damaged as in vanilla, without this mod's hook ({(tame != null ? Describe(tame, before) : "dead")})");
            c.Check(Tap.CountSince(mark, "Hooked tame") == 0 && Tap.CountSince(mark, "A harpoon hit a tame without its hook effect") == 1 && !HitScope.DebugIsOpen,
                "C03: this mod stood aside (no 'Hooked tame' line, still one Info line, no protection scope left open)");
            c.Report();
        }
        finally
        {
            _simStrip = false;
            if (patched)
            {
                sim.Unpatch(original, Sim(nameof(SimStrip_Prefix)));
                sim.Unpatch(original, Sim(nameof(SimStrip_Finalizer)));
            }
            SimReset();
            HarpoonEffect.Clear();
            rig.Done();
        }
    }

    // ---------- harpoon.compat.immortal: C04 (ValheimPlus way) ----------

    private static IEnumerator RunImmortal()
    {
        var c = new Checks(ImmortalName);
        var rig = Rig.Create(ImmortalName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        var sim = new Harmony(SimId);
        var original = AccessTools.Method(typeof(Character), nameof(Character.Damage));
        var patched = false;
        try
        {
            var h = Harpoon.Resolve(c);
            if (h == null)
            {
                c.Report();
                yield break;
            }
            yield return rig.Stage(10f, 3f, c);
            var tame = rig.Spawn("Boar", rig.At(8f), true);
            if (!c.Check(tame != null, "could not spawn a Boar"))
            {
                c.Report();
                yield break;
            }
            yield return FixedFor(0.6f);
            SimReset();
            sim.Patch(original, prefix: new HarmonyMethod(Sim(nameof(SimImmortal_Prefix))));
            patched = true;
            _simTarget = tame;
            c.Note("stand-in for ValheimPlus immortal tames: a default-priority Character.Damage prefix swaps the hit for an empty one");

            var hits = Stat(PlayerStatType.EnemyHits);
            var mark = Tap.Mark();
            var before = Snap.Take(tame);
            ClearCenter();
            var shot = DirectHit(rig, h, tame);
            c.Check(shot.Valid && shot.Stopped, "C04: the harpoon still stops on the tame (this mod lets it hit)");
            c.Check(HookOn(tame, h) == null && Center() == "", $"C04: not hooked: no hook effect, no 'harpooned' message (message '{Center()}')");
            c.Check(Untouched(tame, before) && Near(tame.GetHealth(), tame.GetMaxHealth()), $"C04: no damage ({Describe(tame, before)})");
            c.Check(Tap.CountSince(mark, "Hooked tame") == 0 && !HitScope.DebugIsOpen && Near(Stat(PlayerStatType.EnemyHits), hits),
                "C04: this mod's damage patch ran after the other one and stood aside (no 'Hooked tame' line, no protection scope left open)");
            yield return FixedFor(0.3f);
            c.Check(HookOn(tame, h) == null && Near(tame.GetHealth(), tame.GetMaxHealth()), "C04: still nothing a moment later");
            c.Report();
        }
        finally
        {
            if (patched)
            {
                sim.Unpatch(original, Sim(nameof(SimImmortal_Prefix)));
            }
            SimReset();
            rig.Done();
        }
    }

    // ---------- harpoon.compat.procs: C05, on-hit effects (EpicLoot way) ----------

    private static IEnumerator RunProcs()
    {
        var c = new Checks(ProcsName);
        var rig = Rig.Create(ProcsName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        var sim = new Harmony(SimId);
        var original = AccessTools.Method(typeof(Character), nameof(Character.Damage));
        var patched = false;
        try
        {
            var h = Harpoon.Resolve(c);
            if (h == null)
            {
                c.Report();
                yield break;
            }
            yield return rig.Stage(10f, 3f, c);
            var tame = rig.Spawn("Boar", rig.At(8f), true);
            var wild = rig.Spawn("Boar", rig.At(8f, 4f), false);
            if (!c.Check(tame != null && wild != null, "could not spawn the Boars"))
            {
                c.Report();
                yield break;
            }
            Tough(wild);
            if (wild.GetBaseAI() != null)
            {
                wild.GetBaseAI().enabled = false;
            }
            yield return FixedFor(0.6f);
            SimReset();
            sim.Patch(original, postfix: new HarmonyMethod(Sim(nameof(SimProc_Postfix))));
            patched = true;
            _simTarget = tame;
            _simOther = wild;
            c.Note("stand-in for an EpicLoot on-hit effect: a Character.Damage postfix hits the hooked tame again with 3 lightning damage, and a wild boar too, inside the harpoon hit");

            var mark = Tap.Mark();
            var before = Snap.Take(tame);
            var wildBefore = Snap.Take(wild);
            ClearCenter();
            var shot = DirectHit(rig, h, tame);
            c.Check(_simProcs == 1 && _simScopeSeen, $"setup: the on-hit effect fired once, inside the protected hit (fired {_simProcs}, protection open {_simScopeSeen})");
            CheckHarmlessHook(c, "C05 hook with an on-hit effect", rig, h, tame, before, shot);
            c.Check(!tame.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectLightning), "C05: no lightning effect on the tame");
            c.Check(Tap.CountSince(mark, $"Blocked an extra hit on hooked tame {tame.m_name} during the harpoon hit.") == 1,
                $"C05: one Debug line 'Blocked an extra hit on hooked tame ...' (seen {Tap.CountSince(mark, "Blocked an extra hit")})");
            c.Check(wild.GetHealth() < wildBefore.Health - 0.01f, $"C05: the same effect on another creature is not stopped ({Describe(wild, wildBefore)})");
            c.Check(!HitScope.DebugIsOpen, "C05: the protection ends with the harpoon hit");

            // Documented limit: an effect that strikes later is not stopped.
            _simTarget = null;
            before = Snap.Take(tame);
            tame.Damage(ProcHit(tame));
            yield return FixedFor(0.1f);
            c.Check(tame == null || tame.GetHealth() < before.Health - 0.01f,
                "C05: an effect that strikes after the harpoon hit does hurt the tame (documented limit; shows the protection does not linger)");
            c.Report();
        }
        finally
        {
            if (patched)
            {
                sim.Unpatch(original, Sim(nameof(SimProc_Postfix)));
            }
            SimReset();
            rig.Done();
        }
    }

    // ---------- harpoon.compat.order: C05, damage added by an EpicLoot prefix ----------

    private static IEnumerator RunOrder()
    {
        var c = new Checks(OrderName);
        var rig = Rig.Create(OrderName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        // Patch owned by EpicLoot's Harmony id, lowest priority: the latest an EpicLoot prefix can run. The mod's own
        // prefix asks to run after that id (HarmonyAfter).
        var sim = new Harmony(EpicLootId);
        var original = AccessTools.Method(typeof(Character), nameof(Character.Damage));
        var enchant = Sim(nameof(SimEnchant_Prefix));
        var patched = false;
        try
        {
            var h = Harpoon.Resolve(c);
            if (h == null)
            {
                c.Report();
                yield break;
            }
            rig.KeepSkill(Skills.SkillType.Spears);
            yield return rig.Stage(10f, 3f, c);
            var tame = rig.Spawn("Boar", rig.At(8f), true);
            var wild = rig.Spawn("Boar", rig.At(8f, 4f), false);
            if (!c.Check(tame != null && wild != null, "could not spawn the Boars"))
            {
                c.Report();
                yield break;
            }
            Tough(wild);
            if (wild.GetBaseAI() != null)
            {
                wild.GetBaseAI().enabled = false;
            }
            yield return FixedFor(0.6f);
            SimReset();
            sim.Patch(original, prefix: new HarmonyMethod(enchant) { priority = Priority.Last });
            patched = true;
            c.Note($"stand-in for an EpicLoot enchantment: a Character.Damage prefix under the id '{EpicLootId}', lowest priority, adds 20 lightning damage to a harpoon hit");

            // Control first: on a wild boar the added damage arrives (the stand-in works).
            _simTarget = wild;
            var wildBefore = Snap.Take(wild);
            DirectHit(rig, h, wild);
            c.Check(wild.GetHealth() < wildBefore.Health - 15f, $"control: on a wild boar the harpoon hit carries the added 20 lightning ({Describe(wild, wildBefore)})");

            // On a tame: zeroed with the rest.
            _simTarget = tame;
            var before = Snap.Take(tame);
            ClearCenter();
            var shot = DirectHit(rig, h, tame);
            CheckHarmlessHook(c, "C05 enchanted harpoon", rig, h, tame, before, shot);
            c.Check(!tame.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectLightning), "C05: the damage added by the EpicLoot-named prefix did not reach the tame (no lightning effect)");
            yield return FixedFor(0.3f);
            c.Check(tame != null && Near(tame.GetHealth(), tame.GetMaxHealth()), "C05: still at full health a moment later");
            c.Report();
        }
        finally
        {
            if (patched)
            {
                sim.Unpatch(original, enchant);
            }
            SimReset();
            rig.Done();
        }
    }
}
#endif
