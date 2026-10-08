using System;
using System.Globalization;
using MC.Shared;

namespace MC.Farming.BreedingStarInheritanceMod;

// Where numbers of a birth come from.
internal enum SettingsSource : byte
{
    Own,     // this game's config: single player, host, server, or client before server answer
    Server,  // server's numbers: client of a server with mod
    Test,    // Debug self test override (rule only)
}

// Me = every number a birth need: rule + farmer range, and where they come from.
internal readonly struct BreedingSettings
{
    internal readonly RuleSettings Rule;
    internal readonly float FarmerRange;
    internal readonly SettingsSource Source;

    internal BreedingSettings(in RuleSettings rule, float farmerRange, SettingsSource source)
    {
        Rule = rule;
        FarmerRange = farmerRange;
        Source = source;
    }
}

// Me = server settings for everyone. Server (dedicated or host) send its five birth numbers to each player with mod;
// client use them instead of own config while connected to that server. Single player, host, server: own config.
//   client -> server  "<guid>.SettingsRequest"  "1"                               after handshake (RPC_PeerInfo, client
//                                                                                 register listener there too), and
//                                                                                 when client turn on while connected
//   server -> client  "<guid>.Settings"         "1|c0|c100|cw|range|maxStars"     answer; again to all players with mod
//                                                                                 when server settings change or server
//                                                                                 turn on
// Vanilla peer ignore unknown rpc. Numbers in invariant culture. Client never trust wire: check and clamp (same ranges
// as config). Values belong to ZNet instance they came on: new session = stale, own config again.
// Handlers stay on rpc after feature off (ZRpc no unregister): server answer only while Active; client store always
// (used only while Active, and client ask again when it turn on).
internal static class ServerSettings
{
    internal const string SettingsRpc = ModInfo.Guid + ".Settings";
    internal const string RequestRpc = ModInfo.Guid + ".SettingsRequest";
    internal const string Protocol = "1";

    // Same ranges as config (Plugin.BindConfig).
    internal const float MinChance = 0f;
    internal const float MaxChance = 100f;
    internal const float MinFarmerRange = 5f;
    internal const float MaxFarmerRange = 64f;
    internal const int MinMaxStars = 0;
    internal const int MaxMaxStars = 10;

    private const int FieldCount = 6;

    private static bool _active;
    private static bool _pushPending;
    private static bool _haveServer;
    private static RuleSettings _serverRule;
    private static float _serverRange;
    private static ZNet _serverSession;
    private static string _lastLogged;
    private static bool _badLogged;

    internal static bool PushPending => _pushPending;

#if DEBUG
    // Debug build only: self test put server numbers of an OLDER session in memory (what a game has after it left a
    // server), to prove single player still use own config. Forget() clean it.
    internal static void TestKeepOldServerNumbers(in RuleSettings rule, float farmerRange)
    {
        _haveServer = true;
        _serverRule = rule;
        _serverRange = farmerRange;
        _serverSession = null;
    }
#endif

    // ---------- pure part (self test hammer it) ----------

    // "1|c0|c100|cw|range|maxStars". "R" = float come back bit-exact.
    internal static string Encode(in RuleSettings rule, float farmerRange)
    {
        var c = CultureInfo.InvariantCulture;
        return string.Join("|", new[]
        {
            Protocol,
            rule.ChanceAtFarming0.ToString("R", c),
            rule.ChanceAtFarming100.ToString("R", c),
            rule.ChanceWithoutFarmer.ToString("R", c),
            farmerRange.ToString("R", c),
            rule.MaxStars.ToString(c),
        });
    }

