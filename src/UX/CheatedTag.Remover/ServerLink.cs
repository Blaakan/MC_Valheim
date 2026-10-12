using System;
using System.Collections;
using MC.Shared;
using UnityEngine;

namespace MC.UX.CheatedTagRemoverMod;

// Me = talk between a player's game and the server, both with me. Peer rpc (one connection, nobody relay it), one
// string each, first field = layout "1" (other layout = other version: say so, do nothing):
//   client -> server  "<guid>.Request"  "1|clear" / "1|scan"   Server check the admin list by the connection's own
//                                                              id (same test as vanilla kick / save), then run
//                                                              SweepJob. Not vanilla remote command: that one has no
//                                                              way back to the caller and die silent without me there.
//   server -> client  "<guid>.Local"    "1|clear"              To each player with me, before the world pass: clean
//                                                              own carried items (only that game can) and objects it
//                                                              own (owner write always win; open chest of its own).
//   server -> client  "<guid>.Reply"    "1|<lines>"            Answer for the admin who asked.
// Vanilla peer ignore unknown rpc. Handlers stay on the connection after me turn off: each one check _active first.
internal static class ServerLink
{
    internal const string RequestRpc = ModInfo.Guid + ".Request";
    internal const string LocalRpc = ModInfo.Guid + ".Local";
    internal const string ReplyRpc = ModInfo.Guid + ".Reply";
    internal const string Layout = "1";
    internal const string ClearWord = "clear";
    internal const string ScanWord = "scan";

    // Client: seconds to wait for the server's first answer before me say nobody answered.
    internal const float AnswerWait = 10f;
    // Client asked to clean while it has no character in the world yet: seconds it wait for one.
    internal const float SpawnWait = 120f;

#if DEBUG
    // Self test: this game act like a player without me when the server say "clean your side".
    internal static bool TestIgnoreLocal { get; set; }
#endif

    private static bool _active;
    private static int _asked;    // client: number of the last request sent
    private static int _answered;  // client: number of the last request that got an answer

    // OnActivated. Listen on connections made while me was off.
    internal static void Start()
    {
        _active = true;
        var net = ZNet.instance;
        if (net == null)
        {
            return;
        }
        if (net.IsServer())
        {
            foreach (var peer in net.GetPeers())
            {
                if (peer != null && peer.m_rpc != null)
                {
                    Register(peer.m_rpc, true);
                }
            }
            return;
        }
        var server = net.GetServerPeer();
        if (server != null && server.m_rpc != null)
        {
            Register(server.m_rpc, false);
        }
    }

    internal static void Stop()
    {
        _active = false;
        _answered = _asked;
    }

    // Register replace same name: safe twice.
    internal static void Register(ZRpc rpc, bool server)
    {
        if (server)
        {
            rpc.Register<string>(RequestRpc, OnRequest);
        }
        else
        {
            rpc.Register<string>(LocalRpc, OnLocal);
            rpc.Register<string>(ReplyRpc, OnReply);
        }
    }

    // Client: ask the server for the world pass. False = no connection to send on.
    internal static bool Ask(bool write)
    {
        var net = ZNet.instance;
        var rpc = net != null ? net.GetServerRPC() : null;
        if (rpc == null || !rpc.IsConnected())
        {
            return false;
        }
        Register(rpc, false);
        var number = ++_asked;
        rpc.Invoke(RequestRpc, Layout + "|" + (write ? ClearWord : ScanWord));
        var plugin = Plugin.Instance;
        if (plugin != null)
        {
            plugin.StartCoroutine(WaitAnswer(number));
        }
        return true;
    }

    private static IEnumerator WaitAnswer(int number)
    {
        yield return new WaitForSecondsRealtime(AnswerWait);
        if (_active && _answered < number && ZNet.instance != null)
        {
            _answered = number;
            Output.Say("The server did not answer. It may not have " + ModInfo.Name + ", or has it turned off.");
        }
    }

    // Server: ask every ready player with a matching copy of me to clean its own side. Return how many asked.
    internal static int AskPlayers(out int without)
    {
        without = 0;
        var asked = 0;
        var net = ZNet.instance;
        if (net == null || !net.IsServer())
        {
            return 0;
        }
        foreach (var peer in net.GetPeers())
        {
            if (peer == null || peer.m_rpc == null || !peer.IsReady())
            {
                continue;
            }
            if (NetworkGate.PeerCompatible(peer))
            {
                peer.m_rpc.Invoke(LocalRpc, Layout + "|" + ClearWord);
                asked++;
            }
            else
            {
                without++;
            }
        }
        return asked;
    }

    // Server: answer the admin who asked (may have left meanwhile).
    internal static void Reply(long uid, string text)
    {
        var net = ZNet.instance;
        var peer = net != null ? net.GetPeer(uid) : null;
        if (peer != null && peer.m_rpc != null)
        {
            Reply(peer.m_rpc, text);
        }
    }

