namespace MC.Shared;

// Me = why feature on or off right now. Name go into shared registry as string, so never rename members.
internal enum ModState
{
    Starting,
    Active,
    Disabled,            // user turn off
    MissingDependency,   // needed mod not installed or failed to load
    DependencyInactive,  // needed mod installed but not active
    WaitingForServer,    // client connected, server answer not come yet
    ServerMissing,       // server no have mod
    ServerMismatch,      // server have other major version
    ServerOnly,          // server-side mod, but me just a client of remote server
    SinglePlayerOnly,    // single-player mod, but session is multiplayer
    Error,               // patching or start blew up; see log
    Conflict,            // mod say it cannot run on this game (LocalBlocker), e.g. other mod do same job
}
