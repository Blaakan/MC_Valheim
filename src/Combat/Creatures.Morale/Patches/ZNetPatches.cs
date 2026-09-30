using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.CreaturesMoraleMod.Patches;

// Me = network part (server rules for everyone, refuse players who cannot play by them, Rout rpc, world end). Only
// while Active, like every patch. Framework NetworkGate patch same methods under own Harmony id (handshake); all
// postfixes, no order needed.
//   Awake:           new ZNet = new ZRoutedRpc: register Rout on it; look for overlapping mods (all plugins loaded).
//   OnNewConnection: server listen for rules request; client listen for rules (new session = forget old).
//   RPC_PeerInfo:    peer ready = handshake done. Server: start grace for join check. Client: listen (again) and ask
//                    server rules.
//   Update:          timers (join check deadline, own rule edit settled, rules push after change, boss rank message
//                    after vanilla's boss death message). Nothing to do = three bool reads.
//   OnDestroy:       world end: own rule edit still waiting applied; forget creatures, standings, recent routs, boss
//                    fights, prefab tables, message baseline.
[HarmonyPatch(typeof(ZNet))]
internal static class ZNetPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(ZNet.Awake))]
    private static void Awake_Postfix()
    {
        try
        {
            Rout.EnsureRegistered();
            Compat.WarnOnce();
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
        if (!PlayerCheck.HasWork && !ServerRules.HasWork && !Standing.MessagePending)
        {
            return;
        }
        try
        {
            if (PlayerCheck.HasWork)
            {
                PlayerCheck.Update();
            }
            if (ServerRules.HasWork)
            {
                ServerRules.Update();
            }
            if (Standing.MessagePending)
            {
                Standing.UpdateMessage();
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("ZNet.Update postfix", e);
        }
    }

    // World end (also quit to menu). Game objects of the world go away: every memory keyed on them go too.
    [HarmonyPostfix]
    [HarmonyPatch(nameof(ZNet.OnDestroy))]
    private static void OnDestroy_Postfix()
    {
        try
        {
            ServerRules.FlushEdit();
            CreatureState.Clear();
            StandingCache.Clear();
            BossFights.Clear();
            BossLadder.Clear();
            PrefabTokens.Clear();
            Standing.Clear();
            Rout.Clear();
        }
        catch (Exception e)
        {
            PatchGuard.Report("ZNet.OnDestroy postfix", e);
        }
    }
}
