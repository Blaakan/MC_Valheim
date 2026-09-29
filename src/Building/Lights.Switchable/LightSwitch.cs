using UnityEngine;

namespace MC.Building.LightsSwitchableMod;

// Me = the switch. On = vanilla state 1 and fuel above 0; off = fuel 0 (or state 2 for pieces the game already let
// switch, like the resin candle). Only vanilla ZDO keys, so friend without mod see same light, and after uninstall
// an "off" torch is just an empty torch you can refuel.
// Me take ownership before switch (vanilla Interact and Sign do too): then my write land at once and my read is
// fresh, so fast double press never flip twice, and a modded owner never burn fuel.
internal static class LightSwitch
{
    private static int _lastFrame = -1;
    private static Fireplace _lastFire;

    internal static void Clear()
    {
        _lastFire = null;
        _lastFrame = -1;
    }

    // Light on? Same rule the game use for "burning", minus cover and water checks (those only hide the flame).
    internal static bool IsOn(Fireplace fire)
    {
        var zdo = fire.m_nview.GetZDO();
        return zdo.GetInt(ZDOVars.s_state, 1) == 1 && zdo.GetFloat(ZDOVars.s_fuel) > 0f;
    }

    // Press of E. False = nothing done (hold repeat, same frame twice, invalid object).
    internal static bool Toggle(Fireplace fire, bool hold)
    {
        var nview = fire.m_nview;
        if (hold || nview == null || !nview.IsValid())
        {
            return false;
        }
        // Other mod repeat Interact in same frame (batch feed, macro): one switch only.
        if (ReferenceEquals(fire, _lastFire) && _lastFrame == Time.frameCount)
        {
            return false;
        }
        _lastFire = fire;
        _lastFrame = Time.frameCount;

        nview.ClaimOwnership();
        var zdo = nview.GetZDO();
        // Candle: vanilla already switch it with state (players without the mod can switch it back). Never state 2 on
        // other lights: vanilla could not light them again.
        var useState = CanTurnOffInVanilla(fire);
        if (IsOn(fire))
        {
            if (useState)
            {
                // Candle: vanilla off switch (state 2), so friend without mod can switch it back on.
                nview.InvokeRPC("RPC_ToggleOn");
            }
            else
            {
                zdo.Set(ZDOVars.s_fuel, 0f);
                fire.UpdateState();
            }
        }
        else
        {
            if (zdo.GetInt(ZDOVars.s_state, 1) != 1)
            {
                nview.InvokeRPC("RPC_ToggleOn");
            }
            if (zdo.GetFloat(ZDOVars.s_fuel) < fire.m_maxFuel)
            {
                zdo.Set(ZDOVars.s_fuel, fire.m_maxFuel);
                fire.m_fuelAddedEffects.Create(fire.transform.position, fire.transform.rotation);
            }
            fire.UpdateState();
        }
        return true;
    }

    // Owner tick (vanilla every 2 s). Me burn nothing. Lit light with some fuel gone (burned by owner without mod)
    // get topped up, so everyone see it full. Me keep "lastTime" fresh like vanilla, so an owner without mod later
    // count no huge offline burn.
    internal static void OwnerTick(Fireplace fire)
    {
        var nview = fire.m_nview;
        if (!nview.IsOwner())
        {
            return;
        }
        var zdo = nview.GetZDO();
        zdo.Set(ZDOVars.s_lastTime, ZNet.instance.GetTime().Ticks);
        var fuel = zdo.GetFloat(ZDOVars.s_fuel);
        if (fuel > 0f && fuel < fire.m_maxFuel)
        {
            zdo.Set(ZDOVars.s_fuel, fire.m_maxFuel);
        }
    }

    // Hover: name, on/off, what E do. Same look as vanilla hover lines.
    internal static string HoverText(Fireplace fire)
    {
        var on = IsOn(fire);
        // On but dark: roof or ground too close above, smoke blocked, or under water (vanilla put flame out then).
        var state = !on ? " ( $hud_off )" : fire.IsBurning() ? " ( $hud_on )" : " ( $hud_on, blocked )";
        var text = fire.m_name + state + "\n[<color=yellow><b>$KEY_Use</b></color>] " + (on ? "Turn off" : "Turn on");
        return Localization.instance.Localize(text);
    }

    // Vanilla flag of the prefab (me never change m_canTurnOff, but read prefab to be safe from other mods).
    private static bool CanTurnOffInVanilla(Fireplace fire)
    {
        var prefab = LightRules.PrefabOf(fire);
        return prefab != null ? prefab.m_canTurnOff : fire.m_canTurnOff;
    }
}
