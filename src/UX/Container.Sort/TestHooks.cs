#if DEBUG
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MC.UX.ContainerSortMod;

// Debug build only: windows on the mod's private state for the self-tests (SelfTests.cs). Nothing here change what
// the mod do; Release build has none of it (partial methods without body vanish with their calls).

internal static partial class ContainerSorter
{
    // What the last TrySortOpenContainer call did.
    internal static SortOutcome LastOutcome;
    internal static SortCriterion LastCriterion;
    internal static bool LastWritten;
    internal static int LastMoved;
    internal static int LastMerged;

    // Every TrySortOpenContainer call that came to an end, whatever the end.
    internal static int Clicks;

    internal static void TestReset()
    {
        LastOutcome = SortOutcome.None;
        LastWritten = false;
        LastMoved = 0;
        LastMerged = 0;
    }

    static partial void TestNote(SortOutcome outcome)
    {
        Clicks++;
        LastOutcome = outcome;
        LastWritten = false;
        LastMoved = 0;
        LastMerged = 0;
    }

    static partial void TestDone(SortCriterion criterion, Run run)
    {
        Clicks++;
        LastOutcome = SortOutcome.Done;
        LastCriterion = criterion;
        LastWritten = run.Written;
        LastMoved = run.Moved;
        LastMerged = run.Merged;
    }
}

internal static partial class SortChestUi
{
    internal const string TestNamePrefix = "MC_ContainerSort_";

    // Read-only picture of one of our two buttons.
    internal sealed class TestView
    {
        internal GameObject Go;
        internal RectTransform Rect;
        internal Button Button;
        internal string Label;
        internal UITooltip Tip;
        internal UIGamePad Pad;
        internal GameObject Hint;
        internal string Glyph;
        internal string Key;
    }

    // Size combination -> how far its SelfCheck got (1 = row picked, 2 = screen check and dump done).
    private static readonly Dictionary<long, int> TestStages = new Dictionary<long, int>();

    static partial void TestMark(long combo, int stage)
    {
        TestStages[combo] = stage;
    }

    internal static TestView TestSort => View(_sort);

    internal static TestView TestCriterion => View(_criterion);

    private static TestView View(Widget widget)
    {
        if (!Alive(widget))
        {
            return null;
        }
        return new TestView
        {
            Go = widget.Go,
            Rect = widget.Rect,
            Button = widget.Go.GetComponent<Button>(),
            Label = widget.Label != null ? widget.Label.text : null,
            Tip = widget.Tip,
            Pad = widget.Pad,
            Hint = widget.Pad != null ? widget.Pad.m_hint : null,
            Glyph = widget.Glyph != null ? widget.Glyph.text : null,
            Key = widget.Key,
        };
    }

    // The mod's own layout verdict (same words as its warning). Null = fine.
    internal static string TestLayoutProblem(InventoryGui gui) =>
        Alive(_sort) && Alive(_criterion) ? FindProblem(gui) : "the buttons do not exist";

    internal static UIGroupHandler TestContainerGroup(InventoryGui gui) => ContainerGroup(gui);

    internal static bool TestParentHasLayoutGroup => _parentHasLayoutGroup;

    // Make the next opening of this size combination check its layout again.
    internal static void TestForgetLayout(int width, int height, int rows)
    {
        var combo = Combo(width, height, rows);
        Checked.Remove(combo);
        SecondRow.Remove(combo);
        TestStages.Remove(combo);
    }

    internal static int TestLayoutStage(int width, int height, int rows) =>
        TestStages.TryGetValue(Combo(width, height, rows), out var stage) ? stage : 0;

    internal static bool TestSecondRow(int width, int height, int rows) =>
        SecondRow.TryGetValue(Combo(width, height, rows), out var second) && second;

    // Fresh buttons like the first opening of a session: controller parts not started, no layout remembered.
    internal static void TestRemake(InventoryGui gui)
    {
        Destroy();
        TestStages.Clear();
        Create(gui);
    }
}

internal static partial class BiomeIndex
{
    internal static int TestBuilds;
    internal static long TestLastBuildMs = -1;

    static partial void TestBuilt(long ms)
    {
        TestBuilds++;
        TestLastBuildMs = ms;
    }

    internal static bool TestEntry(string prefab, out BiomeRank rank, out string source)
    {
        if (prefab != null && Map.TryGetValue(prefab, out var e))
        {
            rank = e.Rank;
            source = SourceLabel(e.Src);
            return true;
        }
        rank = BiomeRank.Unknown;
        source = "";
        return false;
    }
}
#endif
