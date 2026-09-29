using System;
using System.Reflection.Emit;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.CompendiumEncyclopediaMod;

// Me check the one vanilla detail the Esc handling stand on (design 1.2, decision 19): InventoryGui.Update close the
// inventory on Esc only when m_shownFrames > 1, then call Hide. Our Update prefix set m_shownFrames = 0 when Esc close
// our window, so vanilla skip its Hide that frame. Game update change that code = one Warning: Esc then close the whole
// inventory too; everything else keep working.
// Real IL of 1.0.16 (ilspycmd -il): "ldarg.0; ldfld m_shownFrames; ldc.i4.1; cgt; ldloc flag2; and; brfalse" (the
// compiler fold "&& flag2" into an "and", no branch right after the constant). Other compilers may emit
// "ldc.i4.1; ble(.s)" instead. Me accept both: compare op or branch op right after "ldfld; ldc.i4.1". The increment at
// the top ("ldfld; ldc.i4.1; add") is neither, so it never count.
internal static class EscGuard
{
    private static bool _warned;

    /// <summary>Last check result: null = gate found (self tests read it).</summary>
    internal static string Problem { get; private set; } = "not checked";

    // OnActivated call me. Read original (unpatched) IL.
    internal static void Verify()
    {
        string problem;
        try
        {
            problem = Check();
        }
        catch (Exception e)
        {
            problem = $"{e.GetType().Name}: {e.Message}";
        }
        Problem = problem;
        if (problem == null || _warned)
        {
            return;
        }
        _warned = true;
        Log.Warning($"InventoryGui.Update changed ({problem}): Esc may close the whole inventory instead of only the Encyclopedia.");
    }

    private static string Check()
    {
        var method = AccessTools.Method(typeof(InventoryGui), nameof(InventoryGui.Update));
        var shown = AccessTools.Field(typeof(InventoryGui), nameof(InventoryGui.m_shownFrames));
        var hide = AccessTools.Method(typeof(InventoryGui), nameof(InventoryGui.Hide));
        if (method == null || shown == null || hide == null)
        {
            return "Update, m_shownFrames or Hide not found";
        }
        var il = PatchProcessor.GetOriginalInstructions(method);
        if (il == null)
        {
            return "no method body";
        }
        var gate = -1;
        for (var i = 0; i + 2 < il.Count; i++)
        {
            if (il[i].LoadsField(shown) && il[i + 1].LoadsConstant(1) && IsGreaterTest(il[i + 2].opcode))
            {
                gate = i;
                break;
            }
        }
        if (gate < 0)
        {
            return "no 'm_shownFrames > 1' check";
        }
        for (var j = gate + 3; j < il.Count; j++)
        {
            if (il[j].Calls(hide))
            {
                Log.Debug($"InventoryGui.Update Esc gate checked: m_shownFrames > 1 at IL {gate} ({il[gate + 2].opcode.Name}), Hide at {j}.");
                return null;
            }
        }
        return "no Hide call after the 'm_shownFrames > 1' check";
    }

    // "> 1" as a value (cgt, cgt.un) or as a branch: jump over when <= 1 (ble), or jump in when > 1 (bgt).
    private static bool IsGreaterTest(OpCode op) =>
        op == OpCodes.Cgt || op == OpCodes.Cgt_Un
        || op == OpCodes.Ble || op == OpCodes.Ble_S || op == OpCodes.Ble_Un || op == OpCodes.Ble_Un_S
        || op == OpCodes.Bgt || op == OpCodes.Bgt_S || op == OpCodes.Bgt_Un || op == OpCodes.Bgt_Un_S;
}
