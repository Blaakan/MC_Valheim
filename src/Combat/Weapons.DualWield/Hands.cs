using System;
using System.Text;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.WeaponsDualWieldMod;

// What EquipItem prefix do with one equip (design 2.2 table).
internal enum EquipRow : byte
{
    Vanilla,      // V: shield, bow, two-handed, tool, spear, bomb, excluded, left hand shield/torch, empty right hand...
    MainHandKey,  // K: main-hand key held for this item: vanilla equip (off-hand weapon put away by vanilla)
    Restore,      // 1: right weapon came this frame as first half of a re-equip: it go back left, new one right
    OffHand,      // 2: second eligible weapon go to off hand (weapon already there put away)
    MainKeepOff,  // 3: right empty, lone off-hand weapon: new weapon right, off hand kept
    Torch,        // T: torch while paired: off-hand weapon put away, then vanilla put torch left, main stay
}

// Everything EquipRow decision read. Plain values: Route is pure, self test hammer it.
internal struct RouteInput
{
    internal bool Intent;      // main-hand key asked for this item (inside a toggle or queue window)
    internal bool XEligible;   // item being equipped can pair
    internal bool XTorch;
    internal bool XMarked;     // item carry the off-hand marker
    internal bool RNull;
    internal bool REligible;
    internal bool RCandidate;  // right item = this frame's restore candidate
    internal bool LNull;
    internal bool LEligible;
}

// Me = pair state of the local player's hands. Off-hand weapon live in vanilla m_leftItem (visuals, save, death,
// blocking all vanilla). Me mark it with custom data so re-equip bursts (load, draw, drag, radial hammer) put it back
// in the left hand whatever order they use (design 2.4).
// Keep the pair, five small things (design 2.4):
//   1 off-hand marker on the item      2 same-frame restore (rule 1)      3 lone off-hand weapon go right at end of
//   frame (never inside an equip/unequip call)      4 eating (refuse attack, wait return, keep main hidden)
//   5 rules / ObjectDB / player change = caches rebuilt, hands checked again.
internal static class Hands
{
    // Off-hand marker on the item in the off hand. Saved with the item; vanilla keep and ignore it.
    internal const string OffHandKey = ModInfo.Guid + ".OffHand";

    internal const float IntentLifetime = 5f;

    // Trigger of vanilla's put-away / draw animation (Humanoid.HideHandItems / ShowHandItems, key R). Swap fire it
    // when the hands change, like a draw.
    internal const string DrawTrigger = "equip_hip";

    // Top-left message when sprint stop the swap key (vanilla sprint clear the equip queue every tick).
    internal const string SprintMessage = "Cannot swap hands while sprinting";

    // Last rules object, ObjectDB and local player the Player.Update postfix saw (design 2.4, mechanism 5). Other
    // reference = rebuild caches, clear stale markers (new player), check the hands again.
    internal static DualRules SeenRules;
    internal static ObjectDB SeenObjectDb;
    internal static Player SeenPlayer;

    // Restore candidate: marked item vanilla just put in an empty right hand, and the frame it happened.
    private static ItemDrop.ItemData _candidate;
    private static int _candidateFrame = -1;

    // Main-hand key intents: item + time, fixed 4 slots (no alloc). "Alt + 1, then 2" keep intent of 1.
    private struct Intent
    {
        internal ItemDrop.ItemData Item;
        internal float Time;
    }

    private static readonly Intent[] Intents = new Intent[4];

    // Toggle window: inside Player.ToggleEquipped of local player (InToggle), and the item asked for with the key
    // held (ToggleItem). Queue window: inside Player.UpdateActionQueue (queued equip happen there).
    internal static bool InToggle;
    internal static ItemDrop.ItemData ToggleItem;
    internal static bool QueueWindow;

    // Inside local player's StartAttack: special asked for? (Attack.Start prefix read it.)
    internal static bool InStartAttack;
    internal static bool SecondaryRequested;

