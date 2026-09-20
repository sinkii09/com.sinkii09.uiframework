using System.Collections.Generic;
using NUnit.Framework;

namespace Sinkii09.UIFramework.Tests.Editor
{
    /// <summary>
    /// The iteration budget is derived from <c>MinStride</c>, which in a grid is one ROW — while the
    /// pump still realises one CELL per iteration. Budget rows for a loop that spends cells and it
    /// comes up short by exactly the column count; the pump then logs an error and abandons the
    /// tick, so the grid takes several frames, and an error apiece, to fill a window it should
    /// have filled in one. Nothing about that is visible in a single-column test, which is why
    /// these exist separately.
    /// </summary>
    public class RecycleWindowGridBudgetTests
    {
        private const float CreateDistance = 200f;
        private const float RecycleDistance = 300f;
        private const float RowHeight = 100f;
        private const float Spacing = 10f;

        private static IItemOffsets Grid(int itemCount, int columns)
        {
            int rows = (itemCount + columns - 1) / columns;
            return new GridOffsets(new UniformOffsets(rows, RowHeight, Spacing), columns, itemCount);
        }

        /// <summary>Cells a reseed must create to span the viewport plus both create bands.</summary>
        private static int CellsToFill(float viewportSize, float rowStride, int columns)
            => (int)System.Math.Ceiling((viewportSize + 2f * CreateDistance) / rowStride) * columns;

        [TestCase(500f, 2)]
        [TestCase(500f, 3)]
        [TestCase(1080f, 4)]
        [TestCase(1920f, 6)]
        public void Budget_CoversEveryCellInEveryVisibleRow(float viewportSize, int columns)
        {
            IItemOffsets offsets = Grid(itemCount: 100000, columns: columns);

            int needed = CellsToFill(viewportSize, offsets.MinStride, columns);
            int budget = RecycleWindow.MaxIterationsFor(
                viewportSize, CreateDistance, offsets.MinStride, offsets.Count, offsets.ItemsPerStride);

            Assert.Greater(budget, needed,
                $"{columns} columns at viewport {viewportSize} needs {needed} creates to fill; " +
                $"a budget of {budget} aborts the tick before the grid is full");
        }

        /// <summary>
        /// The defect this parameter exists to fix, stated directly: a row-only budget is short.
        /// </summary>
        [Test]
        public void RowOnlyBudget_IsShortOfWhatAGridNeeds()
        {
            const float viewportSize = 1080f;
            const int columns = 4;
            IItemOffsets offsets = Grid(itemCount: 100000, columns: columns);

            int needed = CellsToFill(viewportSize, offsets.MinStride, columns);
            int rowOnly = RecycleWindow.MaxIterationsFor(
                viewportSize, CreateDistance, offsets.MinStride, offsets.Count);

            Assert.Less(rowOnly, needed,
                "if a row-only budget were already sufficient, ItemsPerStride would be dead weight " +
                "and this whole parameter could be deleted");
        }

        [Test]
        public void SingleColumn_IsUnchangedByTheNewParameter()
        {
            int oldWay = RecycleWindow.MaxIterationsFor(1920f, CreateDistance, 30f, 100000);
            int newWay = RecycleWindow.MaxIterationsFor(1920f, CreateDistance, 30f, 100000, 1);

            Assert.That(newWay, Is.EqualTo(oldWay));
        }

        [TestCase(0)]
        [TestCase(-3)]
        public void NonsenseColumnCount_FallsBackToOne(int itemsPerStride)
        {
            int budget = RecycleWindow.MaxIterationsFor(500f, CreateDistance, 100f, 1000, itemsPerStride);
            int asList = RecycleWindow.MaxIterationsFor(500f, CreateDistance, 100f, 1000, 1);

            Assert.That(budget, Is.EqualTo(asList));
        }

        /// <summary>
        /// A grid with fewer items than the viewport could hold must still be allowed to realise
        /// every one of them.
        ///
        /// <para>This deliberately does <b>not</b> claim to pin the multiply-then-clamp ordering: on
        /// a short grid the wrong order is over-generous rather than short, so no assertion here can
        /// distinguish them. The ordering is covered where it bites, by
        /// <see cref="Window_ConvergesWithinItsBudget"/>.</para>
        /// </summary>
        [Test]
        public void ShortGrid_CanStillRealiseEveryItem()
        {
            const int itemCount = 12;
            const int columns = 3;
            IItemOffsets offsets = Grid(itemCount, columns);

            int budget = RecycleWindow.MaxIterationsFor(
                4000f, CreateDistance, offsets.MinStride, itemCount, columns);

            Assert.GreaterOrEqual(budget, itemCount,
                "a 12-item grid must be allowed to realise all 12 items");
        }

        // ---- the simulation: does the window actually converge inside its budget? ---------------

