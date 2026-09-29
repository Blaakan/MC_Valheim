using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Farming.BreedingStarInheritanceMod.Patches;

// Me = network part (server settings for everyone, refuse players without mod). Only while Active, like every patch.
// Framework NetworkGate patch same methods under own Harmony id (handshake); all postfixes, no order needed.
//   OnNewConnection: server listen for settings request; client listen for settings (new session = forget old).
//   RPC_PeerInfo:    peer ready = handshake done. Server: start grace for join check. Client: listen (again) and ask
//                    server settings.
//   Update:          timers (join check deadline, settings push after change). Nothing to do = two bool reads.
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
                ServerSettings.RegisterServer(peer.m_rpc);
            }
            else
            {
                ServerSettings.RegisterClient(peer.m_rpc, forget: true);
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
                ServerSettings.RegisterClient(rpc, forget: false);
                ServerSettings.Request(rpc);
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
        if (!PlayerCheck.HasWork && !ServerSettings.PushPending)
        {
            return;
        }
        try
        {
            if (PlayerCheck.HasWork)
            {
                PlayerCheck.Update();
            }
            if (ServerSettings.PushPending)
            {
                ServerSettings.Update();
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("ZNet.Update postfix", e);
        }
    }
}
