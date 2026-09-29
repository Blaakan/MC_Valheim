using System.Collections.Generic;
using HarmonyLib;
using MC.Shared;

namespace MC.Crafting.ForgeIdolUpgradesMod;

// Other Forge mods (ReforgedPotential, OdinBet, Wire's, Forge of Potential Safe...) also take over the refinement in
// InventoryGui.DoCrafting. Me cannot share the roll with them: me warn once in the log (first Forge visit, when every
// mod is loaded), naming them. Warn only, never act.
internal static class ForgeGuard
{
    private static bool _done;

    internal static void Reset() => _done = false;

    internal static void WarnForeignPatches()
    {
        if (_done)
        {
            return;
        }
        _done = true;
        var method = AccessTools.Method(typeof(InventoryGui), nameof(InventoryGui.DoCrafting));
        var info = method != null ? Harmony.GetPatchInfo(method) : null;
        if (info == null)
        {
            return;
        }
        var owners = new List<string>();
        foreach (var t in info.Transpilers)
        {
            if (t.owner != ModInfo.Guid && !owners.Contains(t.owner))
            {
                owners.Add(t.owner);
            }
        }
        foreach (var p in info.Prefixes)
        {
            // Bool prefix can skip vanilla (a takeover). Void prefix only watch.
            if (p.owner != ModInfo.Guid && p.PatchMethod != null && p.PatchMethod.ReturnType == typeof(bool)
                && !owners.Contains(p.owner))
            {
                owners.Add(p.owner);
            }
        }
        if (owners.Count > 0)
        {
            Log.Warning($"Another mod changes crafting or the Forge of Potential ({string.Join(", ", owners.ToArray())}). "
                        + $"If it changes refinement too, {ModInfo.Name} cannot set the odds: use one Forge mod at a time.");
        }
    }
}
