using System;
using System.Collections.Generic;
using System.Globalization;
using MC.Shared;
using UnityEngine;

namespace MC.Farming.CultivatorReplantMod;

// Me = crosshair hint of Replant (design 2.4, E5), from the Hud.UpdateCrosshair postfix, every frame. Only when
// vanilla left the hover text empty (build mode always does) and Replant found a plant this frame or the one before:
//   "<plant name>\n[E] Replant"                                  cultivator level high enough (crosshair yellow)
//   "<plant name>\nNeeds a black metal cultivator (level 4)"     too low (muted, crosshair stay vanilla)
// No plant, Yggdrasil transplant picked for placing: "Plant 2 to 6 m from an Ancient Root" (2 = its grow radius,
// closer it never have room to grow; 6 = rules RootRange).
// Key = the button Replant really read: keyboard Use or gamepad X (vanilla's two gamepad Replace calls of
// Hud.UpdateCrosshair applied). Key text from Localization.GetBoundKeyString, not a "$KEY_" token: Localize cache
// never forget a key after a rebind. When JoyButtonX show no glyph, me borrow the glyph of the layout name on X
// (classic JoySit, alternative JoyUse), then plain "X"; never the keyboard key.
// Strings built once per (plant, sapling, allowed, key mode) and reused: no allocation per frame. Cache cleared on
// layout or device change (ZInput.OnInputLayoutChanged), language change and while the game menu is open (key
// rebinding live in its settings). No ward check here (PrivateArea.CheckAccess allocate): the press tell.
internal static class ReplantHint
{
    // Slot = sapling bit | allowed bit | gamepad bit (0 keyboard Use, 1 gamepad X) << 2.
    private const int SlotCount = 8;
    private const string PadFallbackKey = "X";
    private const string KeyOpen = "[<color=yellow><b>";
    private const string KeyClose = "</b></color>] ";
    private const string Muted = "<color=#A0A0A0>";
    private const string MutedEnd = "</color>";

    private static readonly Dictionary<PlantKind, string[]> Cache = new Dictionary<PlantKind, string[]>();
    private static readonly Action Invalidate = OnInvalidate;

    private static bool _hooked;
    private static PlantKind _yggKind;
    private static string _rootText;
    private static float _rootRange = float.NaN;

    // Hud.UpdateCrosshair postfix. Caller catch.
    internal static void Update(Hud hud, Player player)
    {
        if (player == null || !ReferenceEquals(player, Player.m_localPlayer) || hud.m_hoverName == null)
        {
            return;
        }
        if (Menu.IsVisible())
        {
            ClearCache();
            return;
        }
        if (!Plugin.FeatureActive || !player.InPlaceMode() || !string.IsNullOrEmpty(hud.m_hoverName.text))
        {
            return;
        }
        if (TextViewer.instance != null && TextViewer.instance.IsVisible())
        {
            return;
        }
        var rules = ServerRules.Current;
        if (rules.IsPending)
        {
            return;
        }
        Hook();

        var target = Replant.Target;
        if (target.IsFresh)
        {
            if (!target.View.IsValid())
            {
                return; // dug up this frame
            }
            hud.m_hoverName.text = Text(target);
            if (target.Allowed && hud.m_crosshair != null)
            {
                hud.m_crosshair.color = Color.yellow;
            }
            return;
        }

        if (IsPlacingYggdrasil(player))
        {
            hud.m_hoverName.text = RootText(_yggKind.GrowRadius, rules.RootRange);
        }
    }

    // Feature off: listeners off, strings forgot.
    internal static void Reset()
    {
        if (_hooked)
        {
            ZInput.OnInputLayoutChanged -= Invalidate;
            Localization.OnLanguageChange = (Action)Delegate.Remove(Localization.OnLanguageChange, Invalidate);
            _hooked = false;
        }
        ClearCache();
    }

    private static void Hook()
    {
        if (_hooked)
        {
            return;
        }
        ZInput.OnInputLayoutChanged += Invalidate;
        Localization.OnLanguageChange = (Action)Delegate.Combine(Localization.OnLanguageChange, Invalidate);
        _hooked = true;
    }

