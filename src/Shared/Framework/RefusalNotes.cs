using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace MC.Shared;

// Me = why a server refused this game, said on the connection error screen. Vanilla only show "Incompatible version"
// (ErrorVersion carry no mod name), and Jotunn's compatibility window list only Jotunn mods. Me add a "MC mods:" block
// to the vanilla error text; Jotunn copy that text into its window (its ShowConnectError postfix run at priority 0,
// mine at Normal, so mine first).
// Two sources, kept per connection:
//   known: NetworkGate (client) saw a server copy whose network version cannot talk to ours, or its own copy is off
//          while the server's is on (servers refuse such players);
//   sent:  the server's NetworkGate.Refuse send "MC.Framework.Refused" with one line just before the Error rpc. Sent
//          line win over known line of same mod. Needs one MC mod on this game to listen; vanilla ignore unknown rpc.
// Every MC mod dll carry own copy of me: notes live in one AppDomain slot (BCL types only, like FeatureRegistry), and
// only the first copy patch ShowConnectError (slot flag). Never rename slot keys; new keys ok.
internal static class RefusalNotes
{
    internal const string Rpc = "MC.Framework.Refused";
    internal const string Header = "MC mods:";
    private const string Slot = "MC.Framework.RefusalNotes.v1";

    private static Dictionary<string, object> Root
    {
        get
        {
            var domain = AppDomain.CurrentDomain;
            if (domain.GetData(Slot) is Dictionary<string, object> root)
            {
                return root;
            }
            root = new Dictionary<string, object>
            {
                ["conn"] = 0,
                ["known"] = new Dictionary<string, string>(),
                ["sent"] = new Dictionary<string, string>(),
                ["patched"] = false,
            };
            domain.SetData(Slot, root);
            return root;
        }
    }

    // Client: new connection to a server. Same ZRpc from every copy: only the first call of a connection clear.
    internal static void NewConnection(ZRpc rpc)
    {
        var id = rpc != null ? RuntimeHelpers.GetHashCode(rpc) : 0;
        var root = Root;
        lock (root)
        {
            if (root["conn"] is int old && old == id)
            {
                return;
            }
            root["conn"] = id;
            ((Dictionary<string, string>)root["known"]).Clear();
            ((Dictionary<string, string>)root["sent"]).Clear();
        }
    }

#if DEBUG
    // Self test (world probe probe.refusal, single player): start from no notes.
    internal static void ResetForTest()
    {
        var root = Root;
        lock (root)
        {
            root["conn"] = -1;
            ((Dictionary<string, string>)root["known"]).Clear();
            ((Dictionary<string, string>)root["sent"]).Clear();
        }
    }
#endif

    // Client: what this game knows about one mod (null = nothing wrong: line removed).
    internal static void SetKnown(string mod, string line) => Set("known", mod, line);

    // Client rpc handler (registered by every copy on the server connection): server's line for one mod.
    // Line = "<mod name>\t<text>".
    internal static void OnServerLine(ZRpc rpc, string line)
    {
        try
        {
            var tab = (line ?? "").IndexOf('\t');
            if (tab <= 0)
            {
                return;
            }
            Set("sent", line.Substring(0, tab), line.Substring(tab + 1));
        }
        catch (Exception e)
        {
            PatchGuard.Report("RefusalNotes.OnServerLine", e);
        }
    }

    private static void Set(string kind, string mod, string line)
    {
        var root = Root;
        lock (root)
        {
            var notes = (Dictionary<string, string>)root[kind];
            if (string.IsNullOrEmpty(line))
            {
                notes.Remove(mod);
            }
            else
            {
                notes[mod] = line;
            }
        }
    }

    // "- <mod>: <text>" per mod, sorted by mod name; server's line when it sent one.
    internal static List<string> Lines()
    {
        var root = Root;
        lock (root)
        {
            var known = (Dictionary<string, string>)root["known"];
            var sent = (Dictionary<string, string>)root["sent"];
            var mods = new SortedSet<string>(known.Keys, StringComparer.OrdinalIgnoreCase);
            mods.UnionWith(sent.Keys);
            var lines = new List<string>();
            foreach (var mod in mods)
            {
                lines.Add($"- {mod}: {(sent.TryGetValue(mod, out var s) ? s : known[mod])}");
            }
            return lines;
        }
    }

    // Error text with the "MC mods:" block, or the text as is (no notes, or block already there).
    internal static string AddTo(string text)
    {
        text ??= "";
        var lines = Lines();
        if (lines.Count == 0 || text.Contains(Header))
        {
            return text;
        }
        return text.TrimEnd() + "\n\n" + Header + "\n" + string.Join("\n", lines.ToArray());
    }

    // NetworkGate.Install of a Both mod: first copy patch the error screen, for the whole game session.
    internal static void InstallOnce(Harmony h)
    {
        var root = Root;
        lock (root)
        {
            if (root["patched"] is bool done && done)
            {
                return;
            }
            var target = AccessTools.Method(typeof(FejdStartup), nameof(FejdStartup.ShowConnectError))
                         ?? throw new MissingMethodException(nameof(FejdStartup), nameof(FejdStartup.ShowConnectError));
            h.Patch(target, postfix: new HarmonyMethod(typeof(RefusalNotes), nameof(ShowConnectErrorPostfix)) { priority = Priority.Normal });
            root["patched"] = true;
        }
    }

    private static void ShowConnectErrorPostfix(FejdStartup __instance, ZNet.ConnectionStatus statusOverride)
    {
        try
        {
            var status = statusOverride == ZNet.ConnectionStatus.None ? ZNet.GetConnectionStatus() : statusOverride;
            if (status != ZNet.ConnectionStatus.ErrorVersion || __instance == null || __instance.m_connectionFailedError == null)
            {
                return;
            }
            var before = __instance.m_connectionFailedError.text;
            var after = AddTo(before);
            if (ReferenceEquals(after, before) || after == before)
            {
                return;
            }
            __instance.m_connectionFailedError.text = after;
            Log.Info("MC mods on the error screen, why the server refused this game: " + string.Join(" ", Lines().ToArray()));
        }
        catch (Exception e)
        {
            PatchGuard.Report("FejdStartup.ShowConnectError postfix (MC refusal notes)", e);
        }
    }
}
