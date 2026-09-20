using NUnit.Framework;

namespace Sinkii09.UIFramework.Tests.Editor
{
    /// <summary>
    /// A grid is a stack of rows, so every number here is a row number wearing an item index. The
    /// cases that matter are the ones where those two disagree: inside a row, at a partial last row,
    /// and at the ends where a clamp applies.
    ///
    /// <para>All of it runs with <b>non-zero spacing</b>, for the reason
    /// <see cref="ItemOffsetsTests"/> gives: at zero spacing several wrong implementations produce
    /// the same answers as the right one.</para>
    /// </summary>
    public class GridOffsetsTests
    {
        private const float RowHeight = 100f;
        private const float Spacing = 10f;
        private const float Stride = RowHeight + Spacing;

        private static int RowCount(int itemCount, int columns)
            => columns <= 1 ? itemCount : (itemCount + columns - 1) / columns;

        private static IItemOffsets Grid(int itemCount, int columns, float rowHeight = RowHeight)
            => new GridOffsets(
                new UniformOffsets(RowCount(itemCount, columns), rowHeight, Spacing),
                columns,
                itemCount);

        // ---- one column must be indistinguishable from no grid at all -------------------------

        /// <summary>
        /// The single-column path is the regression anchor for every list shipped before grids
        /// existed. Fuzzed across the whole index range rather than spot-checked, because an
        /// off-by-one that only shows at one index is exactly the kind this guards against.
        /// </summary>
        [Test]
        public void OneColumn_IsIdenticalToTheUnderlyingTable()
        {
            const int count = 97;
            var plain = new UniformOffsets(count, RowHeight, Spacing);
            IItemOffsets grid = new GridOffsets(plain, 1, count);

            Assert.That(grid.Count, Is.EqualTo(plain.Count));
            Assert.That(grid.TotalSize, Is.EqualTo(plain.TotalSize));
            Assert.That(grid.MinStride, Is.EqualTo(plain.MinStride));
            Assert.That(grid.ItemsPerStride, Is.EqualTo(1));

            for (int i = 0; i < count; i++)
            {
                Assert.That(grid.OffsetOf(i), Is.EqualTo(plain.OffsetOf(i)), $"OffsetOf({i})");
                Assert.That(grid.SizeOf(i), Is.EqualTo(plain.SizeOf(i)), $"SizeOf({i})");
            }

            // Sweep offsets, including overscroll on both sides.
            for (float offset = -200f; offset < count * Stride + 200f; offset += 17f)
            {
                Assert.That(grid.IndexAt(offset), Is.EqualTo(plain.IndexAt(offset)), $"IndexAt({offset})");
            }
        }

        // ---- inside a row ----------------------------------------------------------------------

        [Test]
        public void ItemsInTheSameRow_ShareOneOffsetAndOneSize()
        {
            IItemOffsets grid = Grid(itemCount: 9, columns: 3);

            Assert.That(grid.OffsetOf(0), Is.EqualTo(0f));
            Assert.That(grid.OffsetOf(1), Is.EqualTo(0f));
            Assert.That(grid.OffsetOf(2), Is.EqualTo(0f));

            Assert.That(grid.SizeOf(0), Is.EqualTo(RowHeight));
            Assert.That(grid.SizeOf(2), Is.EqualTo(RowHeight));
        }

        [Test]
        public void CrossingARowBoundary_AdvancesByOneStride()
        {
            IItemOffsets grid = Grid(itemCount: 9, columns: 3);

            Assert.That(grid.OffsetOf(3), Is.EqualTo(Stride).Within(0.001f));
            Assert.That(grid.OffsetOf(6), Is.EqualTo(2f * Stride).Within(0.001f));
        }

        [Test]
        public void ItemsPerStride_IsTheColumnCount()
        {
            Assert.That(Grid(itemCount: 9, columns: 3).ItemsPerStride, Is.EqualTo(3));
        }

        /// <summary>
        /// One row's advance, not one cell's. This is the number the pump's iteration budget is
        /// derived from, and reading it as a per-cell stride is what makes that budget too small.
        /// </summary>
        [Test]
        public void MinStride_IsTheRowStride()
        {
            Assert.That(Grid(itemCount: 9, columns: 3).MinStride, Is.EqualTo(Stride).Within(0.001f));
        }

        // ---- counts and extent -------------------------------------------------------------------

        [Test]
        public void Count_IsItemsNotRows()
        {
            Assert.That(Grid(itemCount: 9, columns: 3).Count, Is.EqualTo(9));
        }

        /// <summary>
        /// Two extra items are a whole extra row: the content has to be tall enough to scroll to
        /// them, however empty the rest of that row is.
        /// </summary>
        [Test]
        public void PartialLastRow_StillOccupiesAFullRow()
        {
            IItemOffsets grid = Grid(itemCount: 8, columns: 3);   // rows: 3,3,2

            Assert.That(grid.OffsetOf(6), Is.EqualTo(2f * Stride).Within(0.001f));
            Assert.That(grid.OffsetOf(7), Is.EqualTo(2f * Stride).Within(0.001f));
            Assert.That(grid.TotalSize, Is.EqualTo(3f * Stride - Spacing).Within(0.001f));
        }

