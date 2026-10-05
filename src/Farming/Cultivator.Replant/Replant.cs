using UnityEngine;

namespace MC.Farming.CultivatorReplantMod;

// Me = what the cultivator aim at in one frame: plant under the crosshair. Replant set it in Player.UpdatePlacement
// prefix, ReplantHint read it in Hud.UpdateCrosshair postfix (Hud may run before or after Player: one frame slack).
internal struct ReplantTarget
{
    internal ZNetView View;
    internal PlantKind Kind;
    // One of our transplant saplings (E2), not a wild plant.
    internal bool IsSapling;
    // Cultivator level high enough for this plant.
    internal bool Allowed;
    // Time.frameCount when found.
    internal int Frame;

    // Found this frame or the frame before.
    internal bool IsFresh => Kind != null && View != null && Time.frameCount - Frame <= 1;
}

// Me = the Replant action (design 2.4). Build mode with the cultivator in hand: aim at a wild plant of the table
// (PlantCatalog) or at one of our transplant saplings, press Use (E) or gamepad X: plant come out of the ground as one
// transplant item. Ripe plant get picked first (E1: produce not lost). Costs like a vanilla hammer removal (tool
// stamina, tool wear, swing, noise, effects); no Farming skill, no build-remove debt.
// Input read in Player.UpdatePlacement PREFIX (before vanilla build menu toggle and place/remove), only local player.
// Pressed button reset (ZInput.ResetButtonStatus) also when refused: no other reader see it, button always do what
// the hint say.
// Keys: keyboard Use (follow rebinding). Gamepad X (JoyButtonX, free in build mode: JoySit in classic layout, JoyUse
// with no hover in alternative layouts) without the alt keys held, in every PC layout. Game's gamepad-mouse context
// (X = JoyUse = build menu) is Switch Joy-Con mouse only: PC never set ZInput source GamepadMouse, so me not care.
// At ship helm or on lox saddle (doodad controller), and in the frame E let go of it: no target, no hint, the press
// stay with the game (it let go of the helm).
// Target found the vanilla way (Player.FindHoverObject: first hit of the interact mask within reach), then
// ZNetView in parents + ZDO prefab hash. Never by component: YggaShoot1-3 carry TreeBase, YggaShoot_small1 a
// Destructible, bushes and forage a Pickable.
internal static class Replant
{
    internal const string KeyboardButton = "Use";
    private const string KeyboardSnapAlias = "TabRight";
    internal const string PadButtonX = "JoyButtonX";
    private const string PadAltKeys = "JoyAltKeys";
    private const string PadSit = "JoySit";
    private const string PadUse = "JoyUse";

    // Vanilla RemovePiece numbers.
    private const float RemoveNoise = 50f;
    private const float NoPress = -9999f;

    private static ReplantTarget _target;
    private static int _menuFrame = -10;
    private static int _helmFrame = -10;

    // Plant under the crosshair, set each UpdatePlacement of the local player (default = none). Check IsFresh.
    internal static ReplantTarget Target => _target;

    // Layout name that sit on gamepad X too: classic JoySit, alternative JoyUse. Consume reset it, hint borrow its
    // glyph when JoyButtonX show none.
    internal static string PadAlias => ZInput.InputLayout == InputLayout.Default ? PadSit : PadUse;

    // Feature off, new world: no target, no remembered frames.
    internal static void Reset()
    {
        _target = default;
        _menuFrame = -10;
        _helmFrame = -10;
    }

    // Player.UpdateHover postfix: player at a ship helm or on a lox saddle (doodad controller) this frame. UpdateHover
    // run before the Use block that let go of the helm, so the release frame count too: no hint, no replant on it.
    internal static void MarkHelmFrame() => _helmFrame = Time.frameCount;

