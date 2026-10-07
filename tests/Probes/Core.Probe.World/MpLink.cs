#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using MC.Core.ProbeShared;
using MC.Shared;
using UnityEngine;

namespace MC.Core.ProbeWorldMod;

// Client side of multiplayer run. Me register probe rpcs on this session's ZRoutedRpc, say hello to server probe,
// and give mods SelfTest.CallServer (delegate in SelfTest.ServerCallSlot): send step, wait its reply by id.
internal static class MpLink
{
    private static ZRoutedRpc _rpc;
    private static int _nextId;
    private static readonly Dictionary<int, object[]> Waiting = new Dictionary<int, object[]>();

    internal static bool HaveAck { get; private set; }
    internal static string ServerScenario { get; private set; } = "";
    internal static Dictionary<string, MpProtocol.ModInfoLine> ServerMods { get; private set; } =
        new Dictionary<string, MpProtocol.ModInfoLine>();

    // Server step must answer before this (test timeout leave room for test to report).
    private static float CallTimeout => Mathf.Max(10f, ProbeSettings.TestTimeout * 0.75f);

    internal static void Install()
    {
        var rpc = ZRoutedRpc.instance;
        if (rpc == null)
        {
            throw new InvalidOperationException("no ZRoutedRpc: not connected to a server");
        }
        if (!ReferenceEquals(rpc, _rpc))
        {
            rpc.Register<ZPackage>(MpProtocol.HelloAck, OnHelloAck);
            rpc.Register<ZPackage>(MpProtocol.StepReply, OnStepReply);
            _rpc = rpc;
            HaveAck = false;
        }
        AppDomain.CurrentDomain.SetData(SelfTest.ServerCallSlot, (Func<string, string, object[], IEnumerator>)Call);
    }

    internal static void Uninstall()
    {
        AppDomain.CurrentDomain.SetData(SelfTest.ServerCallSlot, null);
    }

    // Hello to server probe, wait ack (server scenario + its mods).
    internal static IEnumerator Hello(float timeout)
    {
        HaveAck = false;
        var pkg = new ZPackage();
        pkg.Write(MpProtocol.DescribeMods());
        _rpc.InvokeRoutedRPC(MpProtocol.Hello, pkg);
        var end = Time.realtimeSinceStartup + timeout;
        while (!HaveAck && Time.realtimeSinceStartup < end)
        {
            yield return null;
        }
    }

    // Server probe: save world, quit. Fire and forget (client quit right after).
    internal static void SendQuit()
    {
        var rpc = ZRoutedRpc.instance;
        if (rpc != null && ReferenceEquals(rpc, _rpc) && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected)
        {
            rpc.InvokeRoutedRPC(MpProtocol.Quit, new ZPackage());
        }
    }

    private static IEnumerator Call(string step, string arg, object[] reply)
    {
        var rpc = ZRoutedRpc.instance;
        if (rpc == null || !ReferenceEquals(rpc, _rpc))
        {
            reply[2] = "not connected to the server";
            yield break;
        }
        var id = ++_nextId;
        Waiting[id] = reply;
        try
        {
            var pkg = new ZPackage();
            pkg.Write(id);
            pkg.Write(step ?? "");
            pkg.Write(arg ?? "");
            rpc.InvokeRoutedRPC(MpProtocol.Step, pkg);
            var end = Time.realtimeSinceStartup + CallTimeout;
            while (!(reply[0] is bool answered && answered))
            {
                if (Time.realtimeSinceStartup > end)
                {
                    reply[2] = $"the server did not answer step '{step}' within {CallTimeout:F0} s";
                    yield break;
                }
                yield return null;
            }
        }
        finally
        {
            Waiting.Remove(id);
        }
    }

    private static void OnHelloAck(long sender, ZPackage pkg)
    {
        try
        {
            ServerScenario = pkg.ReadString();
            ServerMods = MpProtocol.ParseMods(pkg.ReadString());
            HaveAck = true;
        }
        catch (Exception e)
        {
            Log.Error($"bad {MpProtocol.HelloAck}: {e}");
        }
    }

    private static void OnStepReply(long sender, ZPackage pkg)
    {
        try
        {
            var id = pkg.ReadInt();
            var ok = pkg.ReadBool();
            var detail = pkg.ReadString();
            if (Waiting.TryGetValue(id, out var reply))
            {
                reply[1] = ok;
                reply[2] = detail;
                reply[0] = true;
            }
        }
        catch (Exception e)
        {
            Log.Error($"bad {MpProtocol.StepReply}: {e}");
        }
    }
}
#endif
