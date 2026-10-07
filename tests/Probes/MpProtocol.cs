#if DEBUG
using System;
using System.Collections.Generic;
using System.Text;
using MC.Shared;

namespace MC.Core.ProbeShared;

// Me = words the two probes say in a multiplayer run (tools/Test-Multiplayer.ps1): client probe (Core.Probe.World) and
// server probe (Core.Probe.Server). Every rpc carry one ZPackage. Vanilla peer ignore unknown rpc. Compiled into both
// probes (same file), never into a mod.
internal static class MpProtocol
{
    internal const string Hello = "MC.Probe.Hello";         // client -> server: (string client mods)
    internal const string HelloAck = "MC.Probe.HelloAck";   // server -> client: (string scenario, string server mods)
    internal const string Step = "MC.Probe.Step";           // client -> server: (int id, string step, string arg)
    internal const string StepReply = "MC.Probe.StepReply"; // server -> client: (int id, bool ok, string detail)
    internal const string Quit = "MC.Probe.Quit";           // client -> server: () = save world, quit

    // Scenarios (tools/Test-Multiplayer.ps1 -Scenario). Modded = SelfTest.Modded.
    internal const string Modded = "modded";                // server and client: every MC mod
    internal const string VanillaServer = "vanilla-server"; // server: probe only; client: every MC mod
    internal const string VanillaClient = "vanilla-client"; // server: every MC mod (refuse players without them); client: probe only
    internal const string OpenServer = "open-server";       // like vanilla-client, but server AllowPlayersWithoutMod = true
    internal const string EachOff = "each-off";             // both: every MC mod; client turn each Both mod off in turn

    // Server probe's own steps (mods' steps come from SelfTest.RegisterServerStep).
    internal const string SetEnabledStep = "probe.set-enabled"; // arg "<guid>=on|off": turn that mod on/off on the server

    internal const string ServerSide = "Server";
    internal const string BothSide = "Both";
    internal const string ClientSide = "Client";

    // One MC mod as the other side see it.
    internal sealed class ModInfoLine
    {
        internal string Guid;
        internal string Side;
        internal string State;
        internal string Status;
    }

    // Every MC mod of this game (FeatureRegistry), probes left out: "guid|side|state|status" lines.
    internal static string DescribeMods()
    {
        var sb = new StringBuilder();
        foreach (var f in FeatureRegistry.All())
        {
            if (f.Guid.StartsWith("MC.Core.Probe", StringComparison.Ordinal))
            {
                continue;
            }
            sb.Append(f.Guid).Append('|').Append(f.Side).Append('|').Append(f.State).Append('|')
                .Append((f.Status ?? "").Replace('\n', ' ').Replace('|', '/')).Append('\n');
        }
        return sb.ToString();
    }

    internal static Dictionary<string, ModInfoLine> ParseMods(string text)
    {
        var result = new Dictionary<string, ModInfoLine>(StringComparer.Ordinal);
        foreach (var line in (text ?? "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('|');
            if (parts.Length < 3)
            {
                continue;
            }
            result[parts[0]] = new ModInfoLine { Guid = parts[0], Side = parts[1], State = parts[2], Status = parts.Length > 3 ? parts[3] : "" };
        }
        return result;
    }
}
#endif
