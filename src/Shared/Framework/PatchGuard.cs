using System;
using System.Collections.Generic;

namespace MC.Shared;

// Patch body must never throw into game code: game method stop half-way = broken state.
// Me catch in each patch and call Report. Me log first time per site only, so per-frame patch no flood log.
internal static class PatchGuard
{
    private static readonly HashSet<string> Reported = new HashSet<string>();

    public static void Report(string site, Exception e)
    {
        if (Reported.Add(site))
        {
            Log.Error($"{site} failed (further errors from here are hidden): {e}");
        }
    }
}
