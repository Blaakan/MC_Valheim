#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using MC.Shared;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace MC.Crafting.StationsBatchFeedMod;

// Single-player self tests of the hover hint, settings and keys, hold, vanilla actions and the coverage dump.
internal static partial class SelfTests
{
    // ---------- batchfeed.hint (T12) ----------

    private static IEnumerator RunHint()
    {
        var c = new Checks(HintName);
        Rig rig = null;
        try
        {
            rig = new Rig();
            c.Note($"keys: Alternative placement = '{AltLabel()}', Use = '{UseLabel()}'");

            // Covered spots of T01-T11. Expected hint come from the hover text itself (Use line + keys + x5).
            foreach (var prefab in new[] { "smelter", "blastfurnace", "eitrrefinery" })
            {
                var go = rig.Spawn(prefab, 0f, 7f);
                var smelter = go != null ? go.GetComponentInChildren<Smelter>() : null;
                if (c.Check(smelter != null && smelter.m_addOreSwitch != null && smelter.m_addWoodSwitch != null, $"spawn '{prefab}' with input and fuel switches"))
                {
                    ExpectHint(c, prefab + " input slot", smelter.m_addOreSwitch.GetHoverText());
                    ExpectHint(c, prefab + " fuel slot", smelter.m_addWoodSwitch.GetHoverText());
                }
                rig.Destroy(go);
                yield return null;
            }
            foreach (var prefab in new[] { "charcoal_kiln", "windmill", "piece_spinningwheel" })
            {
                var go = rig.Spawn(prefab, 0f, 7f);
                var smelter = go != null ? go.GetComponentInChildren<Smelter>() : null;
                if (c.Check(smelter != null && smelter.m_addOreSwitch != null, $"spawn '{prefab}' with an input switch"))
                {
                    ExpectHint(c, prefab + " input slot", smelter.m_addOreSwitch.GetHoverText());
                    if (prefab == "windmill" && c.Check(smelter.m_emptyOreSwitch != null, "the windmill has an Empty switch"))
                    {
                        ExpectNoHint(c, "windmill Empty switch", smelter.m_emptyOreSwitch.GetHoverText());
                    }
                }
                rig.Destroy(go);
                yield return null;
            }
            foreach (var prefab in new[] { "piece_FrostKiln", "piece_bathtub" })
            {
                var go = rig.Spawn(prefab, 0f, 7f);
                var smelter = go != null ? go.GetComponentInChildren<Smelter>() : null;
                if (c.Check(smelter != null && smelter.m_addWoodSwitch != null, $"spawn '{prefab}' with a fuel switch"))
                {
                    ExpectHint(c, prefab + " fuel slot", smelter.m_addWoodSwitch.GetHoverText());
                }
                rig.Destroy(go);
                yield return null;
            }
            foreach (var prefab in new[] { "fire_pit", "hearth", "bonfire", "piece_brazierfloor01", "piece_walltorch" })
            {
                var go = rig.Spawn(prefab, 0f, 7f);
                var fire = go != null ? go.GetComponent<Fireplace>() : null;
                if (c.Check(fire != null, $"spawn '{prefab}' with a Fireplace"))
                {
                    if (prefab == "piece_walltorch" && ModActive(LightsGuid) && !fire.m_canRefill)
                    {
                        c.Note("piece_walltorch is a managed light of Switchable Lights here: no hint expected (see C04)");
                        ExpectNoHint(c, "Sconce managed by Switchable Lights", fire.GetHoverText());
                    }
                    else
                    {
                        ExpectHint(c, prefab, fire.GetHoverText());
                    }
                }
                rig.Destroy(go);
                yield return null;
            }

            // Cooking stations (hover on the station itself), also with a cooked piece on it.
            var cookGo = rig.Spawn("piece_cookingstation", 0f, 7f);
            var cook = cookGo != null ? cookGo.GetComponent<CookingStation>() : null;
            if (c.Check(cook != null && cook.m_conversion.Count > 0 && cook.m_conversion[0].m_to != null, "spawn 'piece_cookingstation'"))
            {
                ExpectHint(c, "piece_cookingstation", cook.GetHoverText());
                SetSlot(cook, 0, cook.m_conversion[0].m_to.name, cook.m_conversion[0].m_cookTime + 1f, 1);
                yield return new WaitForSeconds(0.35f);
                ExpectNoHint(c, "cooking station holding a cooked piece", cook.GetHoverText());
                SetSlot(cook, 0, "", 0f, 0);
                yield return new WaitForSeconds(0.35f);
                ExpectHint(c, "cooking station a quarter second after the cooked piece is gone", cook.GetHoverText());
            }
            rig.Destroy(cookGo);
            yield return null;
            var ironGo = rig.Spawn("piece_cookingstation_iron", 0f, 7f);
            var iron = ironGo != null ? ironGo.GetComponent<CookingStation>() : null;
            if (c.Check(iron != null, "spawn 'piece_cookingstation_iron'"))
            {
                ExpectHint(c, "piece_cookingstation_iron", iron.GetHoverText());
            }
            rig.Destroy(ironGo);
            yield return null;

            var ovenGo = rig.Spawn("piece_oven", 0f, 7f);
            var oven = ovenGo != null ? ovenGo.GetComponent<CookingStation>() : null;
            if (c.Check(oven != null && oven.m_addFoodSwitch != null && oven.m_addFuelSwitch != null, "spawn 'piece_oven'"))
            {
                ExpectHint(c, "piece_oven food slot", oven.m_addFoodSwitch.GetHoverText());
                ExpectHint(c, "piece_oven fuel slot", oven.m_addFuelSwitch.GetHoverText());
            }
            rig.Destroy(ovenGo);
            yield return null;

            var foundryGo = rig.Spawn("piece_FrostFoundry", 0f, 7f);
            var foundry = foundryGo != null ? foundryGo.GetComponent<CookingStation>() : null;
            if (c.Check(foundry != null && foundry.m_addFoodSwitch != null && foundry.m_addFuelSwitch != null, "spawn 'piece_FrostFoundry'"))
            {
                ExpectHint(c, "piece_FrostFoundry fuel slot", foundry.m_addFuelSwitch.GetHoverText());
                ExpectNoHint(c, "Frost Foundry cast slot", foundry.m_addFoodSwitch.GetHoverText());
            }
            rig.Destroy(foundryGo);
            yield return null;

            var shieldGo = rig.Spawn("piece_shieldgenerator", 0f, 7f);
            var shield = shieldGo != null ? shieldGo.GetComponentInChildren<ShieldGenerator>() : null;
            if (c.Check(shield != null && shield.m_addFuelSwitch != null, "spawn 'piece_shieldgenerator'"))
            {
                // Its fuel switch get its hover text when the generator wake up (Start).
                yield return ShieldReady(shield);
                ExpectHint(c, "piece_shieldgenerator fuel slot", shield.m_addFuelSwitch.GetHoverText());
                ExpectNoHint(c, "Shield Generator's own attack text", shield.GetHoverText());
            }
            rig.Destroy(shieldGo);
            yield return null;

            var turretGo = rig.Spawn("piece_turret", 0f, 7f);
            var turret = turretGo != null ? turretGo.GetComponent<Turret>() : null;
            if (c.Check(turret != null, "spawn 'piece_turret'"))
            {
                ExpectHint(c, "piece_turret", turret.GetHoverText());
            }
            rig.Destroy(turretGo);
            yield return null;

            // Things that must show no hint.
            var chestGo = rig.Spawn("piece_chest_wood", 0f, 6f);
            var chest = chestGo != null ? chestGo.GetComponent<Container>() : null;
            if (c.Check(chest != null, "spawn 'piece_chest_wood'"))
            {
                ExpectNoHint(c, "chest", chest.GetHoverText());
            }
            rig.Destroy(chestGo);

            var candleGo = rig.Spawn("Candle_resin", 0f, 6f);
            var candle = candleGo != null ? candleGo.GetComponent<Fireplace>() : null;
            if (c.Check(candle != null, "spawn 'Candle_resin'"))
            {
                ExpectNoHint(c, "Resin Candle", candle.GetHoverText());
            }
            rig.Destroy(candleGo);
            yield return null;

            GameObject bedPrefab = null;
            GameObject fermenterPrefab = null;
            foreach (var prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab == null)
                {
                    continue;
                }
                if (bedPrefab == null && prefab.GetComponent<Bed>() != null && prefab.GetComponent<Piece>() != null)
                {
                    bedPrefab = prefab;
                }
                if (fermenterPrefab == null && prefab.GetComponent<Fermenter>() != null)
                {
                    fermenterPrefab = prefab;
                }
            }
            var bedGo = rig.SpawnPrefab(bedPrefab, 0f, 6f);
            var bed = bedGo != null ? bedGo.GetComponent<Bed>() : null;
            if (c.Check(bed != null, "spawn a bed"))
            {
                c.Note($"bed prefab: {bedPrefab.name}");
                ExpectNoHint(c, "bed", bed.GetHoverText());
            }
            rig.Destroy(bedGo);
            var fermenterGo = rig.SpawnPrefab(fermenterPrefab, 4f, 6f);
            var fermenter = fermenterGo != null ? fermenterGo.GetComponent<Fermenter>() : null;
            if (c.Check(fermenter != null, "spawn a fermenter"))
            {
                c.Note($"fermenter prefab: {fermenterPrefab.name}");
                ExpectNoHint(c, "Fermenter", fermenter.GetHoverText());
            }
            rig.Destroy(fermenterGo);
            yield return null;

            var boarGo = rig.SpawnAt("Boar", rig.At(-4f, 5f) + Vector3.up * 0.5f);
            var tame = boarGo != null ? boarGo.GetComponent<Tameable>() : null;
            var boar = boarGo != null ? boarGo.GetComponent<Character>() : null;
            if (c.Check(tame != null && boar != null, "spawn a 'Boar'"))
            {
                boar.SetTamed(true);
                ExpectNoHint(c, "tame", tame.GetHoverText());
            }
            rig.Destroy(boarGo);
            yield return null;

            // ShowHint = false: no hint, the batch still works. Forced in memory.
            var go2 = rig.Spawn("smelter", 0f, 7f);
            var s2 = go2 != null ? go2.GetComponentInChildren<Smelter>() : null;
            if (c.Check(s2 != null && s2.m_addOreSwitch != null, "spawn a smelter for ShowHint"))
            {
                Plugin.TestShowHint = false;
                HoverHint.Invalidate();
                ExpectNoHint(c, "ShowHint = false", s2.m_addOreSwitch.GetHoverText());
                rig.Give("CopperOre", 5);
                var p = Press(s2.m_addOreSwitch.gameObject, alt: true);
                c.Check(p.Removed == 5 && s2.GetQueueSize() == 5, $"ShowHint = false: Shift+E still adds 5 (queue {s2.GetQueueSize()})");
                Plugin.TestShowHint = true;
                HoverHint.Invalidate();
                ExpectHint(c, "ShowHint = true again", s2.m_addOreSwitch.GetHoverText());
            }
            c.Report();
        }
        finally
        {
            rig?.End();
        }
    }

    // ---------- batchfeed.settings (T14, T15, T20, T26, T27) ----------

    private static IEnumerator RunSettings()
    {
        var c = new Checks(SettingsName);
        Rig rig = null;
        LogTap tap = null;
        var layout = ZInput.InputLayout;
        try
        {
            rig = new Rig();
            tap = new LogTap();
            HudReady(c);
            var oreToken = Token("CopperOre");
            Smelter NewSmelter(out GameObject spawned)
            {
                spawned = rig.Spawn("smelter", 0f, 7f);
                return spawned != null ? spawned.GetComponentInChildren<Smelter>() : null;
            }

            // T15: Amount = 10.
            var smelter = NewSmelter(out var go);
            if (!c.Check(smelter != null && smelter.m_addOreSwitch != null && smelter.m_maxOre == 10 && oreToken != null, "spawn a vanilla smelter (10 ore)"))
            {
                c.Report();
                yield break;
            }
            var ore = smelter.m_addOreSwitch.gameObject;
            c.Check(rig.Give("CopperOre", 30), "give 30 CopperOre");
            Plugin.TestAmount = 10;
            HoverHint.Invalidate();
            ExpectHint(c, "T15 Amount = 10", smelter.m_addOreSwitch.GetHoverText(), amount: 10);
            var p = Press(ore, alt: true);
            c.Check(p.Removed == 10 && smelter.GetQueueSize() == 10 && p.Center == Loc($"$msg_added 10 {oreToken} (10/10)"),
                $"T15 Amount = 10: one press fills the smelter with 10 (queue {smelter.GetQueueSize()}, {Show(p)})");
            rig.Destroy(go);
            yield return null;
            var refineryGo = rig.Spawn("eitrrefinery", 0f, 7f);
            var refinery = refineryGo != null ? refineryGo.GetComponentInChildren<Smelter>() : null;
            // Whatever its queue slot takes (game data of the run: Soft Tissue; Sap is its fuel). T06 checks the names.
            string input = null;
            if (refinery != null)
            {
                foreach (var conversion in refinery.m_conversion)
                {
                    if (input == null && conversion != null && conversion.m_from != null)
                    {
                        input = conversion.m_from.name;
                    }
                }
            }
            if (c.Check(refinery != null && refinery.m_addOreSwitch != null && input != null, "spawn 'eitrrefinery' with an input slot that takes an item")
                && c.Check(rig.Give(input, 20), $"give 20 {input}"))
            {
                var n = Mathf.Min(10, refinery.m_maxOre);
                p = Press(refinery.m_addOreSwitch.gameObject, alt: true);
                c.Check(p.Removed == n && refinery.GetQueueSize() == n, $"T15 Amount = 10: the Eitr Refinery ({refinery.m_maxOre}) gets {n} (queue {refinery.GetQueueSize()})");
            }
            rig.Destroy(refineryGo);
            yield return null;
            Plugin.TestAmount = 5;
            HoverHint.Invalidate();

            // T14: mod-only key. Key state forced in memory (TestKeyHeld), the real key is never read.
            rig.Empty();
            c.Check(rig.Give("CopperOre", 30), "give 30 CopperOre");
            smelter = NewSmelter(out go);
            ore = smelter.m_addOreSwitch.gameObject;
            ExpectHint(c, "T15 Amount back to 5", smelter.m_addOreSwitch.GetHoverText());
            var altDef = ZInput.instance != null ? ZInput.instance.GetButtonDef("AltPlace") : null;
            c.Note($"Alternative placement is bound to {(altDef != null ? altDef.GetActionPath() : "?")}");
            p = Press(ore, alt: false);
            c.Check(p.Removed == 1, $"T14 ModifierKey = None: a press without the game's Alternative placement key (Right Shift + E) adds 1 ({Show(p)})");
            Plugin.TestModifierKey = KeyCode.RightShift;
            Plugin.TestKeyHeld = false;
            HoverHint.Invalidate();
            var rshift = Loc("$button_rshift");
            c.Check(!string.IsNullOrEmpty(rshift) && !rshift.StartsWith("[", StringComparison.Ordinal), $"the game has a name for Right Shift ('{rshift}')");
            ExpectHint(c, "T14 ModifierKey = RightShift", smelter.m_addOreSwitch.GetHoverText(), modifier: rshift);
            p = Press(ore, alt: true);
            c.Check(p.Removed == 1 && smelter.GetQueueSize() == 2, $"T14 ModifierKey = RightShift: L-Shift + E (game key only) adds 1 ({Show(p)})");
            Plugin.TestKeyHeld = true;
            p = Press(ore, alt: false);
            c.Check(p.Removed == 5 && smelter.GetQueueSize() == 7, $"T14 ModifierKey = RightShift: R-Shift + E batches 5 (queue {smelter.GetQueueSize()}, {Show(p)})");
            Plugin.TestKeyHeld = false;

            var pit = rig.Spawn("fire_pit", 6f, 7f);
            var fire = pit != null ? pit.GetComponent<Fireplace>() : null;
            if (c.Check(fire != null && FeedTarget.FireSkip(fire) == null, "spawn a campfire"))
            {
                var wood = fire.m_fuelItem.m_itemData.m_shared.m_name;
                var max = fire.m_maxFuel;
                c.Check(rig.Give(fire.m_fuelItem.name, 10), "give 10 fuel items");
                SetFuel(fire, 2f);
                p = Press(pit, alt: true);
                c.Check(p.Removed == 1 && Near(Fuel(fire), 3f) && State(fire) == 1, $"T14 ModifierKey = RightShift: L-Shift + E on a fire adds 1 fuel (fuel {Fuel(fire)}, {Show(p)})");

                // T26: mod-only key on a full fire that can be turned off (instance flag: no vanilla fire has both).
                fire.m_canTurnOff = true;
                SetFuel(fire, max);
                Plugin.TestKeyHeld = true;
                p = Press(pit, alt: false);
                c.Check(p.Removed == 0 && State(fire) == 1 && p.Messages == 1 && p.Center == Loc(Words("$msg_cantaddmore", wood)),
                    $"T26 mod-only key on a full fire: 'can't add more', the fire stays on (state {State(fire)}, {Show(p)})");
                Plugin.TestKeyHeld = false;
                p = Press(pit, alt: false);
                c.Check(p.Removed == 0 && State(fire) == 2, $"T26 plain E still turns it off (state {State(fire)})");
                p = Press(pit, alt: false);
                c.Check(State(fire) == 1, $"T26 plain E turns it on again (state {State(fire)})");
            }
            rig.Destroy(pit);

            rig.Destroy(go);
            yield return null;

            // T27: a key the game cannot read, at a kiln. One warning, Alternative placement key works instead.
            rig.Empty();
            var kilnGo = rig.Spawn("charcoal_kiln", 0f, 7f);
            var kiln = kilnGo != null ? kilnGo.GetComponentInChildren<Smelter>() : null;
            if (c.Check(kiln != null && kiln.m_addOreSwitch != null && ConversionIndex(kiln, "Wood") >= 0 && rig.Give("Wood", 12),
                    "spawn 'charcoal_kiln' that takes Wood; give 12 Wood"))
            {
                BatchFeeder.ResetKeyCheck();
                var mark = tap.Mark();
                Plugin.TestModifierKey = KeyCode.F13;
                Plugin.TestKeyHeld = null;
                HoverHint.Invalidate();
                yield return CheckUnreadableKey(c, tap, mark, kiln);
            }
            Plugin.TestModifierKey = KeyCode.None;
            Plugin.TestKeyHeld = false;
            HoverHint.Invalidate();
            rig.Destroy(kilnGo);
            yield return null;

            // T20: controller in use (forced in memory). Hint shows the controller's buttons, the mod-only key is ignored.
            rig.Empty();
            c.Check(rig.Give("CopperOre", 20), "give 20 CopperOre");
            smelter = NewSmelter(out go);
            ore = smelter.m_addOreSwitch.gameObject;
            var loc = Localization.instance;
            var joyAlt = loc.GetBoundKeyString("JoyAltPlace", true);
            var joyUse = loc.GetBoundKeyString("JoyUse", true);
            var joyKeys = loc.GetBoundKeyString("JoyAltKeys", true);
            c.Note($"controller labels: Alternative placement '{joyAlt}', Use '{joyUse}', alt keys '{joyKeys}'");
            c.Check(joyAlt.Length > 0 && joyUse.Length > 0 && joyKeys.Length > 0 && joyAlt != AltLabel() && joyUse != UseLabel(),
                "T20 the game has controller glyphs for Alternative placement, Use and the alt-keys button");
            Plugin.TestModifierKey = KeyCode.RightShift;
            Plugin.TestKeyHeld = false;
            Plugin.TestGamepad = true;
            HoverHint.Invalidate();
            ZInput.InputLayout = InputLayout.Default;
            var key = KeyPart(HintLine(smelter.m_addOreSwitch.GetHoverText()));
            c.Check(key == joyAlt + " + " + joyUse, $"T20 default controller layout: hint keys are the Alternative placement button + the use button, got '{key}'");
            p = Press(ore, alt: true);
            c.Check(p.Removed == 5, $"T20 controller: holding the game's button and pressing use batches, the mod-only key is ignored ({Show(p)})");
            p = Press(ore, alt: false);
            c.Check(p.Removed == 1, $"T20 controller: use alone adds 1 ({Show(p)})");
            // Alternative layout: same frame, layout put back before anything else runs.
            ZInput.InputLayout = InputLayout.Alternative1;
            key = KeyPart(HintLine(smelter.m_addOreSwitch.GetHoverText()));
            ZInput.InputLayout = layout;
            c.Check(key == joyKeys + " + " + joyUse, $"T20 alternative controller layout: hint keys are the alt-keys button + the use button, got '{key}'");
            Plugin.TestGamepad = false;
            HoverHint.Invalidate();
            c.Report();
        }
        finally
        {
            ZInput.InputLayout = layout;
            tap?.Dispose();
            rig?.End();
        }
    }

    // T27 checks, shared by the single-player test (key forced in memory) and the multiplayer one (real setting).
    // Caller did BatchFeeder.ResetKeyCheck(), took the log mark, then made ModifierKey = F13.
    private static IEnumerator CheckUnreadableKey(Checks c, LogTap tap, int mark, Smelter kiln)
    {
        var sw = kiln.m_addOreSwitch;
        var hover = sw.GetHoverText();
        ExpectHint(c, "T27 ModifierKey = F13: the hint at a kiln names the Alternative placement key", hover);
        c.Check(hover.IndexOf("F13", StringComparison.Ordinal) < 0, $"T27 the hint does not name F13: '{HintLine(hover)}'");
        c.Check(!BatchFeeder.KeyUsable(KeyCode.F13), "T27 F13 counts as a key the game cannot read");
        var p = Press(sw.gameObject, alt: true);
        c.Check(p.Removed == 5 && kiln.GetQueueSize() == 5, $"T27 Shift+E still batch-feeds ({Show(p)})");
        // Half a second later (the hint line is rebuilt): still one warning only.
        yield return new WaitForSeconds(0.6f);
        ExpectHint(c, "T27 the hint half a second later", sw.GetHoverText());
        p = Press(sw.gameObject, alt: true);
        c.Check(p.Removed == 5, $"T27 a second Shift+E batch-feeds too ({Show(p)})");
        var warnings = tap.Since(mark, "General.ModifierKey = F13", LogLevel.Warning);
        var errors = tap.Since(mark, "", LogLevel.Error | LogLevel.Fatal);
        c.Check(warnings.Count == 1 && warnings[0].IndexOf("the game cannot read this key", StringComparison.Ordinal) >= 0
                && warnings[0].IndexOf("Alternative placement key", StringComparison.Ordinal) >= 0,
            $"T27 exactly one warning in the log, got {warnings.Count}{(warnings.Count > 0 ? ": " + warnings[0] : "")}");
        c.Check(errors.Count == 0, $"T27 no error in the log, got {errors.Count}{(errors.Count > 0 ? ": " + errors[0] : "")}");
    }

    // ---------- batchfeed.rebind (T13) ----------

    private sealed class Binding
    {
        internal ZInput.ButtonDef Def;
        internal string Path;      // effective path before the test
        internal bool Overridden;  // player had rebound it before the test

        internal void Restore()
        {
            if (Def == null)
            {
                return;
            }
            if (Overridden && !string.IsNullOrEmpty(Path))
            {
                Def.Rebind(Path);
            }
            else
            {
                Def.ResetBinding();
            }
        }
    }

    private static Binding Remember(string button)
    {
        var def = ZInput.instance != null ? ZInput.instance.GetButtonDef(button) : null;
        if (def == null || def.ButtonAction == null || def.ButtonAction.bindings.Count == 0)
        {
            return null;
        }
        InputBinding binding = def.ButtonAction.bindings[0];
        return new Binding { Def = def, Path = binding.effectivePath, Overridden = !string.IsNullOrEmpty(binding.overridePath) };
    }

    private static IEnumerator RunRebind()
    {
        var c = new Checks(RebindName);
        Rig rig = null;
        Binding alt = null;
        Binding use = null;
        try
        {
            rig = new Rig();
            var go = rig.Spawn("smelter", 0f, 7f);
            var smelter = go != null ? go.GetComponentInChildren<Smelter>() : null;
            alt = Remember("AltPlace");
            use = Remember("Use");
            if (!c.Check(smelter != null && smelter.m_addOreSwitch != null && alt != null && use != null, "spawn a smelter; the game has AltPlace and Use buttons"))
            {
                c.Report();
                yield break;
            }
            var oldAlt = AltLabel();
            var oldUse = UseLabel();
            ExpectHint(c, "before the rebind", smelter.m_addOreSwitch.GetHoverText());

            // What the settings screen does when the player picks a key, without saving it.
            alt.Def.Rebind("<Keyboard>/leftAlt");
            use.Def.Rebind("<Keyboard>/u");
            yield return new WaitForSeconds(0.6f);
            var newAlt = AltLabel();
            var newUse = UseLabel();
            c.Note($"Alternative placement '{oldAlt}' -> '{newAlt}', Use '{oldUse}' -> '{newUse}'");
            c.Check(newAlt != oldAlt && newUse != oldUse, "the game names the new keys differently from the old ones");
            c.Check(newUse.IndexOf("U", StringComparison.OrdinalIgnoreCase) >= 0, $"Use now reads as the U key ('{newUse}')");
            var hover = smelter.m_addOreSwitch.GetHoverText();
            c.Check(KeyPart(HintLine(hover)) == newAlt + " + " + newUse,
                $"T13 hint follows the rebind within half a second: '{HintLine(hover)}', expected keys '{newAlt} + {newUse}'");
            rig.Give("CopperOre", 10);
            var p = Press(smelter.m_addOreSwitch.gameObject, alt: true);
            var p1 = Press(smelter.m_addOreSwitch.gameObject, alt: false);
            c.Check(p.Removed == 5 && p1.Removed == 1, $"T13 with the keys rebound: Alternative placement + Use batches 5, Use alone adds 1 ({p.Removed}, {p1.Removed})");

            alt.Restore();
            use.Restore();
            var back = AltLabel() == oldAlt && UseLabel() == oldUse;
            alt = null;
            use = null;
            c.Check(back, $"bindings put back ('{AltLabel()}', '{UseLabel()}')");
            yield return new WaitForSeconds(0.6f);
            ExpectHint(c, "after the bindings are put back", smelter.m_addOreSwitch.GetHoverText());
            c.Report();
        }
        finally
        {
            alt?.Restore();
            use?.Restore();
            rig?.End();
        }
    }

    // ---------- real key state: batchfeed.realkey (T14, T26), batchfeed.rebind-keys (T13) ----------

    // Key held "for real" without a hand: a keyboard state event through the Input System, the same road a real key
    // takes, so the game's own key reads see it (ZInput.GetKey, ZInput.GetButton). No keys = all released. Works only
    // while the game window has focus: Unity switches the keyboard off in the background.
    private static void HoldKeys(params Key[] keys)
    {
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        }
    }

    // "alt" exactly as Player.Update works it out before it calls Interact.
    private static bool VanillaAlt() =>
        ZInput.IsNonClassicFunctionality() && ZInput.IsGamepadActive()
            ? ZInput.GetButton("JoyAltKeys")
            : ZInput.GetButton("AltPlace") || ZInput.GetButton("JoyAltPlace");

    private static string ButtonPath(string button)
    {
        var def = ZInput.instance != null ? ZInput.instance.GetButtonDef(button) : null;
        return def != null ? def.GetActionPath() : null;
    }

    private static IEnumerator RunRealKey()
    {
        var c = new Checks(RealKeyName);
        Rig rig = null;
        var injected = false;
        try
        {
            rig = new Rig();
            HudReady(c);
            var keyboard = Keyboard.current;
            var go = rig.Spawn("smelter", -4f, 7f);
            var pit = rig.Spawn("fire_pit", 4f, 7f);
            var smelter = go != null ? go.GetComponentInChildren<Smelter>() : null;
            var fire = pit != null ? pit.GetComponent<Fireplace>() : null;
            if (!c.Check(keyboard != null && smelter != null && smelter.m_addOreSwitch != null && fire != null && FeedTarget.FireSkip(fire) == null,
                    "a keyboard is there; spawn a smelter and a campfire"))
            {
                c.Report();
                yield break;
            }
            var ore = smelter.m_addOreSwitch.gameObject;
            var wood = fire.m_fuelItem.m_itemData.m_shared.m_name;
            c.Check(rig.Give("CopperOre", 20) && rig.Give(fire.m_fuelItem.name, 10), "give 20 CopperOre and 10 fuel items");
            var altPath = ButtonPath("AltPlace");
            c.Note($"Alternative placement is bound to {altPath}; window focused {Application.isFocused}");
            // From here the mod reads the real key.
            Plugin.TestKeyHeld = null;

            // Right Shift held.
            injected = true;
            HoldKeys(Key.RightShift);
            yield return Until(() => keyboard[Key.RightShift].isPressed, 1.5f);
            if (!c.Check(keyboard[Key.RightShift].isPressed && ZInput.GetKey(KeyCode.RightShift, false),
                    $"the test can hold Right Shift for the game (needs the game window in front; focused {Application.isFocused})"))
            {
                c.Report();
                yield break;
            }
            if (altPath != "<Keyboard>/rightShift")
            {
                c.Check(!VanillaAlt(), "T14 Right Shift is not the game's Alternative placement key");
            }
            var p = Press(ore, VanillaAlt());
            c.Check(p.Removed == 1 && smelter.GetQueueSize() == 1, $"T14 ModifierKey = None: Right Shift + E adds 1 ({Show(p)})");
            Plugin.TestModifierKey = KeyCode.RightShift;
            HoverHint.Invalidate();
            p = Press(ore, VanillaAlt());
            c.Check(p.Removed == 5 && smelter.GetQueueSize() == 6, $"T14 ModifierKey = RightShift: Right Shift + E batches 5 ({Show(p)})");

            // T26: full fire that can be turned off (instance flag), mod-only key held: adds nothing, stays on.
            fire.m_canTurnOff = true;
            SetFuel(fire, fire.m_maxFuel);
            var state = State(fire);
            p = Press(pit, VanillaAlt());
            c.Check(p.Removed == 0 && State(fire) == state && p.Messages == 1 && p.Center == Loc(Words("$msg_cantaddmore", wood)),
                $"T26 Right Shift + E on a full fire: 'can't add more', the fire stays as it was (state {state} -> {State(fire)}, {Show(p)})");

            // Released: plain E toggles it.
            HoldKeys();
            yield return Until(() => !keyboard[Key.RightShift].isPressed, 1.5f);
            c.Check(!ZInput.GetKey(KeyCode.RightShift, false), "Right Shift released");
            state = State(fire);
            p = Press(pit, VanillaAlt());
            c.Check(p.Removed == 0 && State(fire) != state, $"T26 plain E still toggles the fire (state {state} -> {State(fire)})");
            fire.m_canTurnOff = false;

            // Left Shift held: the game's own key. With ModifierKey = RightShift it adds 1 (a fire: 1 fuel).
            HoldKeys(Key.LeftShift);
            yield return Until(() => keyboard[Key.LeftShift].isPressed && (altPath != "<Keyboard>/leftShift" || VanillaAlt()), 1.5f);
            if (altPath == "<Keyboard>/leftShift")
            {
                c.Check(VanillaAlt(), "T14 Left Shift held is the game's Alternative placement");
            }
            p = Press(ore, VanillaAlt());
            c.Check(p.Removed == 1 && smelter.GetQueueSize() == 7, $"T14 ModifierKey = RightShift: L-Shift + E adds 1 ({Show(p)})");
            fire.m_nview.GetZDO().Set(ZDOVars.s_state, 1);
            SetFuel(fire, 2f);
            p = Press(pit, VanillaAlt());
            c.Check(p.Removed == 1 && Near(Fuel(fire), 3f) && State(fire) == 1, $"T14 ModifierKey = RightShift: L-Shift + E on a fire adds 1 fuel (fuel {Fuel(fire)}, {Show(p)})");
            c.Report();
        }
        finally
        {
            if (injected)
            {
                HoldKeys();
            }
            rig?.End();
        }
    }

    private static IEnumerator RunRebindKeys()
    {
        var c = new Checks(RebindKeysName);
        Rig rig = null;
        Binding alt = null;
        Binding use = null;
        var injected = false;
        try
        {
            rig = new Rig();
            HudReady(c);
            var keyboard = Keyboard.current;
            var go = rig.Spawn("smelter", 0f, 7f);
            var smelter = go != null ? go.GetComponentInChildren<Smelter>() : null;
            alt = Remember("AltPlace");
            use = Remember("Use");
            if (!c.Check(keyboard != null && smelter != null && smelter.m_addOreSwitch != null && alt != null && use != null,
                    "a keyboard is there; spawn a smelter; the game has AltPlace and Use buttons"))
            {
                c.Report();
                yield break;
            }
            var ore = smelter.m_addOreSwitch.gameObject;
            c.Check(rig.Give("CopperOre", 20), "give 20 CopperOre");

            // Alternative placement on L-Alt, Use on U (in memory, never saved).
            alt.Def.Rebind("<Keyboard>/leftAlt");
            use.Def.Rebind("<Keyboard>/u");
            yield return null;
            c.Check(ButtonPath("AltPlace") == "<Keyboard>/leftAlt" && ButtonPath("Use") == "<Keyboard>/u" && AltLabel() == Loc("$button_lalt"),
                $"T13 the game has the new bindings: Alternative placement '{AltLabel()}' ({ButtonPath("AltPlace")}), Use '{UseLabel()}' ({ButtonPath("Use")})");

            // L-Alt held: the game reads Alternative placement, so a Use press batches. (The Use key itself is not pressed
            // here: with it held the game would use whatever the camera looks at.)
            injected = true;
            HoldKeys(Key.LeftAlt);
            yield return Until(() => keyboard[Key.LeftAlt].isPressed && ZInput.GetButton("AltPlace"), 1.5f);
            if (!c.Check(keyboard[Key.LeftAlt].isPressed, $"the test can hold Left Alt for the game (needs the game window in front; focused {Application.isFocused})"))
            {
                c.Report();
                yield break;
            }
            c.Check(ZInput.GetButton("AltPlace"), "T13 with L-Alt held the game reads Alternative placement as held");
            var p = Press(ore, VanillaAlt());
            c.Check(p.Removed == 5 && smelter.GetQueueSize() == 5, $"T13 L-Alt + Use batches 5 ({Show(p)})");

            // Left Shift held instead: no longer Alternative placement, so a Use press adds 1.
            HoldKeys(Key.LeftShift);
            yield return Until(() => keyboard[Key.LeftShift].isPressed && !keyboard[Key.LeftAlt].isPressed && !ZInput.GetButton("AltPlace"), 1.5f);
            c.Check(keyboard[Key.LeftShift].isPressed && !ZInput.GetButton("AltPlace"), "T13 with Left Shift held the game no longer reads Alternative placement");
            p = Press(ore, VanillaAlt());
            c.Check(p.Removed == 1 && smelter.GetQueueSize() == 6, $"T13 Shift + Use adds 1 ({Show(p)})");

            HoldKeys();
            alt.Restore();
            use.Restore();
            alt = null;
            use = null;
            yield return null;
            c.Note($"bindings put back: Alternative placement {ButtonPath("AltPlace")}, Use {ButtonPath("Use")}");
            c.Report();
        }
        finally
        {
            if (injected)
            {
                HoldKeys();
            }
            alt?.Restore();
            use?.Restore();
            rig?.End();
        }
    }

    // ---------- batchfeed.hold (T16) ----------

    private static float MeanGap(List<float> times, float first)
    {
        if (times.Count == 0)
        {
            return 0f;
        }
        return (times[times.Count - 1] - first) / times.Count;
    }

    private static IEnumerator RunHold()
    {
        var c = new Checks(HoldName);
        Rig rig = null;
        try
        {
            rig = new Rig();
            HudReady(c);

            // Campfire with little fuel: +5 at the press, then one per hold repeat until full.
            var pit = rig.Spawn("fire_pit", 6f, 7f);
            var fire = pit != null ? pit.GetComponent<Fireplace>() : null;
            if (c.Check(fire != null && FeedTarget.FireSkip(fire) == null && fire.m_maxFuel >= 8f, "spawn a campfire that holds 8 or more"))
            {
                var token = fire.m_fuelItem.m_itemData.m_shared.m_name;
                var max = Mathf.RoundToInt(fire.m_maxFuel);
                c.Check(rig.Give(fire.m_fuelItem.name, 20), "give 20 fuel items");
                var start = max - 9 > 0 ? max - 9 : 1;
                SetFuel(fire, start);
                var t0 = Time.time;
                var p = Press(pit, alt: true);
                c.Check(p.Removed == 5 && Near(Fuel(fire), start + 5f), $"T16 campfire: +5 at the press (fuel {start} -> {Fuel(fire)})");
                var times = new List<float>();
                var above = false;
                yield return Hold(pit, true, 1.8f, times, null, _ => above |= Fuel(fire) > fire.m_maxFuel + 0.001f);
                var expected = max - (start + 5);
                var end = Fuel(fire);
                c.Check(times.Count == expected && Mathf.CeilToInt(end) == max && !above,
                    $"T16 campfire: the hold adds 1 at a time until full: {times.Count} adds (expected {expected}), fuel {end}/{max}, above max {above}");
                var paced = times.Count > 0 && times[0] - t0 >= 0.19f;
                var slowest = times.Count > 0 ? times[0] - t0 : 0f;
                for (var i = 1; i < times.Count; i++)
                {
                    paced &= times[i] - times[i - 1] >= 0.19f;
                    slowest = Mathf.Max(slowest, times[i] - times[i - 1]);
                }
                c.Check(paced && slowest <= 0.5f, $"T16 campfire: one add about every 0.2 s (mean {MeanGap(times, t0):0.00} s, slowest {slowest:0.00} s)");
                c.Check(rig.Count(token) == 20 - 5 - times.Count && Near(end - start, 5 + times.Count, 0.05f),
                    $"T16 campfire: wood dropped by exactly the fuel added ({20 - rig.Count(token)} wood, fuel +{end - start})");
            }
            rig.Destroy(pit);
            yield return null;

            // Smelter: plain hold first (the vanilla pace), then batch + hold on a new one.
            rig.Empty();
            c.Check(rig.Give("CopperOre", 40), "give 40 CopperOre");
            var goA = rig.Spawn("smelter", 0f, 7f);
            var a = goA != null ? goA.GetComponentInChildren<Smelter>() : null;
            var goB = rig.Spawn("smelter", -6f, 7f);
            var b = goB != null ? goB.GetComponentInChildren<Smelter>() : null;
            if (c.Check(a != null && b != null && a.m_addOreSwitch != null && b.m_addOreSwitch != null && a.m_maxOre == 10, "spawn two vanilla smelters"))
            {
                var repeat = a.m_addOreSwitch.m_holdRepeatInterval;
                c.Note($"smelter ore switch hold repeat interval: {repeat} s ({(repeat > 0f ? "holding E keeps adding" : "holding E adds nothing more")})");
                var tA = Time.time;
                var p = Press(a.m_addOreSwitch.gameObject, alt: false);
                var plain = new List<float>();
                yield return Hold(a.m_addOreSwitch.gameObject, false, 1.3f, plain);
                c.Check(p.Removed == 1 && a.GetQueueSize() == 1 + plain.Count, $"plain hold: 1 at the press, then {plain.Count} in 1.3 s (queue {a.GetQueueSize()})");
                c.Check(repeat > 0f ? plain.Count >= 3 : plain.Count == 0, $"plain hold matches the switch's repeat setting ({plain.Count} repeats, interval {repeat})");

                var tB = Time.time;
                p = Press(b.m_addOreSwitch.gameObject, alt: true);
                var batch = new List<float>();
                var over = false;
                yield return Hold(b.m_addOreSwitch.gameObject, true, 1.3f, batch, null, _ => over |= b.GetQueueSize() > b.m_maxOre);
                var want = Mathf.Min(plain.Count, b.m_maxOre - 5);
                c.Check(p.Removed == 5, $"T16 smelter: +5 at the press ({Show(p)})");
                c.Check(Mathf.Abs(batch.Count - want) <= 1 && b.GetQueueSize() == 5 + batch.Count,
                    $"T16 smelter: then the same pace as the plain hold: {batch.Count} adds in 1.3 s, plain hold gave {plain.Count} (queue {b.GetQueueSize()})");
                if (plain.Count >= 2 && batch.Count >= 2)
                {
                    c.Check(Mathf.Abs(MeanGap(plain, tA) - MeanGap(batch, tB)) <= 0.08f,
                        $"T16 smelter: mean time per add {MeanGap(batch, tB):0.00} s after a batch, {MeanGap(plain, tA):0.00} s plain");
                }
                // Keep holding: never above the maximum.
                yield return Hold(b.m_addOreSwitch.gameObject, true, 1.2f, batch, null, _ => over |= b.GetQueueSize() > b.m_maxOre);
                c.Check(!over && b.GetQueueSize() <= b.m_maxOre && (repeat <= 0f || b.GetQueueSize() == b.m_maxOre),
                    $"T16 smelter: never above {b.m_maxOre}/{b.m_maxOre} while holding (queue {b.GetQueueSize()}, above max {over})");
                c.Check(rig.Count(Token("CopperOre")) == 40 - a.GetQueueSize() - b.GetQueueSize(), "T16 smelter: ore gone = ore queued");
            }
            c.Report();
        }
        finally
        {
            rig?.End();
        }
    }

    // ---------- batchfeed.vanilla (T17) ----------

    private static IEnumerator RunVanilla()
    {
        var c = new Checks(VanillaName);
        Rig rig = null;
        try
        {
            rig = new Rig();
            HudReady(c);

            // Tame: Shift+E opens the rename box.
            var boarGo = rig.SpawnAt("Boar", rig.At(-4f, 5f) + Vector3.up * 0.5f);
            var boar = boarGo != null ? boarGo.GetComponent<Character>() : null;
            if (c.Check(boar != null && boarGo.GetComponent<Tameable>() != null && TextInput.instance != null, "spawn a 'Boar'"))
            {
                boar.SetTamed(true);
                c.Check(!FeedTarget.TryResolve(boarGo, out _), "a tame is not a batch target");
                Press(boarGo, alt: true);
                var open = TextInput.instance.m_panel.activeSelf;
                TextInput.instance.Hide();
                c.Check(open, "T17 Shift+E on a tame opens the rename box");
            }
            rig.Destroy(boarGo);
            yield return null;

            // Dropped item: picked up (the game ignores a drop younger than half a second).
            var woodToken = Token("Wood");
            var dropGo = rig.SpawnPrefab(ObjectDB.instance.GetItemPrefab("Wood"), 0f, 1.5f, 0.6f);
            if (c.Check(dropGo != null && dropGo.GetComponent<ItemDrop>() != null, "drop a Wood item"))
            {
                yield return new WaitForSeconds(0.8f);
                if (c.Check(dropGo != null, "the dropped Wood is still there"))
                {
                    c.Check(!FeedTarget.TryResolve(dropGo, out _), "a dropped item is not a batch target");
                    var stack = dropGo.GetComponent<ItemDrop>().m_itemData.m_stack;
                    Press(dropGo, alt: true);
                    c.Check(rig.Count(woodToken) == stack, $"T17 Shift+E on a dropped item picks it up ({rig.Count(woodToken)} Wood in the inventory, the drop held {stack})");
                    yield return null;
                    yield return null;
                    c.Check(dropGo == null, "T17 the dropped item is gone from the ground");
                    rig.Forget(dropGo);
                }
            }
            rig.Empty();

            // Chest: opens.
            var chestGo = rig.Spawn("piece_chest_wood", 4f, 4f);
            var chest = chestGo != null ? chestGo.GetComponent<Container>() : null;
            if (c.Check(chest != null && InventoryGui.instance != null, "spawn 'piece_chest_wood'"))
            {
                c.Check(!FeedTarget.TryResolve(chestGo, out _), "a chest is not a batch target");
                Press(chestGo, alt: true);
                var open = InventoryGui.instance.m_currentContainer == chest;
                InventoryGui.instance.Hide();
                c.Check(open, "T17 Shift+E on a chest opens it");
                yield return new WaitForSeconds(0.3f);
            }
            rig.Destroy(chestGo);
            yield return null;

            // Windmill's Empty switch: empties it (flour set by the owner, pressed in the same frame: the windmill's own tick would spawn it too).
            var millGo = rig.Spawn("windmill", 0f, 9f);
            var mill = millGo != null ? millGo.GetComponentInChildren<Smelter>() : null;
            if (c.Check(mill != null && mill.m_emptyOreSwitch != null && mill.m_conversion.Count > 0 && mill.m_conversion[0].m_from != null
                        && mill.m_conversion[0].m_to != null, "spawn 'windmill' with an Empty switch"))
            {
                var empty = mill.m_emptyOreSwitch.gameObject;
                c.Check(!FeedTarget.TryResolve(empty, out _), "the Empty switch is not a batch target");
                var zdo = mill.m_nview.GetZDO();
                zdo.Set(ZDOVars.s_spawnOre, mill.m_conversion[0].m_from.name);
                zdo.Set(ZDOVars.s_spawnAmount, 2);
                var p = Press(empty, alt: true);
                var flour = rig.NewDropCount(mill.m_conversion[0].m_to.name);
                c.Check(p.Removed == 0 && mill.GetProcessedQueueSize() == 0 && flour == 2,
                    $"T17 Shift+E on the windmill's Empty switch empties it (left {mill.GetProcessedQueueSize()}, {flour} {mill.m_conversion[0].m_to.name} came out)");
            }
            rig.Destroy(millGo);
            yield return null;

            // Fermenter: Shift+E is the vanilla press (it needs a roof to take an item: without one both presses say the same and take nothing).
            GameObject fermenterPrefab = null;
            foreach (var prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab != null && prefab.GetComponent<Fermenter>() != null)
                {
                    fermenterPrefab = prefab;
                    break;
                }
            }
            var fermenterGo = rig.SpawnPrefab(fermenterPrefab, 4f, 8f);
            var fermenter = fermenterGo != null ? fermenterGo.GetComponent<Fermenter>() : null;
            if (c.Check(fermenter != null && fermenter.m_conversion.Count > 0 && fermenter.m_conversion[0].m_from != null, "spawn a fermenter"))
            {
                c.Check(!FeedTarget.TryResolve(fermenterGo, out _), "a fermenter is not a batch target");
                var baseName = fermenter.m_conversion[0].m_from.name;
                c.Check(rig.Give(baseName, 3), $"give 3 {baseName}");
                var plain = Press(fermenterGo, alt: false);
                yield return null;
                if (plain.Removed == 0)
                {
                    var shift = Press(fermenterGo, alt: true);
                    c.Note($"fermenter has no roof here: it takes nothing ('{plain.Center}'); the 'exactly 1 item' part needs a roofed fermenter");
                    c.Check(shift.Removed == 0 && shift.Center == plain.Center, $"T17 Shift+E on a fermenter does what plain E does ({Show(shift)})");
                }
                else
                {
                    // It took one with plain E: a second fermenter must take exactly one with Shift+E.
                    var second = rig.SpawnPrefab(fermenterPrefab, 8f, 8f);
                    var shift = second != null ? Press(second, alt: true) : default(Pressed);
                    c.Check(plain.Removed == 1 && shift.Removed == 1, $"T17 a fermenter takes exactly 1 item with E and with Shift+E ({plain.Removed}, {shift.Removed})");
                }
            }
            rig.Destroy(fermenterGo);
            rig.Empty();
            yield return null;

            // Plain E on covered pieces still adds exactly 1.
            var smelterGo = rig.Spawn("smelter", -6f, 7f);
            var smelter = smelterGo != null ? smelterGo.GetComponentInChildren<Smelter>() : null;
            if (c.Check(smelter != null && smelter.m_addOreSwitch != null && smelter.m_addWoodSwitch != null && smelter.m_fuelItem != null, "spawn a smelter"))
            {
                rig.Give("CopperOre", 10);
                rig.Give(smelter.m_fuelItem.name, 10);
                var p = Press(smelter.m_addOreSwitch.gameObject, alt: false);
                var p2 = Press(smelter.m_addWoodSwitch.gameObject, alt: false);
                c.Check(p.Removed == 1 && smelter.GetQueueSize() == 1 && p2.Removed == 1 && Near(smelter.GetFuel(), 1f),
                    $"T17 plain E on the smelter's ore and coal slots adds exactly 1 each (queue {smelter.GetQueueSize()}, fuel {smelter.GetFuel()})");
            }
            rig.Destroy(smelterGo);
            var pit = rig.Spawn("fire_pit", 6f, 7f);
            var fire = pit != null ? pit.GetComponent<Fireplace>() : null;
            if (c.Check(fire != null && FeedTarget.FireSkip(fire) == null, "spawn a campfire"))
            {
                rig.Give(fire.m_fuelItem.name, 10);
                SetFuel(fire, 2f);
                var p = Press(pit, alt: false);
                c.Check(p.Removed == 1 && Near(Fuel(fire), 3f), $"T17 plain E on a campfire adds exactly 1 fuel (fuel {Fuel(fire)})");
            }
            c.Report();
        }
        finally
        {
            rig?.End();
        }
    }

    // ---------- batchfeed.coverage (T24) ----------

    private const string DumpPrefix = "Batch feed coverage: ";

    // Dump line of one station part of a prefab ("<prefix><prefab> <Class>..."), or null.
    private static string DumpLine(List<string> lines, string prefab, string type)
    {
        var start = DumpPrefix + prefab + " " + type;
        foreach (var line in lines)
        {
            if (line.StartsWith(start + " ", StringComparison.Ordinal))
            {
                return line;
            }
        }
        return null;
    }

    private static void ExpectDump(Checks c, List<string> lines, string prefab, string type, params string[] parts)
    {
        var line = DumpLine(lines, prefab, type);
        var ok = line != null;
        foreach (var part in parts)
        {
            ok &= line != null && line.IndexOf(part, StringComparison.Ordinal) >= 0;
        }
        c.Check(ok, $"T24 {prefab} {type} line with '{string.Join("', '", parts)}', got '{line ?? "(no line)"}'");
    }

    private static string After(string line, string mark)
    {
        if (line == null)
        {
            return "?";
        }
        var at = line.IndexOf(mark, StringComparison.Ordinal);
        if (at < 0)
        {
            return "?";
        }
        var end = line.IndexOfAny(new[] { ',', ')' }, at + mark.Length);
        return end < 0 ? line.Substring(at + mark.Length) : line.Substring(at + mark.Length, end - at - mark.Length);
    }

    private static IEnumerator RunCoverage()
    {
        var c = new Checks(CoverageName);
        LogTap tap = null;
        try
        {
            tap = new LogTap();
            var scene = ZNetScene.instance;
            if (!c.Check(scene != null && scene.m_prefabs != null, "a world is loaded"))
            {
                c.Report();
                yield break;
            }
            // The dump of the world load itself: the session listener counted its last line (Debug lines reach listeners
            // at any log level). Nobody else ran a dump before this test.
            c.Check(SessionLog.Dumps >= 1, $"T24 loading the world wrote the dump by itself ({SessionLog.Dumps} dump(s) seen since the mod started; -1 = listener off)");
            // The real dump again, for the loaded world: this test reads its lines.
            var mark = tap.Mark();
            CoverageDump.ForgetScene();
            CoverageDump.Run(scene);
            yield return null;
            var lines = tap.Since(mark, DumpPrefix, LogLevel.Debug);
            var errors = tap.Since(mark, "", LogLevel.Error | LogLevel.Fatal);
            c.Check(errors.Count == 0, $"T24 no error while listing, got {errors.Count}{(errors.Count > 0 ? ": " + errors[0] : "")}");
            if (!c.Check(lines.Count > 1, $"T24 the dump wrote lines ({lines.Count})"))
            {
                c.Report();
                yield break;
            }
            var last = lines[lines.Count - 1];
            var failed = 0;
            foreach (var line in lines)
            {
                if (line.IndexOf(" could not be listed: ", StringComparison.Ordinal) >= 0)
                {
                    failed++;
                    c.Note("prefab not listed: " + line);
                }
            }
            // A prefab that failed half-way may have printed lines that are not counted: exact count only without failures.
            var tail = $" station part(s) listed from {scene.m_prefabs.Count} prefabs (pieces other mods add later are not listed here but are still covered).";
            var counted = -1;
            if (last.StartsWith(DumpPrefix, StringComparison.Ordinal) && last.EndsWith(tail, StringComparison.Ordinal))
            {
                int.TryParse(last.Substring(DumpPrefix.Length, last.Length - DumpPrefix.Length - tail.Length), out counted);
            }
            var listed = lines.Count - 1 - failed;
            c.Check(counted > 0 && (failed == 0 ? counted == listed : counted <= listed),
                $"T24 the dump ends with the count line ({listed} part lines, {scene.m_prefabs.Count} prefabs): '{last}'");

            foreach (var prefab in new[] { "smelter", "blastfurnace", "eitrrefinery" })
            {
                ExpectDump(c, lines, prefab, "Smelter", "input covered", "fuel covered");
            }
            foreach (var prefab in new[] { "charcoal_kiln", "windmill", "piece_spinningwheel" })
            {
                ExpectDump(c, lines, prefab, "Smelter", "input covered", "fuel skipped (no fuel switch)");
            }
            ExpectDump(c, lines, "piece_FrostKiln", "Smelter", "input skipped (no input switch)", "fuel covered (maxFuel=25, fuel=");
            c.Note("Frigid Kiln fuel item: " + After(DumpLine(lines, "piece_FrostKiln", "Smelter"), "fuel covered (maxFuel=25, fuel="));
            ExpectDump(c, lines, "piece_bathtub", "Smelter", "input skipped (no input switch)", "fuel covered (maxFuel=10, fuel=Wood)");
            foreach (var prefab in new[] { "fire_pit", "hearth", "bonfire", "piece_brazierfloor01", "piece_walltorch" })
            {
                ExpectDump(c, lines, prefab, "Fireplace", ": covered (maxFuel=");
            }
            ExpectDump(c, lines, "Candle_resin", "Fireplace", ": skipped (not refillable)");
            ExpectDump(c, lines, "piece_cookingstation", "CookingStation", "food covered via station (slots=2,");
            ExpectDump(c, lines, "piece_cookingstation_iron", "CookingStation", "food covered via station (slots=5,");
            ExpectDump(c, lines, "piece_oven", "CookingStation", "food covered via switch (slots=4,", "fuel covered (maxFuel=10, fuel=Wood,");
            ExpectDump(c, lines, "piece_FrostFoundry", "CookingStation", "food skipped (capacity 1 slot(s)) via switch", "fuel covered (maxFuel=20, fuel=FrozenFuel,");
            ExpectDump(c, lines, "piece_shieldgenerator", "ShieldGenerator", "fuel covered");
            ExpectDump(c, lines, "piece_turret", "Turret", "ammo covered");

            // Every fire of the game data: the line says what its flags say. Notes = what a tester would write down.
            var infinite = new List<string>();
            var capacityOne = new List<string>();
            var both = new List<string>();
            var covered = new List<string>();
            var wrong = new List<string>();
            foreach (var prefab in scene.m_prefabs)
            {
                if (prefab == null)
                {
                    continue;
                }
                foreach (var fire in prefab.GetComponentsInChildren<Fireplace>(true))
                {
                    var line = DumpLine(lines, prefab.name, "Fireplace");
                    string status;
                    if (!fire.m_canRefill)
                    {
                        status = ": skipped (not refillable)";
                    }
                    else if (fire.m_infiniteFuel)
                    {
                        status = ": skipped (infinite fuel)";
                        infinite.Add(prefab.name);
                    }
                    else if (fire.m_fuelItem == null)
                    {
                        status = ": skipped (no fuel item)";
                    }
                    else if (fire.m_maxFuel <= 1f)
                    {
                        status = ": skipped (capacity " + fire.m_maxFuel + ")";
                        capacityOne.Add(prefab.name);
                    }
                    else
                    {
                        status = ": covered (";
                        covered.Add(prefab.name);
                    }
                    if (fire.m_canRefill && fire.m_canTurnOff && !fire.m_infiniteFuel)
                    {
                        both.Add(prefab.name);
                    }
                    // A prefab with several fires has several lines; one of them must carry this status.
                    var found = false;
                    foreach (var l in lines)
                    {
                        found |= l.StartsWith(DumpPrefix + prefab.name + " Fireplace", StringComparison.Ordinal) && l.IndexOf(status, StringComparison.Ordinal) >= 0;
                    }
                    if (!found)
                    {
                        wrong.Add($"{prefab.name} (expected '{status}', line '{line ?? "(none)"}')");
                    }
                }
            }
            c.Check(wrong.Count == 0, $"T24 every Fireplace line matches the prefab's flags: {(wrong.Count == 0 ? "ok" : string.Join("; ", wrong.GetRange(0, Mathf.Min(5, wrong.Count)).ToArray()))}");
            c.Note($"fires covered ({covered.Count}): {string.Join(", ", covered.ToArray())}");
            c.Note($"fires skipped for infinite fuel: {(infinite.Count == 0 ? "none" : string.Join(", ", infinite.ToArray()))}");
            c.Note($"lights skipped for capacity 1: {(capacityOne.Count == 0 ? "none" : string.Join(", ", capacityOne.ToArray()))}");
            c.Note($"fires with canRefill=True canTurnOff=True infiniteFuel=False (T08, T26): {(both.Count == 0 ? "none" : string.Join(", ", both.ToArray()))}");
            c.Note("Stone Oven items: " + After(DumpLine(lines, "piece_oven", "CookingStation"), "items=").Replace(">", " to "));
            var foundry = DumpLine(lines, "piece_FrostFoundry", "CookingStation");
            if (foundry != null)
            {
                var at = foundry.IndexOf("items=", StringComparison.Ordinal);
                var end = foundry.IndexOf(", requireFire=", StringComparison.Ordinal);
                if (at >= 0 && end > at)
                {
                    var items = foundry.Substring(at + 6, end - at - 6);
                    c.Note("Frost Foundry items: " + (items.Length > 400 ? items.Substring(0, 400) + "..." : items));
                }
            }
            c.Report();
        }
        finally
        {
            tap?.Dispose();
        }
    }
}
#endif
