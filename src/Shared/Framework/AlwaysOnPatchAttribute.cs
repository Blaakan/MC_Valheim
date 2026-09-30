using System;

namespace MC.Shared;

// Me mark Harmony patch class that must stay on whatever feature state: registration of items, networked prefabs,
// rpc handlers, guards that must work while feature off. ModPlugin apply me once at start under own Harmony id
// (<guid>.alwayson); live toggle never remove me (it only unpatch feature id). Game quit remove me.
// Code in me must not need feature Active or bound config (read state itself, use defaults when config null).
[AttributeUsage(AttributeTargets.Class)]
internal sealed class AlwaysOnPatchAttribute : Attribute
{
}