    // Player.UpdatePlacement prefix (High priority), local player only, never skip the original. Caller catch.
    internal static void OnUpdatePlacement(Player p, bool takeInput)
    {
        _target = default;
        if (!Plugin.FeatureActive || !takeInput || !p.InPlaceMode() || p.IsDead())
        {
            return;
        }
        var tool = p.GetRightItem();
        if (tool == null || tool.m_shared.m_buildPieces == null || !CultivatorTiers.IsCultivator(tool))
        {
            return;
        }
        if (Hud.IsPieceSelectionVisible())
        {
            // Menu open: its buttons (X = favourite) belong to it. Remember frame: press that close it no replant.
            _menuFrame = Time.frameCount;
            return;
        }
        // Other mod made something hoverable in build mode: vanilla Use go to it, me stay out.
        if (p.m_hovering != null)
        {
            return;
        }
        // Me at ship helm or on lox (or let go of it this frame): E belong to game, no hint, no replant.
        if (Time.frameCount == _helmFrame)
        {
            return;
        }

        p.FindHoverObject(out var go, out _);
        if (go == null)
        {
            return;
        }
        var view = go.GetComponentInParent<ZNetView>();
        if (view == null || !view.IsValid())
        {
            return;
        }
        if (!PlantCatalog.TryFind(view.GetZDO().GetPrefab(), out var kind, out var isSapling))
        {
            return;
        }
        _target = new ReplantTarget
        {
            View = view,
            Kind = kind,
            IsSapling = isSapling,
            Allowed = CultivatorTiers.TierOf(tool) >= kind.Tier,
            Frame = Time.frameCount,
        };

        if (!ReadPress(out var keyboard, out var pad))
        {
            return;
        }
        Consume(keyboard, pad);
        if (Hud.InRadial() || PlayerController.HasInputDelay || Time.frameCount <= _menuFrame + 1
            || p.InAttack() || p.InDodge() || Time.time - p.m_lastToolUseTime <= p.m_removeDelay)
        {
            return;
        }
        TryReplant(p, tool, view, kind, isSapling, out _);
    }

    // The action itself, no input (self tests call it too). Checks first, before any side effect; refusal = why
    // (player already told when it make sense). True = plant gone, transplant given (inventory or ground).
    internal static bool TryReplant(Player p, ItemDrop.ItemData tool, ZNetView view, PlantKind kind, bool isSapling,
        out string refusal)
    {
        refusal = null;
        if (p == null || tool == null || kind == null || ZNetScene.instance == null)
        {
            refusal = "no player, cultivator, plant or world";
            return false;
        }
        if (view == null || !view.IsValid())
        {
            refusal = "the plant is gone";
            return false;
        }
        // Client still wait for server rules: everything of me vanilla, quiet.
        if (ServerRules.Current.IsPending)
        {
            refusal = "waiting for the server's rules";
            return false;
        }
        if (CultivatorTiers.TierOf(tool) < kind.Tier)
        {
            refusal = TierNeededText(kind.Tier);
            p.Message(MessageHud.MessageType.Center, refusal);
            return false;
        }
        var prefab = TransplantContent.ItemPrefab(kind);
        var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        if (drop == null)
        {
            refusal = "the transplant item is not ready";
            return false;
        }
        var pos = view.transform.position;
        // Ward like vanilla RemovePiece (flash, center message). No no-build-zone check (design D4).
        if (!PrivateArea.CheckAccess(pos))
        {
            refusal = "a ward protects it";
            p.Message(MessageHud.MessageType.Center, "$msg_privatezone");
            return false;
        }
        if (!p.HaveStamina(tool.m_shared.m_attack.m_attackStamina))
        {
            refusal = "not enough stamina";
            if (Hud.instance != null)
            {
                Hud.instance.StaminaBarEmptyFlash();
            }
            return false;
        }
        var pickable = view.GetComponent<Pickable>();
        if (pickable != null && pickable.GetEnabled == 0)
        {
            refusal = "the plant is turned off";
            return false;
        }

        // Own the ZDO first: pick RPC then run here at once (routed rpc to self is synchronous), destroy really
        // destroy the ZDO (only owner may).
        view.ClaimOwnership();
        if (!isSapling && pickable != null && pickable.CanBePicked())
        {
            // E1: produce drop at the plant, vanilla stats and skill.
            pickable.Interact(p, false, false);
        }
        // Pick of a one-shot plant destroy it already. Destroy now = ResetZDO now, object gone at frame end.
        if (view.IsValid())
        {
            ZNetScene.instance.Destroy(view.gameObject);
        }
        Give(p, prefab, drop, pos);
        PayLikeRemoval(p, tool, pos);
        return true;
    }

    // "Needs a black metal cultivator (level 4)" / "Needs an eitr cultivator (level 5)".
    internal static string TierNeededText(int tier)
    {
        var name = PlantCatalog.TierName(tier);
        var article = name.Length > 0 && "aeiou".IndexOf(name[0]) >= 0 ? "an " : "a ";
        return "Needs " + article + name + " cultivator (level " + tier + ")";
    }

