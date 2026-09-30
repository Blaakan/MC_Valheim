using System.Collections.Generic;
using HarmonyLib;
using MC.Shared;

namespace MC.Crafting.ForgeIdolUpgradesMod;

// Other Forge mods (ReforgedPotential, OdinBet, Wire's, Forge of Potential Safe...) also take over the refinement in
// InventoryGui.DoCrafting. Me cannot share the roll with them: me warn once in the log (first Forge visit, when every
// mod is loaded), naming them. For the roll: warn only, never act.
// Idol tier rule: such a mod spend the idol with its own code (the recipe's own idol, or its own idol progression),
// so if me still raised the idol in the checks, a player could pass the check with a higher idol and spend nothing.
// So while one is there, the rule step aside (IdolSwap, ForgeRefine.TryPlan), said once in the log.
internal static class ForgeGuard
{
    private static bool _done;
    private static bool? _takeover;
    private static List<string> _owners;
    private static bool _ruleOffLogged;

    internal static void Reset()
    {
        _done = false;
        _takeover = null;
        _owners = null;
        _ruleOffLogged = false;
    }

    // Other mods with a transpiler or a bool prefix (can skip vanilla) on DoCrafting. Void prefix only watch.
    private static List<string> Owners()
    {
        if (_owners != null)
        {
            return _owners;
        }
        _owners = new List<string>();
        var method = AccessTools.Method(typeof(InventoryGui), nameof(InventoryGui.DoCrafting));
        var info = method != null ? Harmony.GetPatchInfo(method) : null;
        if (info == null)
        {
            return _owners;
        }
        foreach (var t in info.Transpilers)
        {
            if (t.owner != ModInfo.Guid && !_owners.Contains(t.owner))
            {
                _owners.Add(t.owner);
            }
        }
        foreach (var p in info.Prefixes)
        {
            if (p.owner != ModInfo.Guid && p.PatchMethod != null && p.PatchMethod.ReturnType == typeof(bool)
                && !_owners.Contains(p.owner))
            {
                _owners.Add(p.owner);
            }
        }
        return _owners;
    }

    // True = another mod may take the refinement over: idol tier rule off (first answer cached for the session; every
    // mod patch at plugin load, long before a Forge is used).
    internal static bool ForeignTakeover
    {
        get
        {
            if (!_takeover.HasValue)
            {
                _takeover = Owners().Count > 0;
            }
            if (_takeover.Value && !_ruleOffLogged)
            {
                _ruleOffLogged = true;
                Log.Warning($"Another mod may take over refinement at the Forge of Potential ({string.Join(", ", Owners().ToArray())}): "
                            + "the higher idols at high levels (HigherIdolAtHighLevels) are off on this game, so the idol "
                            + "it spends is the one the Forge asks for.");
            }
            return _takeover.Value;
        }
    }

    internal static void WarnForeignPatches()
    {
        if (_done)
        {
            return;
        }
        _done = true;
        var owners = Owners();
        if (owners.Count > 0)
        {
            Log.Warning($"Another mod changes crafting or the Forge of Potential ({string.Join(", ", owners.ToArray())}). "
                        + $"If it changes refinement too, {ModInfo.Name} cannot set the odds: use one Forge mod at a time.");
        }
    }
}