    // False = unreadable (null, other protocol, wrong field count, not a number, NaN, infinity): caller keep what it
    // had. True = values usable; clamped = some were outside config ranges and got pulled in.
    internal static bool TryDecode(string payload, out RuleSettings rule, out float farmerRange, out bool clamped)
    {
        rule = default;
        farmerRange = 0f;
        clamped = false;
        if (string.IsNullOrEmpty(payload))
        {
            return false;
        }
        var parts = payload.Split('|');
        if (parts.Length != FieldCount || parts[0] != Protocol)
        {
            return false;
        }
        if (!TryFloat(parts[1], out var c0) || !TryFloat(parts[2], out var c100) || !TryFloat(parts[3], out var cw)
            || !TryFloat(parts[4], out var range)
            || !int.TryParse(parts[5], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var maxStars))
        {
            return false;
        }
        rule = new RuleSettings(
            Clamp(c0, MinChance, MaxChance, ref clamped),
            Clamp(c100, MinChance, MaxChance, ref clamped),
            Clamp(cw, MinChance, MaxChance, ref clamped),
            Clamp(maxStars, MinMaxStars, MaxMaxStars, ref clamped));
        farmerRange = Clamp(range, MinFarmerRange, MaxFarmerRange, ref clamped);
        return true;
    }

    // Me pick: server numbers only for connected client that got them this session; else own.
    internal static BreedingSettings Pick(in BreedingSettings own, bool useServer, in RuleSettings serverRule, float serverRange)
    {
        return useServer ? new BreedingSettings(serverRule, serverRange, SettingsSource.Server) : own;
    }

