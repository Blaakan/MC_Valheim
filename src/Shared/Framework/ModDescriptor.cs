namespace MC.Shared;

// Me = who mod is. Build make one from csproj (ModInfo.Descriptor). Plugin code never write this by hand.
internal sealed class ModDescriptor
{
    public ModDescriptor(string guid, string name, string version, string build, string category, string scope,
        string side, string multiplayer, string multiplayerNotes, string[] requires, int networkVersion)
    {
        Guid = guid;
        Name = name;
        Version = version;
        Build = build ?? "";
        Category = category;
        Scope = scope;
        Side = side;
        Multiplayer = multiplayer;
        MultiplayerNotes = multiplayerNotes ?? "";
        Requires = requires ?? new string[0];
        NetworkVersion = networkVersion;
    }

    public string Guid { get; }
    public string Name { get; }
    public string Version { get; }

    // Git commit (+dirty) of this build. Test report say which code was tested.
    public string Build { get; }
    public string Category { get; }
    public string Scope { get; }

    // Client = only user need it. Server = host/server need it. Both = server and every client need it.
    public string Side { get; }

    // Compatible = fine in multiplayer. Limited = work, with caveats (see notes). SinglePlayer = off in multiplayer.
    public string Multiplayer { get; }
    public string MultiplayerNotes { get; }

    // Guids of other mods this one need at runtime. Missing/off dependency = me go inactive, never crash.
    public string[] Requires { get; }

    // Bump when RPC names/payloads or ZDO keys/format change. Client and server must match (Both mods).
    public int NetworkVersion { get; }

    public bool NeedsServer => Side == Sides.Both;
    public bool ServerOnly => Side == Sides.Server;
    public bool SinglePlayerOnly => Multiplayer == MultiplayerModes.SinglePlayer;

    public static class Sides
    {
        public const string Client = "Client";
        public const string Server = "Server";
        public const string Both = "Both";
    }

    public static class MultiplayerModes
    {
        public const string Compatible = "Compatible";
        public const string Limited = "Limited";
        public const string SinglePlayer = "SinglePlayer";
    }
}
