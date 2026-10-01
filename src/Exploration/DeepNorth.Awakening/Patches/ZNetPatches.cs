using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.DeepNorthAwakeningMod.Patches;

// Me = network part and world heart beat. Only while Active, like every feature patch. Framework NetworkGate patch same
// methods under own Harmony id (handshake); all postfixes, no order needed. Copy of Swim Dive's, plus:
//   Awake:           new ZNet = new ZRoutedRpc: register the Held rpc on it.
//   OnNewConnection: server listen for rules request; client listen for rules (new session = forget old).
//   RPC_PeerInfo:    peer ready = handshake done. Server: start grace for join check. Client: listen (again) and ask
//                    server rules (the server answer with the rules, and after Kall with the held areas).
//   Update:          timers (join check, rules push), world keys twice a second, server work (ServerWorld).
//   OnDestroy:       world end: forget world state.
[HarmonyPatch(typeof(ZNet))]
internal static class ZNetPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(ZNet.Awake))]
    private static void Awake_Postfix()
    {
        try
        {
            HeldCells.EnsureRegistered();
        }
        catch (Exception e)
        {
            PatchGuard.Report("ZNet.Awake postfix", e);
        }
    }

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
                // Me turned on while connecting = OnNewConnection postfix not there then. Register replace: safe twice.
                ServerRules.RegisterClient(rpc, forget: false);
                ServerRules.Request(rpc);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("ZNet.RPC_PeerInfo postfix", e);
        }
    }

    // Every frame on every game: cheap clock checks first.
    [HarmonyPostfix]
    [HarmonyPatch(nameof(ZNet.Update))]
    private static void Update_Postfix()
    {
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
            WorldState.RefreshIfDue();
            ServerWorld.Update();
        }
        catch (Exception e)
        {
            PatchGuard.Report("ZNet.Update postfix", e);
        }
    }

    // World end (also quit to menu): every memory of this world go.
    [HarmonyPostfix]
    [HarmonyPatch(nameof(ZNet.OnDestroy))]
    private static void OnDestroy_Postfix()
    {
        try
        {
            Plugin.ForgetWorld();
        }
        catch (Exception e)
        {
            PatchGuard.Report("ZNet.OnDestroy postfix", e);
        }
    }
}
