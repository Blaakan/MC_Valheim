using System;
using HarmonyLib;
using MC.Shared;

namespace MC.UX.CheatedTagRemoverMod.Patches;

// Me = put my rpc handlers on each new connection (ServerLink). Only while Active, like every feature patch.
// Framework NetworkGate patch same methods under own Harmony id (handshake); all postfixes, no order needed.
//   OnNewConnection: server listen for a request on that player's connection; client listen for the server.
//   RPC_PeerInfo:    again when the peer is ready: me turned on while connecting = first postfix was not there.
// Connections made while me was off: ServerLink.Start take them.
[HarmonyPatch(typeof(ZNet))]
internal static class ZNetPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(ZNet.OnNewConnection))]
    private static void OnNewConnection_Postfix(ZNet __instance, ZNetPeer peer)
    {
        try
        {
            if (peer == null || peer.m_rpc == null)
            {
                return;
            }
            ServerLink.Register(peer.m_rpc, __instance.IsServer());
        }
        catch (Exception e)
        {
            PatchGuard.Report("ZNet.OnNewConnection postfix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(ZNet.RPC_PeerInfo))]
    private static void RPC_PeerInfo_Postfix(ZNet __instance, ZRpc rpc)
    {
        try
        {
            if (rpc == null)
            {
                return;
            }
            ServerLink.Register(rpc, __instance.IsServer());
        }
        catch (Exception e)
        {
            PatchGuard.Report("ZNet.RPC_PeerInfo postfix", e);
        }
    }
}