    // Swap queued as vanilla equip action of this off-hand weapon (null = none), and "next converted swing start combo
    // from step 0" flag (DualSwing clear it when such a swing really start).
    internal static ItemDrop.ItemData PendingSwap;
    internal static bool ComboRestart;

    // Eat return delayed (attack, dodge, swim): Player.Update postfix draw main weapon when it can.
    internal static bool PendingEatReturn;

    // First pairing of the session from player's own request: one help message.
    private static bool _firstPairShown;

#if DEBUG
    // Self test: throw inside the apply step once (hands must stay consistent).
    internal static bool TestThrowInApply;
#endif

    // OnActivated / OnDeactivated: first frame after activation see "all changed" and check everything.
    internal static void ResetSeen()
    {
        SeenRules = null;
        SeenObjectDb = null;
        SeenPlayer = null;
    }

    // Every per-player short-lived state gone (toggle, player change). First-pairing flag stay (per session).
    internal static void ResetState()
    {
        _candidate = null;
        _candidateFrame = -1;
        for (var i = 0; i < Intents.Length; i++)
        {
            Intents[i] = default;
        }
        InToggle = false;
        ToggleItem = null;
        QueueWindow = false;
        InStartAttack = false;
        SecondaryRequested = false;
        PendingSwap = null;
        ComboRestart = false;
        PendingEatReturn = false;
    }

    internal static bool IsMarked(ItemDrop.ItemData item) => item != null && item.GetCustomBool(OffHandKey);

    internal static void SetMarked(ItemDrop.ItemData item, bool value)
    {
        if (item != null && item.GetCustomBool(OffHandKey) != value)
        {
            item.SetCustomBool(OffHandKey, value);
        }
    }

    // Pair = eligible weapon in both hands.
    internal static bool IsPaired(Player player) =>
        player != null && Eligibility.IsEligible(player.m_rightItem) && Eligibility.IsEligible(player.m_leftItem);

    // ---------- equip rules (design 2.2) ----------

    // Pure: the row. Intent only ever exist for an eligible item.
    internal static EquipRow Route(RouteInput x)
    {
        if (x.Intent && x.XEligible)
        {
            return EquipRow.MainHandKey;
        }
        if (x.XEligible)
        {
            if (x.REligible && x.RCandidate && x.LNull && !x.XMarked)
            {
                return EquipRow.Restore;
            }
            if (x.REligible && (x.LNull || x.LEligible))
            {
                return EquipRow.OffHand;
            }
            if (x.RNull && x.LEligible)
            {
                return EquipRow.MainKeepOff;
            }
            return EquipRow.Vanilla;
        }
        if (x.XTorch && x.REligible && x.LEligible)
        {
            return EquipRow.Torch;
        }
        return EquipRow.Vanilla;
    }

    // Step 1 (decide): read every input, no change. Vanilla would refuse = vanilla run and refuse with own message.
    internal static EquipRow Decide(Player player, ItemDrop.ItemData item)
    {
        if (VanillaWouldRefuse(player, item))
        {
            return EquipRow.Vanilla;
        }
        return Route(ReadInput(player, item));
    }

    internal static RouteInput ReadInput(Player player, ItemDrop.ItemData item)
    {
        var right = player.m_rightItem;
        var left = player.m_leftItem;
        return new RouteInput
        {
            Intent = HasIntentNow(item),
            XEligible = Eligibility.IsEligible(item),
            XTorch = item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Torch,
            XMarked = IsMarked(item),
            RNull = right == null,
            REligible = Eligibility.IsEligible(right),
            RCandidate = right != null && ReferenceEquals(right, _candidate) && _candidateFrame == Time.frameCount,
            LNull = left == null,
            LEligible = Eligibility.IsEligible(left),
        };
    }