    // Keyboard Use, or gamepad X without the alt keys held.
    private static bool ReadPress(out bool keyboard, out bool pad)
    {
        keyboard = ZInput.GetButtonDown(KeyboardButton);
        pad = ZInput.GetButtonDown(PadButtonX) && !ZInput.GetButton(PadAltKeys);
        return keyboard || pad;
    }

    // Reset only what was pressed. X also live under a layout name (classic JoySit, alternative JoyUse): that one too.
    private static void Consume(bool keyboard, bool pad)
    {
        if (keyboard)
        {
            ZInput.ResetButtonStatus(KeyboardButton);
            // Default E is also TabRight: build mode "cycle snap point" (Player.UpdatePlacementGhost, LateUpdate). Same
            // press this frame = same key: me eat it too, one press do one thing.
            if (ZInput.GetButtonDown(KeyboardSnapAlias))
            {
                ZInput.ResetButtonStatus(KeyboardSnapAlias);
            }
        }
        if (!pad)
        {
            return;
        }
        ZInput.ResetButtonStatus(PadButtonX);
        var alias = PadAlias;
        if (ZInput.GetButtonDown(alias))
        {
            ZInput.ResetButtonStatus(alias);
        }
    }

    // One transplant: inventory (top-left "added" line like a pickup), else dropped where the plant stood
    // (Pickable.Drop way: Instantiate + OnCreateNew) with vanilla "no room" message.
    private static void Give(Player p, GameObject prefab, ItemDrop drop, Vector3 at)
    {
        var inv = p.GetInventory();
        var data = drop.m_itemData;
        if (inv != null && inv.CanAddItem(prefab, 1) && inv.AddItem(prefab, 1))
        {
            var icons = data.m_shared.m_icons;
            var icon = icons != null && icons.Length > 0 ? data.GetIcon() : null;
            p.Message(MessageHud.MessageType.TopLeft, "$msg_added " + data.m_shared.m_name, 1, icon);
            return;
        }
        var go = UnityEngine.Object.Instantiate(prefab, at + Vector3.up * 0.5f,
            Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
        var dropped = go.GetComponent<ItemDrop>();
        if (dropped != null)
        {
            dropped.SetStack(1);
            ItemDrop.OnCreateNew(dropped);
        }
        p.Message(MessageHud.MessageType.Center, "$msg_noroom");
    }

    // Vanilla removal costs and feedback (Player.UpdatePlacement remove branch + RemovePiece), minus Farming debt.
    private static void PayLikeRemoval(Player p, ItemDrop.ItemData tool, Vector3 at)
    {
        p.m_lastToolUseTime = Time.time;
        // No vanilla place or remove from this call or from a click buffered in the last 0.2 s.
        p.m_placePressedTime = NoPress;
        p.m_removePressedTime = NoPress;
        var attack = tool.m_shared.m_attack;
        p.FaceLookDirection();
        if (attack != null && !string.IsNullOrEmpty(attack.m_attackAnimation) && p.m_zanim != null)
        {
            p.m_zanim.SetTrigger(attack.m_attackAnimation);
        }
        p.AddNoise(RemoveNoise);
        p.UseStamina(BuildStamina(p, tool));
        if (tool.m_shared.m_useDurability)
        {
            tool.m_durability -= p.GetPlaceDurability(tool) * Game.m_durabilityRate;
        }
        var zdoid = p.GetZDOID();
        tool.m_shared.m_destroyEffect?.Create(at, Quaternion.identity, null, 1f, -1, zdoid);
        p.m_removeEffects?.Create(at, Quaternion.identity, null, 1f, -1, zdoid);
    }

    // Vanilla GetBuildStamina read the right hand and the build table: only when the tool really is in hand in build
    // mode (self test may pass a tool not equipped), else the tool's plain attack stamina.
    private static float BuildStamina(Player p, ItemDrop.ItemData tool)
    {
        if (p.InPlaceMode() && ReferenceEquals(p.GetRightItem(), tool))
        {
            return p.GetBuildStamina();
        }
        return tool.m_shared.m_attack != null ? tool.m_shared.m_attack.m_attackStamina : 0f;
    }
}