        /// <summary>
        /// Drives <see cref="RecycleWindow.Decide"/> exactly as the pump does — reseed, then apply
        /// one action per iteration — and reports whether it settled before the budget ran out. No
        /// scene, no ScrollRect, no frames.
        /// </summary>
        private static bool Converges(IItemOffsets offsets, float viewportStart, float viewportSize, out int used)
        {
            var shown = new List<int> { offsets.IndexAt(viewportStart) };
            int budget = RecycleWindow.MaxIterationsFor(
                viewportSize, CreateDistance, offsets.MinStride, offsets.Count, offsets.ItemsPerStride);

            used = 0;
            while (true)
            {
                int head = shown[0];
                int tail = shown[shown.Count - 1];

                var state = new WindowState(
                    viewportStart, viewportSize, offsets.Count, shown.Count, tick: 1,
                    headIndex: head, headOffset: offsets.OffsetOf(head), headSize: offsets.SizeOf(head),
                    headCreatedTick: 0,
                    tailIndex: tail, tailOffset: offsets.OffsetOf(tail), tailSize: offsets.SizeOf(tail),
                    tailCreatedTick: 0,
                    itemsPerStride: offsets.ItemsPerStride);

                WindowAction action = RecycleWindow.Decide(state, RecycleDistance, CreateDistance);
                if (action == WindowAction.None) return true;

                if (++used > budget) return false;

                switch (action)
                {
                    case WindowAction.RecycleHead: shown.RemoveAt(0); break;
                    case WindowAction.RecycleTail: shown.RemoveAt(shown.Count - 1); break;
                    case WindowAction.CreateBeforeHead: shown.Insert(0, shown[0] - 1); break;
                    case WindowAction.CreateAfterTail: shown.Add(shown[shown.Count - 1] + 1); break;
                }
            }
        }

        [TestCase(500f, 2, 0f)]
        [TestCase(500f, 3, 0f)]
        [TestCase(1080f, 4, 5000f)]
        [TestCase(1920f, 6, 12345f)]
        [TestCase(300f, 5, 99f)]
        public void Window_ConvergesWithinItsBudget(float viewportSize, int columns, float viewportStart)
        {
            IItemOffsets offsets = Grid(itemCount: 100000, columns: columns);

            Assert.That(Converges(offsets, viewportStart, viewportSize, out int used), Is.True,
                $"{columns} columns at viewport {viewportSize} from {viewportStart} did not settle " +
                $"within its budget (used {used})");
        }

        [Test]
        public void Window_ConvergesOnAGridSmallerThanTheViewport()
        {
            IItemOffsets offsets = Grid(itemCount: 5, columns: 3);

            Assert.That(Converges(offsets, 0f, 2000f, out _), Is.True);
        }

        /// <summary>
        /// Every item in a row shares one offset, so the tail reaches the create band as soon as the
        /// row's FIRST cell exists. Without the stride-completion rule the rest of that row is never
        /// realised — and with a create distance smaller than a row, the half-empty row is on screen.
        /// </summary>
        [TestCase(200f, TestName = "PartialRow_Completed_WithGenerousCreateDistance")]
        [TestCase(50f, TestName = "PartialRow_Completed_WhenCreateDistanceIsSmallerThanARow")]
        public void Window_NeverSettlesOnAPartiallyRealisedRow(float createDistance)
        {
            const int columns = 5;
            IItemOffsets offsets = Grid(itemCount: 100000, columns: columns);

            var shown = new List<int> { offsets.IndexAt(0f) };
            int guard = 0;

            while (guard++ < 10000)
            {
                int head = shown[0];
                int tail = shown[shown.Count - 1];

                var state = new WindowState(
                    0f, 500f, offsets.Count, shown.Count, tick: 1,
                    headIndex: head, headOffset: offsets.OffsetOf(head), headSize: offsets.SizeOf(head),
                    headCreatedTick: 0,
                    tailIndex: tail, tailOffset: offsets.OffsetOf(tail), tailSize: offsets.SizeOf(tail),
                    tailCreatedTick: 0,
                    itemsPerStride: columns);

                WindowAction action = RecycleWindow.Decide(state, RecycleDistance, createDistance);
                if (action == WindowAction.None) break;

                switch (action)
                {
                    case WindowAction.RecycleHead: shown.RemoveAt(0); break;
                    case WindowAction.RecycleTail: shown.RemoveAt(shown.Count - 1); break;
                    case WindowAction.CreateBeforeHead: shown.Insert(0, shown[0] - 1); break;
                    case WindowAction.CreateAfterTail: shown.Add(shown[shown.Count - 1] + 1); break;
                }
            }

            Assert.That(guard, Is.LessThan(10000), "the window did not settle at all");
            Assert.That(shown.Count % columns, Is.Zero,
                $"settled on {shown.Count} cells, which is not a whole number of {columns}-cell rows " +
                "— the last row is realised only in part");
            Assert.That(shown[0] % columns, Is.Zero, "the window must start at a row boundary");
        }
    }
}
