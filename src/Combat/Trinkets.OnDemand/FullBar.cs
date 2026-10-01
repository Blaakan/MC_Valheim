using System;
using System.Collections.Generic;
using MC.Shared;

namespace MC.Combat.TrinketsOnDemandMod;

// What a press of the trigger did (self test and messages read it).
internal enum TriggerResult : byte
{
    None,        // no press yet
    Fired,       // vanilla pop ran: effects added or refreshed, bar empty
    NoTrinket,   // no capacity, or no equipped item with a full-adrenaline effect
    NotFull,     // bar below max
    StillActive, // RefuseWhileActive and an equipped effect still runs
    Blocked,     // menu, chat, dead, radial, build mode (TakeInput and co.)
    Pending,     // client waiting for server rules: vanilla, key does nothing
    Failed,      // pop did not happen (another mod skipped AddAdrenaline?)
}

// Me = the full bar that waits (G4) and the trigger (G5).
// Hold: Player.AddAdrenaline prefix (Priority.First) hide the m_fullAdrenalineSE of every equipped item for the call,
// void finalizer put them back. Vanilla then take its own "no effect" branch: no pop, bar = exactly max. No transpiler;
// gain scaling, tier effects and other mods' patches run unchanged. Only calls that can reach the full-bar test hide
// (max > 0, and a gain, or bar already full). Depth counter: an effect set up inside the call can call AddAdrenaline
// again (SE_Stats.StartupEffects); restore only when the outermost counted call ends.
// Trigger: scope flag, consumed by the first AddAdrenaline call after it is set (our own AddAdrenaline(0)): that call
// hides nothing, so the vanilla pop runs (every equipped effect added or refreshed, bar 0, pop effect). Never tell the
// trigger by v == 0: vanilla melee miss send 0 too. Nested calls inside the pop (effect setup) hide as usual and put
// back when they end, before the pop loop goes on: a trinket effect with an up-front gain cannot pop again in a loop.
// SharedData is shared by every item of that prefab: hidden only for the length of one call on the main thread; the
// tooltip (only other reader) never run inside it.
internal static class FullBar
{
    private struct Hidden
    {
        internal ItemDrop.ItemData.SharedData Shared;
        internal StatusEffect Effect;
    }

    private static readonly List<Hidden> HiddenList = new List<Hidden>();
    private static int _depth;
    private static bool _triggering;

    internal static int Depth => _depth;
    internal static int HiddenCount => HiddenList.Count;

#if DEBUG
    // Self test read what the last press did.
    internal static TriggerResult LastResult = TriggerResult.None;
#endif

    // Prefix of Player.AddAdrenaline, local player only. True = counted (finalizer must call Exit).
    internal static bool Enter(Player player, float v)
    {
        if (_triggering)
        {
            _triggering = false; // our own trigger call: vanilla pop
            return false;
        }
        if (ServerRules.Current.IsPending)
        {
            return false;
        }
        _depth++;
        try
        {
            var max = player.GetMaxAdrenaline();
            if (max > 0f && (v > 0f || player.m_adrenaline >= max))
            {
                Hide(player);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("FullBar.Enter", e);
        }
        return true;
    }

    // Finalizer of Player.AddAdrenaline (also after an exception): outermost counted call put everything back.
    internal static void Exit()
    {
        if (_depth > 0)
        {
            _depth--;
        }
        if (_depth == 0)
        {
            RestoreAll();
        }
    }

    // OnActivated / OnDeactivated: nothing may stay hidden, no half-open scope.
    internal static void Reset()
    {
        _depth = 0;
        _triggering = false;
        RestoreAll();
    }

    private static void Hide(Player player)
    {
        var inventory = player.GetInventory();
        if (inventory == null)
        {
            return;
        }
        var items = inventory.GetAllItems();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item == null || !item.m_equipped)
            {
                continue;
            }
            var shared = item.m_shared;
            var se = shared != null ? shared.m_fullAdrenalineSE : null;
            if (se == null)
            {
                continue; // no effect, or already hidden (two items of one prefab share SharedData)
            }
            HiddenList.Add(new Hidden { Shared = shared, Effect = se });
            shared.m_fullAdrenalineSE = null;
        }
    }

    // Reverse order. A field another mod set meanwhile (not null) stay theirs.
    private static void RestoreAll()
    {
        for (var i = HiddenList.Count - 1; i >= 0; i--)
        {
            var h = HiddenList[i];
            if (h.Shared != null && h.Shared.m_fullAdrenalineSE == null)
            {
                h.Shared.m_fullAdrenalineSE = h.Effect;
            }
        }
        HiddenList.Clear();
    }

    // Some equipped item has a full-adrenaline effect. Trinket slot first (the usual case), then every equipped item
    // like the vanilla pop loop (extra-slot mods). No alloc.
    internal static bool HasEquippedEffect(Player player)
    {
        var trinket = player.m_trinketItem;
        if (trinket != null && trinket.m_shared != null && trinket.m_shared.m_fullAdrenalineSE != null)
        {
            return true;
        }
        var inventory = player.GetInventory();
        if (inventory == null)
        {
            return false;
        }
        var items = inventory.GetAllItems();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item != null && item.m_equipped && item.m_shared != null && item.m_shared.m_fullAdrenalineSE != null)
            {
                return true;
            }
        }
        return false;
    }

    // Some equipped item's effect still runs on the player.
    internal static bool AnyEffectRunning(Player player)
    {
        var seman = player.GetSEMan();
        var inventory = player.GetInventory();
        if (seman == null || inventory == null)
        {
            return false;
        }
        var items = inventory.GetAllItems();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var se = item != null && item.m_equipped && item.m_shared != null ? item.m_shared.m_fullAdrenalineSE : null;
            if (se != null && seman.HaveStatusEffect(se.NameHash()))
            {
                return true;
            }
        }
        return false;
    }

    // Bar full now: capacity above 0 and bar at (or above, after a swap to a cheaper trinket) the max.
    internal static bool IsFull(Player player)
    {
        var max = player.GetMaxAdrenaline();
        return max > 0f && player.m_adrenaline >= max;
    }

    // The press (gates already passed): checks in order, then the vanilla pop through AddAdrenaline(0).
    internal static TriggerResult Fire(Player player, TrinketRules rules)
    {
        var max = player.GetMaxAdrenaline();
        if (max <= 0f || !HasEquippedEffect(player))
        {
            return TriggerResult.NoTrinket;
        }
        if (player.m_adrenaline < max)
        {
            return TriggerResult.NotFull;
        }
        if (rules.RefuseWhileActive && AnyEffectRunning(player))
        {
            return TriggerResult.StillActive;
        }
        _triggering = true;
        try
        {
            player.AddAdrenaline(0f);
        }
        finally
        {
            // Normally consumed by our prefix already; a prefix that never ran (another mod) must not leave it open.
            _triggering = false;
        }
        return player.m_adrenaline < max ? TriggerResult.Fired : TriggerResult.Failed;
    }
}