    // Same early refusals as Humanoid.EquipItem, same order (the New Game+ rule only concern utility and trinkets,
    // never a row me handle).
    private static bool VanillaWouldRefuse(Player player, ItemDrop.ItemData item)
    {
        if (player.IsItemEquiped(item) || !player.GetInventory().ContainsItem(item))
        {
            return true;
        }
        if (player.InAttack() || player.InDodge())
        {
            return true;
        }
        if (!player.IsDead() && player.IsSwimming() && !player.IsOnGround())
        {
            return true;
        }
        var shared = item.m_shared;
        if (shared.m_useDurability && item.m_durability <= 0f)
        {
            return true;
        }
        return shared.m_dlc.Length > 0 && (DLCMan.instance == null || !DLCMan.instance.IsDLCInstalled(shared.m_dlc));
    }

    // Row K: intent used up (toggle window one need nothing: EndToggle drop it).
    internal static void ConsumeIntent(ItemDrop.ItemData item) => DropIntent(item);

    // Row T: off-hand weapon put away first, then vanilla put the torch in the free left hand (design D4).
    internal static void ReleaseForTorch(Player player, bool triggerEquipEffects)
    {
        var left = player.m_leftItem;
        if (left == null)
        {
            return;
        }
        SetMarked(left, false);
        player.UnequipItem(left, triggerEquipEffects);
    }

