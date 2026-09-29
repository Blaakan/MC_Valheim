using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;
#endif

namespace MC.Building.LightsSwitchableMod;

// Debug build only (calls vanish in Release). In-world self tests, run by world probe (tools/Test-InWorld.ps1):
//   lights.torch    standing wood torch in front of player: flag, top-up, E off, E on, hold E, no resin used, no burn
//   lights.candle   resin candle: E off = vanilla state 2, E on = state 1, burnt-out candle light again
//   lights.campfire fire pit stay vanilla: E add wood, fuel burn, hover show fuel; cooking fires never lights (even
//                   listed); server rules on the wire; join check verdicts; foreign flag kept
// Me destroy all me spawn and take back items me give, also when test fail.
internal static class SelfTests
{
    private const string TorchName = "lights.torch";
    private const string CandleName = "lights.candle";
    private const string CampfireName = "lights.campfire";

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        SelfTest.Register(TorchName, RunTorch);
        SelfTest.Register(CandleName, RunCandle);
        SelfTest.Register(CampfireName, RunCampfire);
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        SelfTest.Unregister(TorchName);
        SelfTest.Unregister(CandleName);
        SelfTest.Unregister(CampfireName);
#endif
    }

#if DEBUG
    private static GameObject Spawn(string prefabName, float distance)
    {
        var player = Player.m_localPlayer;
        var prefab = ZNetScene.instance.GetPrefab(prefabName);
        if (player == null || prefab == null)
        {
            return null;
        }
        var forward = player.transform.forward;
        forward.y = 0f;
        forward.Normalize();
        // A bit to the right: camera behind player, so screenshot show the light beside him, not hidden by him.
        var right = new Vector3(forward.z, 0f, -forward.x);
        var pos = player.transform.position + forward * distance + right * 1.5f;
        pos.y = ZoneSystem.instance.GetGroundHeight(pos);
        return Object.Instantiate(prefab, pos, Quaternion.LookRotation(-forward));
    }

    private static void Destroy(GameObject go)
    {
        if (go != null && ZNetScene.instance != null)
        {
            ZNetScene.instance.Destroy(go);
        }
    }

    private static float Fuel(Fireplace f) => f.m_nview.GetZDO().GetFloat(ZDOVars.s_fuel);

    private static int State(Fireplace f) => f.m_nview.GetZDO().GetInt(ZDOVars.s_state, 1);

    // ---------- lights.torch ----------

    private static IEnumerator RunTorch()
    {
        var failures = new List<string>();
        var checks = 0;
        void Check(bool ok, string what)
        {
            checks++;
            if (!ok)
            {
                failures.Add(what);
            }
        }

        var player = Player.m_localPlayer;
        GameObject go = null;
        try
        {
            go = Spawn("piece_groundtorch_wood", 3f);
            if (go == null)
            {
                SelfTest.Fail(TorchName, "could not spawn piece_groundtorch_wood");
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
            var fire = go.GetComponent<Fireplace>();
            Check(LightRules.IsManaged(fire), "standing wood torch must be a managed light (default list)");
            Check(!fire.m_canRefill, "managed light must have m_canRefill false (Awake postfix)");
            var prefab = LightRules.PrefabOf(fire);
            Check(prefab != null && prefab.m_canRefill, "prefab must keep vanilla m_canRefill true (only instances change)");

            // First owner tick (vanilla InvokeRepeating at 0 s, then every 2 s) top up start fuel to max.
            var until = Time.time + 3f;
            while (Fuel(fire) < fire.m_maxFuel && Time.time < until)
            {
                yield return null;
            }
            Check(Mathf.Approximately(Fuel(fire), fire.m_maxFuel), $"new torch must be topped up to {fire.m_maxFuel} fuel, has {Fuel(fire)}");
            Check(LightSwitch.IsOn(fire) && fire.IsBurning(), "new torch must be on and burning");
            SelfTest.Screenshot(TorchName, "on");
            yield return null;
            yield return null;

            var resinBefore = player.GetInventory().CountItems("$item_resin");
            var result = fire.Interact(player, false, false);
            Check(result, "Interact (E) must report done");
            Check(Mathf.Approximately(Fuel(fire), 0f), $"E on a lit torch must set fuel 0, has {Fuel(fire)}");
            Check(!fire.IsBurning(), "torch must stop burning after E");
            Check(State(fire) == 1, "torch off must keep vanilla state 1 (fuel 0 = off)");
            var hoverOff = fire.GetHoverText();
            Check(hoverOff.Contains("Turn on") && hoverOff.Contains(Localization.instance.Localize("$hud_off")),
                $"hover of an off torch must say Off and Turn on, is '{hoverOff}'");
            SelfTest.Screenshot(TorchName, "off");
            yield return null;
            yield return null;

            // Same frame again (batch feed, macro): ignored.
            fire.Interact(player, false, false);
            var sameFrame = fire.Interact(player, false, false);
            Check(!sameFrame, "second Interact in the same frame must be ignored");
            Check(LightSwitch.IsOn(fire), "first Interact of the frame must switch the torch back on");
            Check(Mathf.Approximately(Fuel(fire), fire.m_maxFuel), $"E on an off torch must fill fuel to max, has {Fuel(fire)}");
            yield return null;

            // Hold E: nothing.
            var held = fire.Interact(player, true, false);
            Check(!held && LightSwitch.IsOn(fire), "holding E must not switch the torch");
            // Shift+E (alt): switch too (vanilla alt = add fuel, no fuel here).
            yield return null;
            fire.Interact(player, false, true);
            Check(!LightSwitch.IsOn(fire), "Shift+E must switch the torch off as well");
            yield return null;
            fire.Interact(player, false, false);
            Check(LightSwitch.IsOn(fire), "E must switch it on again");
            Check(player.GetInventory().CountItems("$item_resin") == resinBefore, "switching must never take resin");
            var hoverOn = fire.GetHoverText();
            Check(hoverOn.Contains("Turn off") && hoverOn.Contains(Localization.instance.Localize("$hud_on")),
                $"hover of a lit torch must say On and Turn off, is '{hoverOn}'");

            // Burn: vanilla would burn (elapsed / secPerFuel) fuel. Me make secPerFuel 1 s on this instance and last tick
            // 60 s ago (world time is short on day 1: never go below 0 ticks), so vanilla would empty the torch.
            var zdo = fire.m_nview.GetZDO();
            var secPerFuel = fire.m_secPerFuel;
            try
            {
                fire.m_secPerFuel = 1f;
                zdo.Set(ZDOVars.s_lastTime, Math.Max(0L, ZNet.instance.GetTime().Ticks - TimeSpan.FromSeconds(60).Ticks));
                fire.UpdateFireplace();
            }
            finally
            {
                fire.m_secPerFuel = secPerFuel;
            }
            Check(Mathf.Approximately(Fuel(fire), fire.m_maxFuel), $"owner tick must burn no fuel, has {Fuel(fire)}");
            var age = ZNet.instance.GetTime().Ticks - zdo.GetLong(ZDOVars.s_lastTime);
            Check(age >= 0 && age < TimeSpan.FromSeconds(10).Ticks, "owner tick must refresh lastTime (no offline burn for a later vanilla owner)");

            // Partly burned (by an owner without the mod): next tick top up.
            zdo.Set(ZDOVars.s_fuel, 1.5f);
            fire.UpdateFireplace();
            Check(Mathf.Approximately(Fuel(fire), fire.m_maxFuel), $"lit light with fuel 1.5 must be topped up, has {Fuel(fire)}");
            // Off light stay off on tick (new frame: same-frame repeat guard).
            yield return null;
            fire.Interact(player, false, false);
            fire.UpdateFireplace();
            Check(Mathf.Approximately(Fuel(fire), 0f), "off light must stay at fuel 0 after an owner tick");

            // Radial menu and hotbar use: no fuel entry.
            Check(!fire.TryGetItems(player, out var items) && (items == null || items.Count == 0), "radial menu must offer no fuel item");
            Check(!fire.CanUseItems(player, false), "CanUseItems must be false for a light");

            // A light another mod made infinite is left to that mod (never switched with state 2).
            fire.m_infiniteFuel = true;
            Check(!LightRules.IsManaged(fire), "a light with m_infiniteFuel (other mod) must not be managed");
            fire.m_infiniteFuel = false;
            Check(LightRules.IsManaged(fire), "managed again without m_infiniteFuel");

            if (failures.Count == 0)
            {
                SelfTest.Pass(TorchName, $"{checks} checks OK (max fuel {fire.m_maxFuel})");
            }
            else
            {
                SelfTest.Fail(TorchName, $"{failures.Count} of {checks} checks failed: {string.Join("; ", failures.ToArray())}");
            }
        }
        finally
        {
            Destroy(go);
        }
    }

    // ---------- lights.candle ----------

    private static IEnumerator RunCandle()
    {
        var failures = new List<string>();
        var checks = 0;
        void Check(bool ok, string what)
        {
            checks++;
            if (!ok)
            {
                failures.Add(what);
            }
        }

        var player = Player.m_localPlayer;
        GameObject go = null;
        try
        {
            go = Spawn("Candle_resin", 2.5f);
            if (go == null)
            {
                SelfTest.Fail(CandleName, "could not spawn Candle_resin");
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
            var fire = go.GetComponent<Fireplace>();
            Check(LightRules.IsManaged(fire), "resin candle must be a managed light");
            Check(fire.m_canTurnOff, "resin candle keeps vanilla m_canTurnOff (checked in game data)");
            Check(LightSwitch.IsOn(fire), "new candle must be on");

            fire.Interact(player, false, false);
            yield return null;
            Check(State(fire) == 2 && Fuel(fire) > 0f, $"E on a lit candle must use vanilla off state 2 and keep fuel (state {State(fire)}, fuel {Fuel(fire)})");
            Check(!fire.IsBurning(), "candle must not burn when off");

            fire.Interact(player, false, false);
            yield return null;
            Check(State(fire) == 1 && fire.IsBurning(), $"E on an off candle must set state 1 and burn (state {State(fire)})");

            // Burnt-out candle (vanilla burned it before the mod): E light it again.
            fire.m_nview.GetZDO().Set(ZDOVars.s_fuel, 0f);
            yield return null;
            Check(!LightSwitch.IsOn(fire), "candle with 0 fuel counts as off");
            fire.Interact(player, false, false);
            yield return null;
            Check(Mathf.Approximately(Fuel(fire), fire.m_maxFuel) && State(fire) == 1, $"E on a burnt-out candle must refill it ({Fuel(fire)}/{fire.m_maxFuel})");

            if (failures.Count == 0)
            {
                SelfTest.Pass(CandleName, $"{checks} checks OK");
            }
            else
            {
                SelfTest.Fail(CandleName, $"{failures.Count} of {checks} checks failed: {string.Join("; ", failures.ToArray())}");
            }
        }
        finally
        {
            Destroy(go);
        }
    }

    // ---------- lights.campfire ----------

    private static IEnumerator RunCampfire()
    {
        var failures = new List<string>();
        var checks = 0;
        void Check(bool ok, string what)
        {
            checks++;
            if (!ok)
            {
                failures.Add(what);
            }
        }

        var player = Player.m_localPlayer;
        var inventory = player.GetInventory();
        GameObject go = null;
        var gaveWood = false;
        try
        {
            go = Spawn("fire_pit", 3f);
            if (go == null)
            {
                SelfTest.Fail(CampfireName, "could not spawn fire_pit");
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
            var fire = go.GetComponent<Fireplace>();
            Check(!LightRules.IsManaged(fire), "campfire must not be a managed light");
            Check(fire.m_canRefill, "campfire must keep m_canRefill");
            var hover = fire.GetHoverText();
            Check(hover.Contains(Localization.instance.Localize("$piece_fire_fuel")), $"campfire hover must keep the vanilla fuel line, is '{hover}'");

            var before = Fuel(fire);
            gaveWood = inventory.AddItem("Wood", 1, 1, 0, 0L, "", false) != null;
            var woodBefore = inventory.CountItems("$item_wood");
            fire.Interact(player, false, false);
            yield return null;
            Check(inventory.CountItems("$item_wood") == woodBefore - 1, "E on a campfire must take one wood (vanilla)");
            Check(Fuel(fire) > before, $"E on a campfire must add fuel ({before} -> {Fuel(fire)})");
            gaveWood = false;

            // Burn: secPerFuel 1 s on this instance, last tick 1.5 s ago = 1.5 fuel gone (world time short on day 1).
            var fuel = Fuel(fire);
            var secPerFuel = fire.m_secPerFuel;
            try
            {
                fire.m_secPerFuel = 1f;
                fire.m_nview.GetZDO().Set(ZDOVars.s_lastTime, Math.Max(0L, ZNet.instance.GetTime().Ticks - TimeSpan.FromSeconds(1.5).Ticks));
                fire.UpdateFireplace();
            }
            finally
            {
                fire.m_secPerFuel = secPerFuel;
            }
            Check(Fuel(fire) < fuel - 0.5f, $"campfire must still burn fuel ({fuel} -> {Fuel(fire)})");

            // Fires that can cook are never lights, even when listed (test rules, not the config file).
            var scene = ZNetScene.instance;
            foreach (var cooking in new[] { "fire_pit", "fire_pit_iron", "hearth", "bonfire", "piece_brazierfloor01", "piece_brazierfloor02", "piece_brazierceiling01" })
            {
                Check(LightRules.CanCook(scene.GetPrefab(cooking)), $"{cooking} must count as a cooking fire");
            }
            foreach (var light in Plugin.DefaultLights.Split(','))
            {
                Check(!LightRules.CanCook(scene.GetPrefab(light.Trim())), $"default light {light.Trim()} must not be a cooking fire");
            }
            try
            {
                ServerRules.TestRules = new LightsRules { Lights = "piece_groundtorch_wood, fire_pit, piece_brazierfloor01" };
                Check(!LightRules.IsManaged(fire), "a listed campfire must stay a normal fire");
            }
            finally
            {
                ServerRules.TestRules = null;
            }

            // Server rules on the wire, and the join check.
            var pkg = new ZPackage();
            new LightsRules { Lights = "a, b" }.Write(pkg);
            pkg.SetPos(0);
            Check(LightsRules.TryRead(pkg, out var back) && back.Lights == "a, b", "light rules survive the wire");
            pkg = new ZPackage();
            pkg.Write(LightsRules.Layout + 1);
            pkg.SetPos(0);
            Check(!LightsRules.TryRead(pkg, out _), "unknown rules layout refused");
            Check(!ServerRules.Receive(new ZPackage()), "single player never takes rules from a peer");
            Check(PlayerCheck.Decide(true, true, true, false, false, false) == JoinVerdict.Refuse
                  && PlayerCheck.Decide(true, true, true, false, false, true) == JoinVerdict.Allowed
                  && PlayerCheck.Decide(true, true, true, false, true, false) == JoinVerdict.HasMod
                  && PlayerCheck.Decide(false, true, true, false, false, false) == JoinVerdict.Skip,
                "join check: no mod refused (or allowed by setting), mod fine, only the server acts");

            // Flag of a fire me never changed stays as another mod set it, through apply and restore.
            fire.m_canRefill = false;
            LiveLights.ApplyAll();
            Check(!fire.m_canRefill, "ApplyAll must leave an unmanaged fire's flag alone");
            LiveLights.RestoreAll();
            Check(!fire.m_canRefill, "RestoreAll must leave an unmanaged fire's flag alone");
            LiveLights.ApplyAll();
            fire.m_canRefill = true;

            if (failures.Count == 0)
            {
                SelfTest.Pass(CampfireName, $"{checks} checks OK");
            }
            else
            {
                SelfTest.Fail(CampfireName, $"{failures.Count} of {checks} checks failed: {string.Join("; ", failures.ToArray())}");
            }
        }
        finally
        {
            if (gaveWood)
            {
                inventory.RemoveItem("$item_wood", 1);
            }
            Destroy(go);
        }
    }
#endif
}
