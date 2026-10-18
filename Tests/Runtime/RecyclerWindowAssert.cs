using System.Collections.Generic;
using NUnit.Framework;

namespace Sinkii09.UIFramework.Tests
{
    /// <summary>
    /// The three ways a damaged shown-cell window stays silent: a hole in the middle that the pump
    /// never refills, one cell bound to two slots, and a cell left active on screen that no slot
    /// tracks. None of them throws, and a test that only checks <c>ShownIndices</c> sees none of them.
    /// </summary>
    internal static class RecyclerWindowAssert
    {
        internal static void Intact(RecyclerViewHarness harness)
        {
            IReadOnlyList<int> shown = harness.View.ShownIndices;
            for (int i = 1; i < shown.Count; i++)
                Assert.AreEqual(shown[i - 1] + 1, shown[i],
                    "ShownIndices must be one contiguous run — the window only grows at its ends");

            var cells = new HashSet<RecyclerCell>();
            harness.View.ForEachShownCell(cell =>
                Assert.IsTrue(cells.Add(cell), $"the cell for index {cell.Index} is bound to two slots"));

            Assert.AreEqual(shown.Count, cells.Count, "every shown index needs its own cell");
            Assert.AreEqual(shown.Count, harness.ActiveCells,
                "an active cell exists that no slot tracks — a ghost left on screen");
        }
    }
}