    // Step 2 (apply) for rows 1-3, in fixed order: displaced item unequipped through vanilla UnequipItem, slot fields,
    // m_equipped, marker, hidden items, effects. Exception = finally re-sync m_equipped of X, R and L with the slots
    // and run SetupEquipment, so no item stay flagged equipped in no slot; caller report it.
    internal static void Apply(Player player, ItemDrop.ItemData item, EquipRow row, bool triggerEquipEffects)
    {
        var right = player.m_rightItem;
        var left = player.m_leftItem;
        var done = false;
        try
        {
            switch (row)
            {
                case EquipRow.Restore:
                    // Right weapon (came first in the burst) go back to the off hand, new one take the main hand.
                    player.m_rightItem = item;
                    ThrowIfTesting();
                    player.m_leftItem = right;
                    SetMarked(right, true);
                    SetMarked(item, false);
                    _candidate = null;
                    Finish(player, item, rightHand: true, triggerEquipEffects);
                    break;
                case EquipRow.OffHand:
                    if (left != null)
                    {
                        SetMarked(left, false);
                        player.UnequipItem(left, triggerEquipEffects);
                    }
                    ThrowIfTesting();
                    SetMarked(right, false);
                    player.m_leftItem = item;
                    SetMarked(item, true);
                    Finish(player, item, rightHand: false, triggerEquipEffects);
                    FirstPairingMessage(player, item);
                    break;
                case EquipRow.MainKeepOff:
                    player.m_rightItem = item;
                    ThrowIfTesting();
                    SetMarked(item, false);
                    SetMarked(left, true);
                    Finish(player, item, rightHand: true, triggerEquipEffects);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(row), row, "not an apply row");
            }
            done = true;
        }
        finally
        {
            if (!done)
            {
                Resync(player, item);
                Resync(player, right);
                Resync(player, left);
                player.SetupEquipment();
            }
        }
    }

    // End like vanilla's own hand branches: equipped flag, hidden items cleared, equip effect at the hand it went to
    // (players only, not in the main menu), SetupEquipment (ZDO visuals, status effects, stance), equip sound.
    private static void Finish(Player player, ItemDrop.ItemData item, bool rightHand, bool triggerEquipEffects)
    {
        item.m_equipped = true;
        player.m_hiddenRightItem = null;
        player.m_hiddenLeftItem = null;
        PendingEatReturn = false;
        var vis = player.m_visEquipment;
        if (vis != null && vis.m_isPlayer && (object)FejdStartup.instance == null)
        {
            var hand = rightHand ? vis.m_rightHand : vis.m_leftHand;
            if (hand != null)
            {
                item.m_shared.m_equipEffect.Create(hand.position, hand.rotation, null, 1f, -1, player.GetZDOID());
            }
        }
        player.SetupEquipment();
        if (triggerEquipEffects)
        {
            player.TriggerEquipEffect(item);
        }
    }

    private static void Resync(Player player, ItemDrop.ItemData item)
    {
        if (item != null)
        {
            item.m_equipped = player.IsItemEquiped(item);
        }
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private static void ThrowIfTesting()
    {
#if DEBUG
        if (TestThrowInApply)
        {
            TestThrowInApply = false;
            throw new InvalidOperationException("Self test: forced failure inside the equip step.");
        }
#endif
    }

    // EquipItem postfix: marked item landed in the right hand through vanilla = marker off; right hand empty before =
    // me keep it as this frame's restore candidate (first half of a re-equip burst, design 2.4 mechanism 2).
    internal static void OnEquipped(Player player, ItemDrop.ItemData item, bool rightWasEmpty)
    {
        if (!ReferenceEquals(player.m_rightItem, item) || !IsMarked(item))
        {
            return;
        }
        SetMarked(item, false);
        if (rightWasEmpty)
        {
            _candidate = item;
            _candidateFrame = Time.frameCount;
        }
    }

    // First pairing of the session from the player's own request (hotbar, inventory, radial; not load or draw).
    // Own try: a message never break the equip around it.
    private static void FirstPairingMessage(Player player, ItemDrop.ItemData offHand)
    {
        if (_firstPairShown || !(InToggle || QueueWindow))
        {
            return;
        }
        _firstPairShown = true;
        try
        {
            var sb = new StringBuilder("Dual wielding: ")
                .Append(Localization.instance.Localize(offHand.m_shared.m_name)).Append(" is in your off hand.");
            var mainKey = Controls.KeyName(Controls.MainKey);
            if (mainKey != null)
            {
                sb.Append(" Hold ").Append(mainKey).Append(" while equipping to replace your main weapon instead.");
            }
            var swapKey = Controls.KeyName(Controls.SwapKey);
            if (swapKey != null)
            {
                sb.Append(" Press ").Append(swapKey).Append(" to swap hands.");
            }
            player.Message(MessageHud.MessageType.Center, sb.ToString());
        }
        catch (Exception e)
        {
            PatchGuard.Report("Hands.FirstPairingMessage", e);
        }
    }

    // ---------- main-hand key (design 2.2, G3) ----------

    // ToggleEquipped prefix. Key held for an eligible item not equipped, not attacking (vanilla ignore the press
    // then) = toggle window for that item (item with no equip time equip inside the call). True = key window open.
    internal static bool BeginToggle(Player player, ItemDrop.ItemData item)
    {
        InToggle = true;
        ToggleItem = null;
        if (item == null || !Eligibility.IsEligible(item) || player.IsItemEquiped(item) || player.InAttack()
            || !Controls.MainHandHeld())
        {
            return false;
        }
        ToggleItem = item;
        return true;
    }

    // ToggleEquipped postfix. Now queued with the key = intent kept for the queued equip. Else (press cancelled a
    // queued equip, vanilla ignored it, key not held, equipped inside the call) = no intent for that item.
    internal static void EndToggle(Player player, ItemDrop.ItemData item, bool keyWindow)
    {
        CloseToggle();
        if (item == null)
        {
            return;
        }
        if (keyWindow && player.IsEquipActionQueued(item))
        {
            StoreIntent(item);
        }
        else
        {
            DropIntent(item);
        }
    }

    internal static void CloseToggle()
    {
        InToggle = false;
        ToggleItem = null;
    }

    // Row K allowed: inside toggle window for this item, or inside queue window with a live intent for it.
    private static bool HasIntentNow(ItemDrop.ItemData item)
    {
        if (ToggleItem != null && ReferenceEquals(ToggleItem, item))
        {
            return true;
        }
        return QueueWindow && FindIntent(item) >= 0;
    }

    private static int FindIntent(ItemDrop.ItemData item)
    {
        var now = Time.time;
        for (var i = 0; i < Intents.Length; i++)
        {
            if (Intents[i].Item != null && ReferenceEquals(Intents[i].Item, item) && now - Intents[i].Time <= IntentLifetime)
            {
                return i;
            }
        }
        return -1;
    }

    private static void StoreIntent(ItemDrop.ItemData item)
    {
        var now = Time.time;
        var slot = -1;
        var oldest = 0;
        for (var i = 0; i < Intents.Length; i++)
        {
            var e = Intents[i];
            if (e.Item != null && ReferenceEquals(e.Item, item))
            {
                slot = i;
                break;
            }
            if (slot < 0 && (e.Item == null || now - e.Time > IntentLifetime))
            {
                slot = i;
            }
            if (Intents[i].Time < Intents[oldest].Time)
            {
                oldest = i;
            }
        }
        Intents[slot >= 0 ? slot : oldest] = new Intent { Item = item, Time = now };
    }

    private static void DropIntent(ItemDrop.ItemData item)
    {
        for (var i = 0; i < Intents.Length; i++)
        {
            if (Intents[i].Item != null && ReferenceEquals(Intents[i].Item, item))
            {
                Intents[i] = default;
            }
        }
    }

    // ---------- every frame (Player.Update postfix, local player) ----------

    internal static void Tick(Player player)
    {
        var rules = ServerRules.Current;
        var db = ObjectDB.instance;
        if (!ReferenceEquals(rules, SeenRules) || !ReferenceEquals(db, SeenObjectDb) || !ReferenceEquals(player, SeenPlayer))
        {
            OnSeenChanged(player, rules, db);
        }
        if (PendingEatReturn)
        {
            TryEatReturn(player);
        }
        SettleLoneOffHand(player);
        if (PendingSwap != null)
        {
            KeepSwap(player);
        }
        if (Controls.SwapPressed())
        {
            TrySwap(player);
        }
    }

    // Mechanism 5: rules (server rules came or changed), ObjectDB or local player changed.
    private static void OnSeenChanged(Player player, DualRules rules, ObjectDB db)
    {
        var playerChanged = !ReferenceEquals(player, SeenPlayer);
        SeenRules = rules;
        SeenObjectDb = db;
        SeenPlayer = player;
        if (playerChanged)
        {
            // New player (login, respawn) or first frame after activation: nothing short-lived carry over.
            ResetState();
            DualSwing.Clear();
            ClearStaleMarkers(player);
        }
        Eligibility.Refresh(rules);
        MoveTemplates.Refresh(rules, player);
        Revalidate(player);
    }

    // Both hands hold one-handed weapons that are no longer both eligible (server's ExcludedWeapons came or changed):
    // off-hand weapon put away (UnequipItem run SetupEquipment: vanilla stance). Pair still held: SetupEquipment, so
    // stance follow the new moves settings at once. No pair: nothing of mine in the stance, nothing to redo.
    internal static void Revalidate(Player player)
    {
        if (player == null)
        {
            return;
        }
        var right = player.m_rightItem;
        var left = player.m_leftItem;
        if (Eligibility.IsOneHanded(right) && Eligibility.IsOneHanded(left)
            && !(Eligibility.IsEligible(right) && Eligibility.IsEligible(left)))
        {
            SetMarked(left, false);
            player.UnequipItem(left, false);
            return;
        }
        if (IsPaired(player) && player.m_nview != null && player.m_nview.IsValid())
        {
            player.SetupEquipment();
        }
    }

    // Mechanism 3: lone one-handed weapon in the left hand (right hand and hidden right empty) move to the main hand.
    // Only here and at StartAttack, never inside an equip/unequip call: callers that unequip right then left end with
    // both hands empty. Eating set the hidden right item, so nothing move then. Three reference checks when idle.
    internal static void SettleLoneOffHand(Player player)
    {
        var left = player.m_leftItem;
        if (left == null || player.m_rightItem != null || player.m_hiddenRightItem != null
            || left.m_shared.m_itemType != ItemDrop.ItemData.ItemType.OneHandedWeapon)
        {
            return;
        }
        player.m_leftItem = null;
        player.m_rightItem = left;
        SetMarked(left, false);
        player.SetupEquipment();
    }

    // ---------- swap key (design 2.3, G4, E2, D22) ----------

    // Swap = vanilla equip of the off-hand weapon into the main hand, through vanilla's own action queue
    // (Player.QueueEquipAction): same time as any equip (that weapon's m_equipDuration, then vanilla's 0.3 s queue
    // pause), same "equipping" animation and HUD bar, vanilla cancel it like any equip (dodge, jump, sprint, second
    // request for that item; attack me refuse meanwhile, see BeginAttack). Action end: vanilla fire its done trigger
    // (me set equip_hip, the draw animation of R, synced to all), then call EquipItem(off-hand weapon): EquipItem
    // prefix see PendingSwap and swap there.
    // Sprinting (IsRunning = vanilla CheckRun cleared the queue this tick, and do it again next tick): no queue, only
    // a message, so the key never do nothing silent.
    // True = swap queued. Self test call me direct too (key press no reach a test).
    internal static bool TrySwap(Player player)
    {
        if (!player.TakeInput() || !IsPaired(player) || player.InAttack() || AttackStarting(player) || player.InDodge()
            || PendingSwap != null || player.GetActionQueueCount() > 0)
        {
            return false;
        }
        if (player.IsRunning())
        {
            player.Message(MessageHud.MessageType.TopLeft, SprintMessage);
            return false;
        }
        var off = player.m_leftItem;
        player.QueueEquipAction(off);
        if (!player.IsEquipActionQueued(off))
        {
            return false;
        }
        var queue = player.m_actionQueue;
        for (var i = queue.Count - 1; i >= 0; i--)
        {
            var action = queue[i];
            if (ReferenceEquals(action.m_item, off) && action.m_type == Player.MinorActionData.ActionType.Equip)
            {
                action.m_doneAnimation = DrawTrigger;
                break;
            }
        }
        PendingSwap = off;
        return true;
    }

    // Swap still queued in vanilla's action queue (StartAttack refuse attacks then).
    internal static bool IsSwapQueued(Player player) =>
        PendingSwap != null && player.IsEquipActionQueued(PendingSwap);

    // Only while a swap wait: Player.Update postfix, and UpdateActionQueue prefix (right before vanilla can end the
    // action and fire its done trigger: pair lost inside a physics step, like swimming hide hands in FixedUpdate, never
    // reach a draw animation with nothing drawn). Vanilla dropped the action (dodge, jump, sprint, second request) =
    // forget it (sprint: message, player know why). Pair gone meanwhile (hidden, unequipped, eating, rules) = me take
    // the action out of the queue.
    internal static void KeepSwap(Player player)
    {
        if (!player.IsEquipActionQueued(PendingSwap))
        {
            PendingSwap = null;
            if (player.IsRunning())
            {
                player.Message(MessageHud.MessageType.TopLeft, SprintMessage);
            }
            return;
        }
        if (!IsPaired(player) || !ReferenceEquals(player.m_leftItem, PendingSwap))
        {
            CancelSwap(player);
        }
    }

    // Queued swap out of vanilla's queue (pair gone, feature off, self test end).
    internal static void CancelSwap(Player player)
    {
        var off = PendingSwap;
        PendingSwap = null;
        if (off != null && player != null && player.IsEquipActionQueued(off))
        {
            player.RemoveEquipAction(off);
        }
    }

    // EquipItem prefix, inside vanilla's queue window, for the queued off-hand weapon: hands swap now (after the
    // equip animation, as vanilla equip happen at the action end). Pair changed meanwhile = nothing (false): vanilla
    // would put the item in the main hand, not a swap.
    internal static bool IsQueuedSwap(ItemDrop.ItemData item) =>
        QueueWindow && PendingSwap != null && ReferenceEquals(item, PendingSwap);

    internal static bool CompleteSwap(Player player, bool triggerEquipEffects)
    {
        var newMain = PendingSwap;
        PendingSwap = null;
        if (!IsPaired(player) || !ReferenceEquals(player.m_leftItem, newMain))
        {
            return false;
        }
        var oldMain = player.m_rightItem;
        player.m_rightItem = newMain;
        player.m_leftItem = oldMain;
        SetMarked(newMain, false);
        SetMarked(oldMain, true);
        // No picking the hand between chained swings: next converted swing start the combo again.
        ComboRestart = true;
        // ZDO visuals, stance, status effects follow the new hands at once: cosmetic step below throw = hands still
        // in sync (vanilla make effect first; effect only need the hand bone, so order no matter to it).
        player.SetupEquipment();
        // Like vanilla one-handed equip: item's equip effect at the right hand (players, not main menu).
        var vis = player.m_visEquipment;
        if (vis != null && vis.m_isPlayer && vis.m_rightHand != null && (object)FejdStartup.instance == null)
        {
            newMain.m_shared.m_equipEffect.Create(vis.m_rightHand.position, vis.m_rightHand.rotation, null, 1f, -1,
                player.GetZDOID());
        }
        if (triggerEquipEffects)
        {
            player.TriggerEquipEffect(newMain);
        }
        player.Message(MessageHud.MessageType.TopLeft,
            "Main hand: " + Localization.instance.Localize(newMain.m_shared.m_name), 0, newMain.GetIcon());
        return true;
    }

    // Restart gap: StartAttack set m_currentAttack and fire trigger at once, animator enter attack state a frame or
    // more later, so InAttack (animator tags only) still false. Swap there = recorded swing's off-hand hits fall back
    // to old main weapon, now in left hand. Same limit as Weapon Moveset (attack never entering its state no block
    // swap for good).
    internal const float StartingAttackAge = 0.5f;

    internal static bool AttackStarting(Player player)
    {
        var attack = player.m_currentAttack;
        return attack != null && !attack.m_attackDone && !attack.m_wasInAttack && attack.m_time < StartingAttackAge;
    }

    // ---------- eating (design 2.4 mechanism 4, D20) ----------

    // Pair's main weapon hidden for eating, off-hand weapon alone in the left hand.
    internal static bool IsEatWindow(Player player) =>
        player.m_rightItem == null && player.m_hiddenRightItem != null && player.m_hiddenLeftItem == null
        && Eligibility.IsOneHanded(player.m_leftItem);

    // StartAttack prefix (local player). False = refuse the attack (eat window: under a second; no swinging the lone
    // left weapon with right-hand moves. Swap queued: no attack until the hands changed, like during an equip).
    internal static bool BeginAttack(Player player, bool secondary)
    {
        InStartAttack = true;
        SecondaryRequested = secondary;
        SettleLoneOffHand(player);
        return !IsEatWindow(player) && !IsSwapQueued(player);
    }

    internal static void EndAttack()
    {
        InStartAttack = false;
        SecondaryRequested = false;
    }

    // ShowHandItems prefix, eat return (onlyRightHand) of a pair. False = skip vanilla now and wait: vanilla EquipItem
    // refuse while attacking, dodging or swimming, and would drop the main weapon from the hands for good. Hidden
    // off-hand weapon too (full hide during the eat) = draw both.
    internal static bool BeforeEatReturn(Player player, ref bool onlyRightHand)
    {
        if (player.m_hiddenRightItem == null)
        {
            return true;
        }
        var hiddenLeftOneHanded = Eligibility.IsOneHanded(player.m_hiddenLeftItem);
        if (!hiddenLeftOneHanded && !Eligibility.IsOneHanded(player.m_leftItem))
        {
            return true;
        }
        if (player.InAttack() || player.InDodge() || (!player.IsDead() && player.IsSwimming() && !player.IsOnGround()))
        {
            PendingEatReturn = true;
            return false;
        }
        if (hiddenLeftOneHanded)
        {
            onlyRightHand = false;
        }
        return true;
    }

    // Player.Update: delayed eat return, as soon as nothing block it. Hidden main weapon gone (player equipped
    // something else, which clear hidden items like vanilla) = nothing to return.
    private static void TryEatReturn(Player player)
    {
        if (player.m_hiddenRightItem == null)
        {
            PendingEatReturn = false;
            return;
        }
        if (player.InAttack() || player.InDodge() || (!player.IsDead() && player.IsSwimming() && !player.IsOnGround()))
        {
            return;
        }
        PendingEatReturn = false;
        player.ShowHandItems(onlyRightHand: true, animation: false);
    }

    // HideHandItems prefix (full hide, or right-hand hide of a second eat): eat-hidden main weapon to keep. Left hand
    // not empty = vanilla go on and overwrite m_hiddenRightItem with the empty right hand. First eat (main weapon still
    // in hand) = null, vanilla hide it as usual.
    internal static ItemDrop.ItemData EatHiddenToKeep(Player player) =>
        player.m_rightItem == null && player.m_hiddenRightItem != null && Eligibility.IsOneHanded(player.m_leftItem)
            ? player.m_hiddenRightItem
            : null;

    // HideHandItems postfix: put it back (and on the back visuals). Eat return then draw both (full hide) or the main
    // weapon (second eat: rule 3 keep the off hand).
    internal static void KeepEatHidden(Player player, ItemDrop.ItemData mainWeapon)
    {
        if (player.m_rightItem != null || player.m_hiddenRightItem != null)
        {
            return;
        }
        player.m_hiddenRightItem = mainWeapon;
        if (player.m_visEquipment != null)
        {
            player.SetupVisEquipment(player.m_visEquipment, false);
        }
    }

    // ---------- stance (design 2.5) ----------

    // SetupAnimationState postfix: pair with valid template = the template's stance (DualAxes / Knives). No valid
    // template (default moves unusable, animator not set up yet) = pair swing with main weapon own moves (TryConvert
    // skip), so main weapon own stance; vanilla would take the off-hand weapon's (left item wins there).
    internal static void ApplyStance(Player player)
    {
        var right = player.m_rightItem;
        var left = player.m_leftItem;
        if (right == null || left == null || !Eligibility.IsEligible(right) || !Eligibility.IsEligible(left))
        {
            return;
        }
        var template = MoveTemplates.For(right, left, ServerRules.Current, player);
        player.SetAnimationState(template != null ? template.Stance : right.m_shared.m_animationState);
    }

    // ---------- life cycle ----------

    // Main-hand weapon never carry the marker. Stale one = item reached main hand while me off (two marked weapons
    // would swap hands at next draw). OnActivated and every local player change call me.
    internal static void ClearStaleMarkers(Player player)
    {
        if (player == null)
        {
            return;
        }
        SetMarked(player.m_rightItem, false);
        SetMarked(player.m_hiddenRightItem, false);
    }

    // Feature going off (design 7.4, D15): off-hand weapon back in the inventory, so vanilla code never meet a left
    // one-handed weapon. Hidden off-hand weapon forgotten same way (next draw show only main weapon). Unequip run
    // SetupEquipment: stance vanilla again. Me never do this while game quit (see CanReleaseNow).
    internal static void ReleaseOffHand(Player player)
    {
        if (player == null)
        {
            return;
        }
        var left = player.m_leftItem;
        if (left != null && left.m_shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon)
        {
            SetMarked(left, false);
            player.UnequipItem(left, false);
        }
        var hiddenLeft = player.m_hiddenLeftItem;
        if (hiddenLeft != null && hiddenLeft.m_shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon)
        {
            SetMarked(hiddenLeft, false);
            // Not equipped: vanilla UnequipItem only clear the hidden slot (and visuals), then return.
            player.UnequipItem(hiddenLeft, false);
        }
    }

    // Release only in a live world: at quit framework call OnDeactivated from OnDestroy after save and ZDO reset;
    // unequip then = effects on dying player, dirty log, for nothing (saved pair load next time).
    internal static bool CanReleaseNow(Player player)
    {
        return Game.instance != null && !Game.instance.IsShuttingDown() && player != null && player.m_nview != null
               && player.m_nview.IsValid();
    }
}