    // Plain decimal only (no thousands, no hex); NaN and infinity refused.
    private static bool TryFloat(string text, out float value)
    {
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static float Clamp(float value, float min, float max, ref bool clamped)
    {
        if (value < min)
        {
            clamped = true;
            return min;
        }
        if (value > max)
        {
            clamped = true;
            return max;
        }
        return value;
    }

    private static int Clamp(int value, int min, int max, ref bool clamped)
    {
        if (value < min)
        {
            clamped = true;
            return min;
        }
        if (value > max)
        {
            clamped = true;
            return max;
        }
        return value;
    }

    // ---------- state ----------

    // Effective numbers: server's when this game is a connected client that got them in this session, else own.
    internal static BreedingSettings Effective(in BreedingSettings own)
    {
        var net = ZNet.instance;
        var useServer = _haveServer && net != null && !net.IsServer() && ReferenceEquals(net, _serverSession);
        return Pick(own, useServer, _serverRule, _serverRange);
    }

    // OnActivated. Server: listen on peers that joined while me off, send numbers to players with mod (they may have
    // waited with none). Client already connected (turned on late): listen and ask. Client still connecting: no server
    // peer yet, RPC_PeerInfo postfix listen and ask when handshake done.
    internal static void Start()
    {
        _active = true;
        _pushPending = false;
        var net = ZNet.instance;
        if (net == null)
        {
            return;
        }
        if (net.IsServer())
        {
            foreach (var peer in net.GetPeers())
            {
                if (peer?.m_rpc != null)
                {
                    RegisterServer(peer.m_rpc);
                }
            }
            _pushPending = true;
            return;
        }
        var server = net.GetServerPeer();
        if (server?.m_rpc != null)
        {
            RegisterClient(server.m_rpc, forget: false);
            Request(server.m_rpc);
        }
    }

    // OnDeactivated: forget server numbers (own config again at next use), stop answering.
    internal static void Stop()
    {
        _active = false;
        _pushPending = false;
        Forget();
    }

    // Drop server numbers and log memory (new connection, feature off). Own config again at next use.
    internal static void Forget()
    {
        _haveServer = false;
        _serverRule = default;
        _serverRange = 0f;
        _serverSession = null;
        _lastLogged = null;
        _badLogged = false;
    }

    // Server: own settings changed (SettingChanged; config reload fire one per value). Me send once, next ZNet.Update.
    internal static void MarkChanged()
    {
        if (_active)
        {
            _pushPending = true;
        }
    }

    // ZNet.Update postfix, only when PushPending.
    internal static void Update()
    {
        _pushPending = false;
        var net = ZNet.instance;
        if (!_active || net == null || !net.IsServer())
        {
            return;
        }
        var own = Plugin.OwnSettings();
        var payload = Encode(own.Rule, own.FarmerRange);
        var sent = 0;
        foreach (var peer in net.GetPeers())
        {
            if (peer?.m_rpc != null && peer.IsReady() && NetworkGate.PeerHasMod(peer))
            {
                peer.m_rpc.Invoke(SettingsRpc, payload);
                sent++;
            }
        }
        if (sent > 0)
        {
            Log.Debug($"Sent the breeding settings to {sent} player(s): {payload}.");
        }
    }

    // ---------- wire ----------

    // Server side, per peer (OnNewConnection, or Start for peers already there). Register replace: safe twice.
    internal static void RegisterServer(ZRpc rpc)
    {
        rpc.Register<string>(RequestRpc, OnRequest);
    }

    // Client side, on server peer. New connection = forget numbers of old session first.
    internal static void RegisterClient(ZRpc rpc, bool forget)
    {
        if (forget)
        {
            Forget();
        }
        rpc.Register<string>(SettingsRpc, OnSettings);
    }

    // Client: ask server (server without mod ignore it).
    internal static void Request(ZRpc rpc)
    {
        rpc.Invoke(RequestRpc, Protocol);
    }

    private static void OnRequest(ZRpc rpc, string protocol)
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
            var own = Plugin.OwnSettings();
            rpc.Invoke(SettingsRpc, Encode(own.Rule, own.FarmerRange));
        }
        catch (Exception e)
        {
            PatchGuard.Report("ServerSettings.OnRequest", e);
        }
    }

    private static void OnSettings(ZRpc rpc, string payload)
    {
        try
        {
            Receive(payload);
        }
        catch (Exception e)
        {
            PatchGuard.Report("ServerSettings.OnSettings", e);
        }
    }

    // What Receive did with numbers.
    internal enum ReceiveResult : byte
    {
        Used,
        Clamped,
        Unreadable,
        NotAClient,
    }

    // Client: numbers from server. Server / single player / host never take numbers from a peer.
    internal static ReceiveResult Receive(string payload)
    {
        var net = ZNet.instance;
        if (net == null || net.IsServer())
        {
            return ReceiveResult.NotAClient;
        }
        if (!TryDecode(payload, out var rule, out var range, out var clamped))
        {
            if (!_badLogged)
            {
                _badLogged = true;
                Log.Warning($"The server sent breeding settings this version cannot read ({Shorten(payload)}); "
                            + (_haveServer ? "the last settings it sent stay in use." : "your own settings are used until it sends readable ones."));
            }
            return ReceiveResult.Unreadable;
        }
        _haveServer = true;
        _serverRule = rule;
        _serverRange = range;
        _serverSession = net;
        var text = Describe(rule, range);
        if (text != _lastLogged)
        {
            _lastLogged = text;
            if (clamped)
            {
                Log.Warning($"The server sent breeding settings outside the allowed ranges; they were brought back into range: {text}.");
            }
            Log.Info($"Using the server's breeding settings: {text}. Your own settings apply again in single player and when you host.");
        }
        return clamped ? ReceiveResult.Clamped : ReceiveResult.Used;
    }

    internal static string Describe(in RuleSettings rule, float range)
    {
        var c = CultureInfo.InvariantCulture;
        return $"ChanceAtFarming0 {rule.ChanceAtFarming0.ToString("0.##", c)}, ChanceAtFarming100 {rule.ChanceAtFarming100.ToString("0.##", c)}, "
               + $"ChanceWithoutFarmer {rule.ChanceWithoutFarmer.ToString("0.##", c)}, FarmerRange {range.ToString("0.##", c)}, "
               + $"MaxStars {rule.MaxStars.ToString(c)}";
    }

    private static string Shorten(string text)
    {
        if (text == null)
        {
            return "nothing";
        }
        return text.Length > 80 ? text.Substring(0, 80) + "..." : text;
    }
}
