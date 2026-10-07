using MC.Shared;
#if DEBUG
using System;
using System.Collections;
using System.Globalization;
using MC.Core.ProbeShared;
using MC.Core.ProbeWorldMod;
using UnityEngine;
#endif

namespace MC.Core.ProbeServerMod;

// Me run on the dedicated server of a multiplayer run (env MC_MP_SERVER_DIR, set only on the server process):
//   wait world up -> register probe rpcs -> "[selftest] NOTE probe.server: ready ..." (script wait for it) -> answer:
//   Hello (scenario + server mods), Step (run mod's server step with timeout, reply ok + detail), Quit (save, quit).
// Rpcs re-registered if ZRoutedRpc instance change. Step lines go to the server log; script show them too.
internal static class ServerDriver
{
    internal const string T = "probe.server";

#if DEBUG
    private const string DirVar = "MC_MP_SERVER_DIR";
    private const string ScenarioVar = "MC_MP_SCENARIO";
    private const string TimeoutVar = "MC_SELFTEST_TIMEOUT";

    private static Plugin _plugin;
    private static Coroutine _run;
    private static ZRoutedRpc _rpc;
    private static string _scenario = "";
    private static float _stepTimeout = 120f;
#endif

    internal static void Begin(Plugin plugin)
    {
#if DEBUG
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(DirVar)))
        {
            Log.Info($"{DirVar} not set: server probe idle (it only runs under tools/Test-Multiplayer.ps1).");
            return;
        }
        _plugin = plugin;
        _scenario = (Environment.GetEnvironmentVariable(ScenarioVar) ?? "").Trim();
        var timeout = Environment.GetEnvironmentVariable(TimeoutVar);
        _stepTimeout = float.TryParse(timeout, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) && t > 0f ? t * 0.7f : 80f;
        AppDomain.CurrentDomain.SetData(SelfTest.ScenarioSlot, _scenario);
        Log.Info($"Server probe on: scenario '{_scenario}', step timeout {_stepTimeout:F0} s.");
        if (_run == null)
        {
            _run = plugin.StartCoroutine(Run());
        }
#else
        Log.Info("Release build: server probe does nothing (self tests exist in Debug builds only).");
#endif
    }

    internal static void End(Plugin plugin)
    {
#if DEBUG
        if (_run != null)
        {
            plugin.StopCoroutine(_run);
            _run = null;
            Log.Warning("Server probe turned off: it no longer answers the client probe.");
        }
#endif
    }