    private static void Reply(ZRpc rpc, string text)
    {
        if (rpc.IsConnected() && !string.IsNullOrEmpty(text))
        {
            rpc.Invoke(ReplyRpc, Layout + "|" + text);
        }
    }

    private static void OnRequest(ZRpc rpc, string payload)
    {
        try
        {
            var net = ZNet.instance;
            if (!_active || net == null || !net.IsServer() || rpc == null)
            {
                return;
            }
            var peer = net.GetPeer(rpc);
            if (peer == null || !peer.IsReady())
            {
                return;
            }
            var host = peer.m_socket != null ? peer.m_socket.GetHostName() : "";
            var who = peer.m_playerName + " (" + host + ")";
            var word = Word(payload);
            if (word != ClearWord && word != ScanWord)
            {
                Log.Info($"{who} sent a request this version does not understand; nothing done.");
                Reply(rpc, "The server has another version of " + ModInfo.Name + ". Use matching versions.");
                return;
            }
            if (!net.IsAdmin(host))
            {
                Log.Info($"{who} asked for the cheated tags of the world but is not an admin; refused.");
                Reply(rpc, "Only an admin can do this for the whole world: your ID must be in the server's adminlist.txt.");
                return;
            }
            var write = word == ClearWord;
            if (!SweepJob.Start(write, peer.m_uid))
            {
                Reply(rpc, "The server is already working on a request. Try again in a moment.");
                return;
            }
            Log.Info($"{who} asked the server to {(write ? "remove" : "count")} the cheated tags of the world.");
            if (write)
            {
                Reply(rpc, "The server is removing the cheated tags of the whole world. The result follows in a few seconds.");
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("ServerLink.OnRequest", e);
        }
    }

    // Client: server say "clean your side now". Only the server can send on this connection.
    private static void OnLocal(ZRpc rpc, string payload)
    {
        try
        {
            if (!_active || ZNet.instance == null || ZNet.instance.IsServer() || Word(payload) != ClearWord)
            {
                return;
            }
#if DEBUG
            if (TestIgnoreLocal)
            {
                return;
            }
#endif
            var counts = new SweepCounts();
            LiveSweep.Loaded();
            WorldSweep.Run(SweepScope.Owned, true, counts);
            if (Player.m_localPlayer != null)
            {
                LiveSweep.Carried(true, counts);
            }
            else
            {
                // Joined, but no character in the world yet (still loading, or dead): its items come with it.
                CleanCarriedWhenSpawned(ZNet.instance);
            }
            TellOwnSide(counts);
        }
        catch (Exception e)
        {
            PatchGuard.Report("ServerLink.OnLocal", e);
        }
    }

    // Game with no character in the world yet: clean what it carries once it is there (host of a run too).
    internal static void CleanCarriedWhenSpawned(ZNet net)
    {
        try
        {
            var plugin = Plugin.Instance;
            if (_active && plugin != null && net != null)
            {
                plugin.StartCoroutine(CarriedWhenSpawned(net));
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("ServerLink.CleanCarriedWhenSpawned", e);
        }
    }

    // What this game cleaned on its own side: a line where the player typed last (or the console), and a small
    // message when carried items were part of it.
    internal static void TellOwnSide(SweepCounts counts)
    {
        var text = Report.Helped(counts);
        if (text.Length == 0)
        {
            return;
        }
        Output.Say(text);
        var player = Player.m_localPlayer;
        if (counts.CarriedItems > 0 && player != null)
        {
            player.Message(MessageHud.MessageType.TopLeft, "The cheated tag was removed from the items you carry.");
        }
    }

    private static IEnumerator CarriedWhenSpawned(ZNet net)
    {
        var end = Time.realtimeSinceStartup + SpawnWait;
        while (Player.m_localPlayer == null)
        {
            if (!_active || net == null || !ReferenceEquals(net, ZNet.instance) || Time.realtimeSinceStartup > end)
            {
                yield break;
            }
            yield return null;
        }
        try
        {
            var counts = new SweepCounts();
            LiveSweep.Carried(true, counts);
            TellOwnSide(counts);
        }
        catch (Exception e)
        {
            PatchGuard.Report("ServerLink.CarriedWhenSpawned", e);
        }
    }

    private static void OnReply(ZRpc rpc, string payload)
    {
        try
        {
            if (!_active || payload == null)
            {
                return;
            }
            _answered = _asked;
            var split = payload.IndexOf('|');
            if (split < 0 || payload.Substring(0, split) != Layout)
            {
                Output.Say("The server has another version of " + ModInfo.Name + ". Use matching versions.");
                return;
            }
            Output.Say(payload.Substring(split + 1));
        }
        catch (Exception e)
        {
            PatchGuard.Report("ServerLink.OnReply", e);
        }
    }

    // "1|clear" -> "clear". Other layout or shape -> "".
    private static string Word(string payload)
    {
        if (payload == null)
        {
            return "";
        }
        var split = payload.IndexOf('|');
        return split > 0 && payload.Substring(0, split) == Layout ? payload.Substring(split + 1) : "";
    }
}
