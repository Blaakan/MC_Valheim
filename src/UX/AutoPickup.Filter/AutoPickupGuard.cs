using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MC.Shared;

namespace MC.UX.AutoPickupFilterMod;

// Me check the one thing whole mod stand on: vanilla Player.AutoPickup read ItemDrop.m_autoPickup, then call
// ItemDrop.IsPiece, then (later) ItemDrop.RequestOwn. Our IsPiece answer skip a drop before ownership request.
// Game update change that order = filter would silently do nothing: Verify throw, framework show an error instead.
// Other mods: a transpiler or a skipping prefix on AutoPickup may drop the IsPiece call: me warn once in log.
internal static class AutoPickupGuard
{
    internal static bool ForeignCheckDone;

    private static MethodInfo Target => AccessTools.Method(typeof(Player), nameof(Player.AutoPickup), new[] { typeof(float) });

    // OnActivated call me. Read original (unpatched) IL, one time per activation.
    internal static void Verify()
    {
        var method = Target;
        if (method == null)
        {
            throw new InvalidOperationException("Player.AutoPickup changed: method not found.");
        }
        var il = PatchProcessor.GetOriginalInstructions(method);
        var autoPickupField = AccessTools.Field(typeof(ItemDrop), nameof(ItemDrop.m_autoPickup));
        var isPiece = AccessTools.Method(typeof(ItemDrop), nameof(ItemDrop.IsPiece));
        var requestOwn = AccessTools.Method(typeof(ItemDrop), nameof(ItemDrop.RequestOwn));
        if (il == null || autoPickupField == null || isPiece == null || requestOwn == null)
        {
            throw new InvalidOperationException("Player.AutoPickup changed: ItemDrop members or method body not found.");
        }

        int readFlag = -1, callPiece = -1, callOwn = -1;
        for (var i = 0; i < il.Count; i++)
        {
            var ins = il[i];
            if (readFlag < 0)
            {
                if (ins.LoadsField(autoPickupField))
                {
                    readFlag = i;
                }
            }
            else if (callPiece < 0)
            {
                if (ins.Calls(isPiece))
                {
                    callPiece = i;
                }
            }
            else if (callOwn < 0 && ins.Calls(requestOwn))
            {
                callOwn = i;
                break;
            }
        }
        if (callOwn < 0)
        {
            var missing = readFlag < 0 ? "read of ItemDrop.m_autoPickup"
                : callPiece < 0 ? "call of ItemDrop.IsPiece after the m_autoPickup check"
                : "call of ItemDrop.RequestOwn after IsPiece";
            throw new InvalidOperationException($"Player.AutoPickup changed: {missing} not found. "
                                                + "The loot filter cannot hook auto pickup safely.");
        }
        Log.Debug($"Player.AutoPickup gate checked: m_autoPickup at IL {readFlag}, IsPiece at {callPiece}, RequestOwn at {callOwn}.");
    }

    // First AutoPickup call after activation (all mods loaded by then). Warn only, never act.
    internal static void WarnForeignPatches()
    {
        ForeignCheckDone = true;
        var method = Target;
        if (method == null)
        {
            return;
        }
        var info = Harmony.GetPatchInfo(method);
        if (info == null)
        {
            return;
        }
        var owners = new List<string>();
        foreach (var t in info.Transpilers)
        {
            if (t.owner != ModInfo.Guid && !owners.Contains(t.owner))
            {
                owners.Add(t.owner);
            }
        }
        foreach (var p in info.Prefixes)
        {
            // Bool prefix can skip vanilla loop (a copy of it may not call IsPiece). Void prefix cannot.
            if (p.owner != ModInfo.Guid && p.PatchMethod != null && p.PatchMethod.ReturnType == typeof(bool)
                && !owners.Contains(p.owner))
            {
                owners.Add(p.owner);
            }
        }
        if (owners.Count > 0)
        {
            Log.Warning($"Another mod changes Player.AutoPickup ({string.Join(", ", owners)}). If auto pickup ignores the "
                        + $"{ModInfo.Name}, that mod replaces the vanilla item checks.");
        }
    }
}