#if DEBUG
    private static IEnumerator Run()
    {
        var announced = false;
        while (true)
        {
            var net = ZNet.instance;
            var ready = net != null && net.IsServer() && ZRoutedRpc.instance != null && ZNetScene.instance != null
                        && ZoneSystem.instance != null;
            if (ready && !ReferenceEquals(ZRoutedRpc.instance, _rpc))
            {
                _rpc = ZRoutedRpc.instance;
                _rpc.Register<ZPackage>(MpProtocol.Hello, OnHello);
                _rpc.Register<ZPackage>(MpProtocol.Step, OnStep);
                _rpc.Register<ZPackage>(MpProtocol.Quit, OnQuit);
                if (!announced)
                {
                    announced = true;
                    // Script wait for this exact line before it start the client.
                    SelfTest.Note(T, $"ready: dedicated {net.IsDedicated()}, world '{ZNet.World?.m_name}', scenario '{_scenario}', "
                                     + $"{SelfTest.GetServerSteps().Count} server step(s), mods: {MpProtocol.DescribeMods().Replace('\n', ';')}");
                }
            }
            yield return new WaitForSecondsRealtime(ready ? 1f : 0.25f);
        }
    }

    private static void OnHello(long sender, ZPackage pkg)
    {
        try
        {
            var clientMods = MpProtocol.ParseMods(pkg.ReadString());
            SelfTest.Note(T, $"hello from peer {sender}: {clientMods.Count} client MC mod(s)");
            var ack = new ZPackage();
            ack.Write(_scenario);
            ack.Write(MpProtocol.DescribeMods());
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, MpProtocol.HelloAck, ack);
        }
        catch (Exception e)
        {
            Log.Error($"{MpProtocol.Hello} failed: {e}");
        }
    }

    private static void OnStep(long sender, ZPackage pkg)
    {
        try
        {
            var id = pkg.ReadInt();
            var step = pkg.ReadString();
            var arg = pkg.ReadString();
            _plugin.StartCoroutine(RunStep(sender, id, step, arg));
        }
        catch (Exception e)
        {
            Log.Error($"{MpProtocol.Step} failed: {e}");
        }
    }

    private static IEnumerator RunStep(long sender, int id, string step, string arg)
    {
        var ok = false;
        string detail;
        Func<string, object[], IEnumerator> factory = null;
        if (step == MpProtocol.SetEnabledStep)
        {
            factory = SetEnabled;
        }
        else
        {
            var steps = SelfTest.GetServerSteps();
            lock (steps)
            {
                steps.TryGetValue(step, out factory);
            }
        }
        if (factory == null)
        {
            detail = $"the server has no step '{step}' (its mod is missing or not active on the server)";
        }
        else
        {
            var answer = new object[] { null, null };
            var result = new RunResult();
            IEnumerator root = null;
            try
            {
                root = factory(arg, answer);
            }
            catch (Exception e)
            {
                result.Error = e;
            }
            if (root != null)
            {
                yield return SafeRunner.Run(root, _stepTimeout, result);
            }
            if (result.TimedOut)
            {
                detail = $"server step timed out after {result.Seconds:F0} s";
            }
            else if (result.Error != null)
            {
                detail = $"server step threw {result.Error}";
            }
            else if (!(answer[0] is bool answered))
            {
                detail = "server step ended without an answer";
            }
            else
            {
                ok = answered;
                detail = answer[1] as string ?? "";
            }
        }
        SelfTest.Note(T, $"step {step}('{arg}') for peer {sender}: {(ok ? "ok" : "NOT ok")}: {detail}");
        var reply = new ZPackage();
        reply.Write(id);
        reply.Write(ok);
        reply.Write(detail);
        if (ZRoutedRpc.instance != null)
        {
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, MpProtocol.StepReply, reply);
        }
    }

    // Built-in step "<guid>=on|off": like the server owner ticking the mod in MC Mods (Enabled written to the server's
    // own test config). Answer once the mod's state follow (Active / Disabled) or 5 s pass.
    private static IEnumerator SetEnabled(string arg, object[] reply)
    {
        var eq = (arg ?? "").LastIndexOf('=');
        var guid = eq > 0 ? arg.Substring(0, eq) : "";
        var on = eq > 0 && arg.Substring(eq + 1) == "on";
        var view = FeatureRegistry.Find(guid);
        if (view == null || view.Value.Enabled == null)
        {
            SelfTest.Answer(reply, false, $"no MC mod '{guid}' on the server");
            yield break;
        }
        var f = view.Value;
        f.Enabled.Value = on;
        var want = on ? nameof(ModState.Active) : nameof(ModState.Disabled);
        var end = Time.realtimeSinceStartup + 5f;
        while (f.State != want && Time.realtimeSinceStartup < end)
        {
            yield return null;
        }
        SelfTest.Answer(reply, f.State == want, $"{guid} is {f.State} on the server: {f.Status}");
    }

    private static void OnQuit(long sender, ZPackage pkg)
    {
        SelfTest.Note(T, $"quit asked by peer {sender}: saving the world and quitting");
        _plugin.StartCoroutine(Quit());
    }

    private static IEnumerator Quit()
    {
        // Client log out first (its own quit), then me save and go.
        yield return new WaitForSecondsRealtime(3f);
        try
        {
            if (ZNet.instance != null)
            {
                ZNet.instance.Save(sync: true);
            }
        }
        catch (Exception e)
        {
            Log.Error($"save before quit failed: {e}");
        }
        SelfTest.Note(T, "DONE");
        yield return new WaitForSecondsRealtime(1f);
        Application.Quit();
    }
#endif
}
