using UnityEngine;

namespace MC.Crafting.StationsBatchFeedMod;

// Me = which vanilla "add one" path a spot use. One kind per path.
internal enum FeedKind
{
    None,
    SmelterInput,
    SmelterFuel,
    Fire,
    CookFood,
    CookFuel,
    ShieldFuel,
    TurretAmmo,
}

// Me = one covered spot: the station, the thing vanilla call Interact on (switch or station itself) and its net view.
// Me know per kind: value from local ZDO, vanilla "can add" rule, room, display, labels, add effect list.
// Coverage by component class, never by prefab name: modded pieces on same components work too.
// Spot with capacity 1 or less = not covered (nothing to multiply): no hint, vanilla press.
internal readonly struct FeedTarget
{
    internal readonly FeedKind Kind;
    internal readonly MonoBehaviour Station;
    internal readonly MonoBehaviour UseTarget;
    internal readonly ZNetView NView;

    private FeedTarget(FeedKind kind, MonoBehaviour station, MonoBehaviour useTarget, ZNetView nview)
    {
        Kind = kind;
        Station = station;
        UseTarget = useTarget;
        NView = nview;
    }

    // Vanilla Player.Interact call Interact on this. Switch and every station class are Interactable.
    internal Interactable Interactable => (Interactable)UseTarget;

    // Net view alive, ZDO there. Every reader below need this first (some vanilla getters no check it).
    internal bool IsLive => NView != null && NView.IsValid() && Station != null && UseTarget != null;

    internal string PieceName => NView != null ? Utils.GetPrefabName(NView.gameObject) : "?";

    // Hovered object -> covered spot. Same lookup as vanilla Player.Interact (first Interactable up the tree).
    internal static bool TryResolve(GameObject go, out FeedTarget target)
    {
        target = default;
        if (go == null)
        {
            return false;
        }
        var component = go.GetComponentInParent<Interactable>() as Component;
        return component != null && TryResolve(component, out target);
    }

    // Component that vanilla Interact (or hover) on -> covered spot.
    internal static bool TryResolve(Component component, out FeedTarget target)
    {
        target = default;
        switch (component)
        {
            case Switch sw:
                return TryResolveSwitch(sw, out target);
            case Fireplace fire:
                if (FireSkip(fire) == null)
                {
                    target = new FeedTarget(FeedKind.Fire, fire, fire, fire.m_nview);
                    return true;
                }
                return false;
            case CookingStation station:
                // Station with food switch: vanilla Interact on body do nothing, switch do the work.
                if (station.m_addFoodSwitch == null && CookFoodSkip(station) == null)
                {
                    target = new FeedTarget(FeedKind.CookFood, station, station, station.m_nview);
                    return true;
                }
                return false;
            case Turret turret:
                if (TurretSkip(turret) == null)
                {
                    target = new FeedTarget(FeedKind.TurretAmmo, turret, turret, turret.m_nview);
                    return true;
                }
                return false;
        }
        return false;
    }

    // Switch belong to station up the tree. Me match by reference: only the ADD switches count (never Empty switch).
    private static bool TryResolveSwitch(Switch sw, out FeedTarget target)
    {
        target = default;
        var smelter = sw.GetComponentInParent<Smelter>();
        if (smelter != null)
        {
            if (smelter.m_addOreSwitch == sw && SmelterInputSkip(smelter) == null)
            {
                target = new FeedTarget(FeedKind.SmelterInput, smelter, sw, smelter.m_nview);
                return true;
            }
            if (smelter.m_addWoodSwitch == sw && SmelterFuelSkip(smelter) == null)
            {
                target = new FeedTarget(FeedKind.SmelterFuel, smelter, sw, smelter.m_nview);
                return true;
            }
        }

        var station = sw.GetComponentInParent<CookingStation>();
        if (station != null)
        {
            if (station.m_addFoodSwitch == sw && CookFoodSkip(station) == null)
            {
                target = new FeedTarget(FeedKind.CookFood, station, sw, station.m_nview);
                return true;
            }
            if (station.m_addFuelSwitch == sw && CookFuelSkip(station) == null)
            {
                target = new FeedTarget(FeedKind.CookFuel, station, sw, station.m_nview);
                return true;
            }
        }

        var shield = sw.GetComponentInParent<ShieldGenerator>();
        if (shield != null && shield.m_addFuelSwitch == sw && ShieldFuelSkip(shield) == null)
        {
            target = new FeedTarget(FeedKind.ShieldFuel, shield, sw, shield.m_nview);
            return true;
        }
        return false;
    }

    // Static coverage rules. Null = covered, else why not. Coverage dump print same reasons.
    internal static string SmelterInputSkip(Smelter s) =>
        s.m_addOreSwitch == null ? "no input switch"
        : s.m_maxOre <= 1 ? "capacity " + s.m_maxOre
        : null;

    internal static string SmelterFuelSkip(Smelter s) =>
        s.m_addWoodSwitch == null ? "no fuel switch"
        : s.m_fuelItem == null ? "no fuel item"
        : s.m_maxFuel <= 1 ? "capacity " + s.m_maxFuel
        : null;

    internal static string FireSkip(Fireplace f) =>
        !f.m_canRefill ? "not refillable"
        : f.m_infiniteFuel ? "infinite fuel"
        : f.m_fuelItem == null ? "no fuel item"
        : f.m_maxFuel <= 1f ? "capacity " + f.m_maxFuel
        : null;

    internal static string CookFoodSkip(CookingStation c) =>
        c.m_slots == null || c.m_slots.Length <= 1 ? "capacity " + (c.m_slots == null ? 0 : c.m_slots.Length) + " slot(s)"
        : null;

    internal static string CookFuelSkip(CookingStation c) =>
        c.m_addFuelSwitch == null ? "no fuel switch"
        : c.m_fuelItem == null ? "no fuel item"
        : c.m_maxFuel <= 1 ? "capacity " + c.m_maxFuel
        : null;

    internal static string ShieldFuelSkip(ShieldGenerator g) =>
        g.m_addFuelSwitch == null ? "no fuel switch"
        : g.m_fuelItems == null || g.m_fuelItems.Count == 0 ? "no fuel items"
        : g.m_maxFuel <= 1 ? "capacity " + g.m_maxFuel
        : null;

    internal static string TurretSkip(Turret t) =>
        t.m_maxAmmo <= 1 ? "capacity " + t.m_maxAmmo
        : null;

    // Local ZDO copy (stale when other game own the station). Call only when IsLive.
    internal float ReadValue()
    {
        switch (Kind)
        {
            case FeedKind.SmelterInput:
                return ((Smelter)Station).GetQueueSize();
            case FeedKind.SmelterFuel:
                return ((Smelter)Station).GetFuel();
            case FeedKind.Fire:
                return NView.GetZDO().GetFloat(ZDOVars.s_fuel);
            case FeedKind.CookFood:
                return UsedSlots((CookingStation)Station, NView.GetZDO());
            case FeedKind.CookFuel:
                return ((CookingStation)Station).GetFuel();
            case FeedKind.ShieldFuel:
                return ((ShieldGenerator)Station).GetFuel();
            case FeedKind.TurretAmmo:
                return ((Turret)Station).GetAmmo();
        }
        return 0f;
    }

    // Same test as vanilla GetFreeSlot: slot string empty = free.
    private static int UsedSlots(CookingStation station, ZDO zdo)
    {
        var used = 0;
        for (var i = 0; i < station.m_slots.Length; i++)
        {
            if (zdo.GetString("slot" + i) != "")
            {
                used++;
            }
        }
        return used;
    }

    // Vanilla "can add one more" rule, exact compare (floats too), so room never drift from vanilla.
    internal bool CanAdd(float v)
    {
        switch (Kind)
        {
            case FeedKind.SmelterInput:
                return v < ((Smelter)Station).m_maxOre;
            case FeedKind.SmelterFuel:
                return !(v > ((Smelter)Station).m_maxFuel - 1);
            case FeedKind.Fire:
                return Mathf.CeilToInt(v) < ((Fireplace)Station).m_maxFuel;
            case FeedKind.CookFood:
                return v < ((CookingStation)Station).m_slots.Length;
            case FeedKind.CookFuel:
                return !(v > ((CookingStation)Station).m_maxFuel - 1);
            case FeedKind.ShieldFuel:
                return !(v > ((ShieldGenerator)Station).m_maxFuel - 1);
            case FeedKind.TurretAmmo:
                return v < ((Turret)Station).m_maxAmmo;
        }
        return false;
    }

    // Value after one vanilla add on owner. Fire owner clamp to max, others just +1.
    private float Step(float v)
    {
        if (Kind == FeedKind.Fire)
        {
            var max = ((Fireplace)Station).m_maxFuel;
            return Mathf.Clamp(Mathf.Clamp(v, 0f, max) + 1f, 0f, max);
        }
        return v + 1f;
    }

    // How many adds fit, at most limit. Me run vanilla rule step by step on a copy (no formula, no off-by-one).
    internal int Room(float v, int limit)
    {
        var room = 0;
        while (room < limit && CanAdd(v))
        {
            v = Step(v);
            room++;
        }
        return room;
    }

    // "cur/max" like vanilla hover of this spot.
    internal string Display(float v)
    {
        switch (Kind)
        {
            case FeedKind.SmelterInput:
                return (int)v + "/" + ((Smelter)Station).m_maxOre;
            case FeedKind.SmelterFuel:
                return Mathf.Ceil(v) + "/" + ((Smelter)Station).m_maxFuel;
            case FeedKind.Fire:
                return Mathf.Ceil(v) + "/" + (int)((Fireplace)Station).m_maxFuel;
            case FeedKind.CookFood:
                return (int)v + "/" + ((CookingStation)Station).m_slots.Length;
            case FeedKind.CookFuel:
                return Mathf.Ceil(v) + "/" + ((CookingStation)Station).m_maxFuel;
            case FeedKind.ShieldFuel:
                return Mathf.Ceil(v) + "/" + ((ShieldGenerator)Station).m_maxFuel;
            case FeedKind.TurretAmmo:
                return (int)v + "/" + ((Turret)Station).m_maxAmmo;
        }
        return "";
    }

    // Action words of hint line, same tokens vanilla hover use for its Use line.
    internal string ActionLabel()
    {
        switch (Kind)
        {
            case FeedKind.SmelterInput:
                return ((Smelter)Station).m_addOreTooltip;
            case FeedKind.SmelterFuel:
                return "$piece_smelter_add " + ((Smelter)Station).m_fuelItem.m_itemData.m_shared.m_name;
            case FeedKind.Fire:
                return "$piece_use " + ((Fireplace)Station).m_fuelItem.m_itemData.m_shared.m_name;
            case FeedKind.CookFood:
                return ((CookingStation)Station).m_addItemTooltip;
            case FeedKind.CookFuel:
                return "$piece_smelter_add " + ((CookingStation)Station).m_fuelItem.m_itemData.m_shared.m_name;
            case FeedKind.ShieldFuel:
                return ((ShieldGenerator)Station).m_add;
            case FeedKind.TurretAmmo:
                return "$piece_turret_addammo";
        }
        return "";
    }

    // Vanilla "no more room" words of this spot (message goes through MessageHud, which localize it).
    internal string FullMessage()
    {
        switch (Kind)
        {
            case FeedKind.Fire:
                return Localization.instance.Localize("$msg_cantaddmore", ((Fireplace)Station).m_fuelItem.m_itemData.m_shared.m_name);
            case FeedKind.CookFood:
                return "$msg_nocookroom";
            default:
                return "$msg_itsfull";
        }
    }

    // Dynamic check. Cooking station with a finished item: vanilla E take that first, so no batch (and no hint).
    internal bool IsBatchable() => Kind != FeedKind.CookFood || !((CookingStation)Station).HaveDoneItem();

    // Would vanilla do anything on a hold repeat here? Cooking station body never; others by their repeat interval.
    internal bool ReactsToHold()
    {
        switch (UseTarget)
        {
            case Switch sw:
                return sw.m_holdRepeatInterval > 0f;
            case Fireplace fire:
                return fire.m_holdRepeatInterval > 0f;
            case Turret turret:
                return turret.m_holdRepeatInterval > 0f;
            default:
                return false;
        }
    }

    // Vanilla Fireplace.Interact: plain E on a fire that can turn off and has fuel = toggle, no add.
    internal bool WouldToggle(bool hold, bool alt)
    {
        if (Kind != FeedKind.Fire)
        {
            return false;
        }
        var fire = (Fireplace)Station;
        return fire.m_canTurnOff && !hold && !alt && NView.GetZDO().GetFloat(ZDOVars.s_fuel) > 0f;
    }

    // Missile vanilla will pick next press: same call as Turret.UseItem(user, null).
    internal ItemDrop.ItemData PredictAmmo(Inventory inventory) =>
        Kind == FeedKind.TurretAmmo ? ((Turret)Station).FindAmmoItem(inventory, onlyCurrentlyLoadableType: true) : null;

    // Add effect list the owner play once per add. Null = none to mute (cooking food keep its per-slot effect).
    internal EffectList GetAddEffects()
    {
        switch (Kind)
        {
            case FeedKind.SmelterInput:
                return ((Smelter)Station).m_oreAddedEffects;
            case FeedKind.SmelterFuel:
                return ((Smelter)Station).m_fuelAddedEffects;
            case FeedKind.Fire:
                return ((Fireplace)Station).m_fuelAddedEffects;
            case FeedKind.CookFuel:
                return ((CookingStation)Station).m_fuelAddedEffects;
            case FeedKind.ShieldFuel:
                return ((ShieldGenerator)Station).m_fuelAddedEffects;
            case FeedKind.TurretAmmo:
                return ((Turret)Station).m_addAmmoEffect;
            default:
                return null;
        }
    }

    internal void SetAddEffects(EffectList effects)
    {
        switch (Kind)
        {
            case FeedKind.SmelterInput:
                ((Smelter)Station).m_oreAddedEffects = effects;
                break;
            case FeedKind.SmelterFuel:
                ((Smelter)Station).m_fuelAddedEffects = effects;
                break;
            case FeedKind.Fire:
                ((Fireplace)Station).m_fuelAddedEffects = effects;
                break;
            case FeedKind.CookFuel:
                ((CookingStation)Station).m_fuelAddedEffects = effects;
                break;
            case FeedKind.ShieldFuel:
                ((ShieldGenerator)Station).m_fuelAddedEffects = effects;
                break;
            case FeedKind.TurretAmmo:
                ((Turret)Station).m_addAmmoEffect = effects;
                break;
        }
    }
}