        [Test]
        public void TotalSize_CountsRowsNotItems()
        {
            IItemOffsets grid = Grid(itemCount: 9, columns: 3);

            // Three rows, not nine: the single most expensive thing to get wrong, because the
            // content rect ends up three times too tall and nothing else complains.
            Assert.That(grid.TotalSize, Is.EqualTo(3f * Stride - Spacing).Within(0.001f));
        }

        // ---- IndexAt ------------------------------------------------------------------------------

        /// <summary>
        /// First of the row, never the last. The pump grows its window outward from whatever a
        /// reseed anchors on, so anchoring on the row's last item leaves the rest of that row
        /// unrealised until it scrolls out of view entirely.
        /// </summary>
        [Test]
        public void IndexAt_ReturnsTheFirstItemOfTheRow()
        {
            IItemOffsets grid = Grid(itemCount: 9, columns: 3);

            Assert.That(grid.IndexAt(0f), Is.EqualTo(0));
            Assert.That(grid.IndexAt(RowHeight / 2f), Is.EqualTo(0));
            Assert.That(grid.IndexAt(Stride), Is.EqualTo(3));
            Assert.That(grid.IndexAt(2f * Stride), Is.EqualTo(6));
        }

        /// <summary>An offset in the gap between rows belongs to the earlier row, as for a list.</summary>
        [Test]
        public void IndexAt_InTheGapBetweenRows_BelongsToTheEarlierRow()
        {
            IItemOffsets grid = Grid(itemCount: 9, columns: 3);

            Assert.That(grid.IndexAt(RowHeight + Spacing / 2f), Is.EqualTo(0));
        }

        [Test]
        public void IndexAt_Overscroll_ClampsAtBothEnds()
        {
            IItemOffsets grid = Grid(itemCount: 9, columns: 3);

            Assert.That(grid.IndexAt(-500f), Is.EqualTo(0));
            Assert.That(grid.IndexAt(99999f), Is.LessThanOrEqualTo(8));
        }

        /// <summary>
        /// Clamping must land on the FIRST index of the last row, not on the last item. A mid-row
        /// index would satisfy "in range" while breaking the contract the pump reseeds on, so this
        /// asserts the exact value rather than a range.
        /// </summary>
        [Test]
        public void IndexAt_PastAPartialLastRow_ClampsToThatRowsFirstItem()
        {
            IItemOffsets grid = Grid(itemCount: 7, columns: 3);   // rows: [0,1,2] [3,4,5] [6]

            Assert.That(grid.IndexAt(99999f), Is.EqualTo(6));
        }

        [Test]
        public void IndexAt_PastAFullLastRow_ClampsToThatRowsFirstItem()
        {
            IItemOffsets grid = Grid(itemCount: 9, columns: 3);   // rows: [0,1,2] [3,4,5] [6,7,8]

            Assert.That(grid.IndexAt(99999f), Is.EqualTo(6),
                "the last row starts at 6 — clamping to 8 would hand the pump a mid-row anchor");
        }

        // ---- degenerate shapes ---------------------------------------------------------------------

        [Test]
        public void FewerItemsThanColumns_IsOneRow()
        {
            IItemOffsets grid = Grid(itemCount: 2, columns: 5);

            Assert.That(grid.Count, Is.EqualTo(2));
            Assert.That(grid.OffsetOf(0), Is.EqualTo(0f));
            Assert.That(grid.OffsetOf(1), Is.EqualTo(0f));
            Assert.That(grid.TotalSize, Is.EqualTo(RowHeight).Within(0.001f));
        }

        [Test]
        public void Empty_AnswersWithoutThrowing()
        {
            IItemOffsets grid = Grid(itemCount: 0, columns: 3);

            Assert.That(grid.Count, Is.Zero);
            Assert.That(grid.TotalSize, Is.Zero);
            Assert.That(grid.MinStride, Is.Zero);
            Assert.That(grid.IndexAt(0f), Is.Zero);
            Assert.That(grid.IndexAt(500f), Is.Zero);
        }

        [Test]
        public void ColumnCountBelowOne_IsTreatedAsOne()
        {
            IItemOffsets grid = new GridOffsets(new UniformOffsets(4, RowHeight, Spacing), 0, 4);

            Assert.That(grid.ItemsPerStride, Is.EqualTo(1));
            Assert.That(grid.OffsetOf(1), Is.EqualTo(Stride).Within(0.001f));
        }

        [Test]
        public void NegativeIndex_DoesNotThrow()
        {
            IItemOffsets grid = Grid(itemCount: 9, columns: 3);

            Assert.That(grid.OffsetOf(-1), Is.EqualTo(0f));
            Assert.That(grid.SizeOf(-1), Is.EqualTo(RowHeight));
        }
    }
}
