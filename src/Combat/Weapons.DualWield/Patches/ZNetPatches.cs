using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.WeaponsDualWieldMod.Patches;

// Me = network part (server rules for everyone, refuse players whose game no run me). Only while Active, like every
// patch. Copy of MC Forge Idol Upgrades' one. Framework NetworkGate patch same methods under own Harmony id
// (handshake); all postfixes, no order needed. Re-check after a player turn me on/off = PlayerCheck listen to
// NetworkGate.PeerStateChanged, no patch here.
//   OnNewConnection: server listen for rules request; client listen for rules (new session = forget old).
//   RPC_PeerInfo:    peer ready = handshake done. Server: start grace for join check. Client: listen (again) and ask
//                    server rules.
//   Update:          timers (join check deadline, rules push after change). Nothing to do = two bool reads.
[HarmonyPatch(typeof(ZNet))]
internal static class ZNetPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(ZNet.OnNewConnection))]
    private static void OnNewConnection_Postfix(ZNet __instance, ZNetPeer peer)
    {
        try
        {
            if (peer?.m_rpc == null)
            {
                return;
            }
            if (__instance.IsServer())
            {
                ServerRules.RegisterServer(peer.m_rpc);
            }
            else
            {
                ServerRules.RegisterClient(peer.m_rpc, forget: true);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("ZNet.OnNewConnection postfix", e);
        }
    }

    // Vanilla refuse (version, password, full, ban...) = return before m_uid set: peer not ready, me do nothing.
    [HarmonyPostfix]
    [HarmonyPatch(nameof(ZNet.RPC_PeerInfo))]
    private static void RPC_PeerInfo_Postfix(ZNet __instance, ZRpc rpc)
    {
        try
        {
            var peer = rpc != null ? __instance.GetPeer(rpc) : null;
            if (peer == null || !peer.IsReady())
            {
                return;
            }
            if (__instance.IsServer())
            {
                PlayerCheck.Schedule(peer);
            }
            else
            {
                // Me listen here too: me turned on while connecting = OnNewConnection postfix not there then, and
                // Start find no server peer yet (GetServerPeer null until Connected). Register replace: safe twice.
                ServerRules.RegisterClient(rpc, forget: false);
                ServerRules.Request(rpc);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("ZNet.RPC_PeerInfo postfix", e);
        }
    }

    // Every frame on every game: cheap exit first.
    [HarmonyPostfix]
    [HarmonyPatch(nameof(ZNet.Update))]
    private static void Update_Postfix()
    {
        if (!PlayerCheck.HasWork && !ServerRules.PushPending)
        {
            return;
        }
        try
        {
            if (PlayerCheck.HasWork)
            {
                PlayerCheck.Update();
            }
            if (ServerRules.PushPending)
            {
                ServerRules.Update();
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("ZNet.Update postfix", e);
        }
    }
}
