using System;
using MC.Shared;

namespace MC.Exploration.DeepNorthAwakeningMod;

// Me = server heart beat (design 2.1, 2.6). ZNet.Update postfix call me every frame on every game; only the server
// (single player, host, dedicated) do work: destroy delegate on ZDOMan (stones, dead area Jotun), stone effects,
// held-back invasion requests, old-world detection, held areas after Kall.
// Delegate added once per ZDOMan (new world = new ZDOMan), removed when off or not server.
internal static class ServerWorld
{
    private static ZDOMan _hookedOn;

    internal static void Update()
    {
        var net = ZNet.instance;
        if (net == null || !net.IsServer())
        {
            Unhook();
            return;
        }
        Hook();
        Stones.Update();
        HeldCells.ServerUpdate();
    }

    private static void Hook()
    {
        var zdos = ZDOMan.instance;
        if (zdos == null || ReferenceEquals(zdos, _hookedOn))
        {
            return;
        }
        Unhook();
        zdos.m_onZDODestroyed += OnZdoDestroyed;
        _hookedOn = zdos;
    }

    internal static void Unhook()
    {
        if (_hookedOn == null)
        {
            return;
        }
        _hookedOn.m_onZDODestroyed -= OnZdoDestroyed;
        _hookedOn = null;
    }

    // Vanilla call this inside its destroy handling: never throw back.
    private static void OnZdoDestroyed(ZDO zdo)
    {
        try
        {
            if (zdo == null)
            {
                return;
            }
            Stones.OnDestroyed(zdo);
            HeldCells.OnDestroyed(zdo);
        }
        catch (Exception e)
        {
            PatchGuard.Report("ServerWorld.OnZdoDestroyed", e);
        }
    }
}