    private static void OnInvalidate()
    {
        try
        {
            ClearCache();
        }
        catch (Exception e)
        {
            PatchGuard.Report("ReplantHint.OnInvalidate", e);
        }
    }

    private static void ClearCache()
    {
        if (Cache.Count > 0)
        {
            Cache.Clear();
        }
        _rootText = null;
    }

    private static string Text(ReplantTarget target)
    {
        var pad = ZInput.IsGamepadActive();
        var slot = (target.IsSapling ? 1 : 0) | (target.Allowed ? 2 : 0) | (pad ? 4 : 0);
        if (!Cache.TryGetValue(target.Kind, out var slots))
        {
            slots = new string[SlotCount];
            Cache[target.Kind] = slots;
        }
        return slots[slot] ??= Build(target, pad);
    }

    private static string Build(ReplantTarget target, bool pad)
    {
        var name = Localization.instance.Localize(TargetName(target));
        if (!target.Allowed)
        {
            return name + "\n" + Muted + Replant.TierNeededText(target.Kind.Tier) + MutedEnd;
        }
        var text = name + "\n" + KeyOpen + KeyText(pad) + KeyClose + "Replant";
        if (pad)
        {
            // Same as vanilla Hud.UpdateCrosshair with a gamepad: glyph sprite without brackets.
            text = text.Replace("[<color=yellow><b><sprite=", "<sprite=");
            text = text.Replace("\"></b></color>]", "\">");
        }
        return text;
    }

    // Vanilla hover name for picked plants (bushes, forage), else our plain name (Yggdrasil shoots, our saplings).
    private static string TargetName(ReplantTarget target)
    {
        var kind = target.Kind;
        if (target.IsSapling)
        {
            return kind.ItemDisplayName;
        }
        var pickable = target.View.GetComponent<Pickable>();
        if (pickable != null && (!string.IsNullOrEmpty(pickable.m_overrideName) || pickable.m_itemPrefab != null))
        {
            var name = pickable.GetHoverName();
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }
        }
        return kind.DisplayName;
    }

    // Live binding of the button Replant read (Localization.Translate "$KEY_" rule without its cache). Gamepad: X glyph,
    // else glyph of the layout name on X, else plain "X"; never the keyboard key (that button do nothing on pad).
    private static string KeyText(bool pad)
    {
        var loc = Localization.instance;
        if (!pad)
        {
            return loc.GetBoundKeyString(Replant.KeyboardButton);
        }
        var key = loc.GetBoundKeyString(Replant.PadButtonX, emptyStringOnMissing: true);
        if (key.Length > 0)
        {
            return key;
        }
        key = loc.GetBoundKeyString(Replant.PadAlias, emptyStringOnMissing: true);
        return key.Length > 0 ? key : PadFallbackKey;
    }

    // Local player holds the cultivator, menu closed, and the selected piece is the Yggdrasil transplant sapling.
    private static bool IsPlacingYggdrasil(Player player)
    {
        if (player.IsDead() || Hud.IsPieceSelectionVisible() || !player.TakeInput())
        {
            return false;
        }
        var tool = player.GetRightItem();
        if (tool == null || !CultivatorTiers.IsCultivator(tool))
        {
            return false;
        }
        var piece = player.GetSelectedPiece();
        if (piece == null)
        {
            return false;
        }
        _yggKind ??= PlantCatalog.ByKey("YggaShoot");
        if (_yggKind == null)
        {
            return false;
        }
        var sapling = TransplantContent.SaplingPrefab(_yggKind);
        return sapling != null && ReferenceEquals(piece.gameObject, sapling);
    }

    // "Plant 2 to 6 m from an Ancient Root": low end = sapling grow radius (root mesh closer = no room to grow), high
    // end = rules RootRange (placement need the root that near).
    private static string RootText(float growRadius, float range)
    {
        // Float compare per frame; string only when the rules range changed (grow radius fixed in the catalog).
        if (_rootText == null || !Mathf.Approximately(range, _rootRange))
        {
            _rootRange = range;
            _rootText = "Plant " + growRadius.ToString("0.#", CultureInfo.InvariantCulture) + " to "
                        + range.ToString("0.#", CultureInfo.InvariantCulture) + " m from an Ancient Root";
        }
        return _rootText;
    }
}
