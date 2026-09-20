using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    /// <summary>
    /// A grid is the same list folded into rows, so the claims worth checking in a real scene are
    /// the ones the offset table cannot make on its own: that columns land in different places, that
    /// a recycled cell adopts its new column, and that a view left at one column is untouched.
    ///
    /// <para>PlayMode because these need a real <c>ScrollRect</c> and real frames; the arithmetic
    /// behind them is covered as pure functions in <c>GridOffsetsTests</c> and
    /// <c>RecycleWindowGridBudgetTests</c>.</para>
    /// </summary>
    public class RecyclerViewGridTests
    {
        private RecyclerViewHarness _harness;

        [TearDown]
        public void TearDown() => _harness?.Destroy();

        [UnityTest]
        public IEnumerator ItemsInARow_ShareAnAlongPositionAndDifferInTheCross()
        {
            _harness = RecyclerViewHarness.Build(crossAxisCount: 3);
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(60);
            yield return null;

            float c0 = _harness.CellCrossCentreOf(0);
            float c1 = _harness.CellCrossCentreOf(1);
            float c2 = _harness.CellCrossCentreOf(2);

            Assert.That(c0, Is.Not.NaN, "the first row must be realised");
            Assert.That(c1, Is.GreaterThan(c0), "column 1 sits after column 0 across the axis");
            Assert.That(c2, Is.GreaterThan(c1), "column 2 sits after column 1");

            // Same row: identical along-axis position. Read from the rects, not the offset table.
            Assert.That(_harness.RectOf(1).localPosition.y,
                Is.EqualTo(_harness.RectOf(0).localPosition.y).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator TheNextRow_StartsAgainAtTheFirstColumn()
        {
            _harness = RecyclerViewHarness.Build(crossAxisCount: 3);
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(60);
            yield return null;

            Assert.That(_harness.CellCrossCentreOf(3),
                Is.EqualTo(_harness.CellCrossCentreOf(0)).Within(0.01f),
                "item 3 opens the second row, so it shares column 0 with item 0");

            Assert.That(_harness.RectOf(3).localPosition.y,
                Is.LessThan(_harness.RectOf(0).localPosition.y),
                "and it sits one row further down");
        }

        /// <summary>
        /// The trap that only a real pool can show. Anchors are written once per Instantiate, so a
        /// cell first created for column 0 and later recycled into column 2 would keep column 0's
        /// anchors and pile up on top of its neighbour — invisible to any test that never scrolls.
        /// </summary>
        /// <summary>
        /// Anchors are written once per Instantiate, so a cell first created for column 0 and later
        /// recycled into column 2 would keep column 0's anchors and pile up on its neighbour.
        ///
        /// <para>Asserted as an invariant over <b>every</b> shown cell rather than on two hand-picked
        /// indices: which pooled instance lands on which index depends on the pool's pop order, so a
        /// spot check can go green by luck the moment the item count or the viewport changes.</para>
        /// </summary>
        [UnityTest]
        public IEnumerator EveryShownCell_SitsInTheColumnItsIndexImplies()
        {
            const int columns = 3;
            _harness = RecyclerViewHarness.Build(viewportSize: 600f, crossAxisCount: columns);
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(600);
            yield return null;

            // Jump far enough that the window is rebuilt entirely from pooled cells.
            _harness.View.ScrollToIndex(300);
            yield return null;
            yield return null;

            AssertEveryCellIsInItsColumn(columns, 600f);

            // And again after a scroll back, which recycles in the other direction.
            _harness.View.ScrollToIndex(120);
            yield return null;
            yield return null;

            AssertEveryCellIsInItsColumn(columns, 600f);
        }

        private void AssertEveryCellIsInItsColumn(int columns, float contentCross)
        {
            float columnWidth = contentCross / columns;

            foreach (int index in _harness.View.ShownIndices)
            {
                int column = index % columns;

                // Centre of the column band, in the content's own centred space.
                float expected = (column + 0.5f) * columnWidth - contentCross / 2f;

                Assert.That(_harness.CellCrossCentreOf(index), Is.EqualTo(expected).Within(0.5f),
                    $"index {index} belongs in column {column}");
            }
        }

        [UnityTest]
        public IEnumerator ColumnWidth_IsTheViewportDividedByTheColumnCount()
        {
            _harness = RecyclerViewHarness.Build(viewportSize: 600f, crossAxisCount: 3);
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(60);
            yield return null;

            Assert.That(_harness.CellCrossSizeOf(0), Is.EqualTo(200f).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator CrossSpacing_InsetsEachColumn()
        {
            _harness = RecyclerViewHarness.Build(viewportSize: 600f, crossAxisCount: 3, crossSpacing: 20f);
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(60);
            yield return null;

            Assert.That(_harness.CellCrossSizeOf(0), Is.EqualTo(180f).Within(0.5f),
                "200px column minus the 20px gap");
        }

        [UnityTest]
        public IEnumerator ContentSize_CountsRowsNotItems()
        {
            _harness = RecyclerViewHarness.Build(cellSize: 100f, crossAxisCount: 3);
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(30);   // ten rows
            yield return null;

            // The single most expensive thing to get wrong: at 30 items the content would be three
            // times too tall, and the list would scroll into two thirds of nothing.
            Assert.That(_harness.ContentSize, Is.EqualTo(1000f).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator ShownIndices_StayContiguousAndAscending()
        {
            _harness = RecyclerViewHarness.Build(crossAxisCount: 4);
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(800);
            yield return null;

            _harness.ScrollTo(2000f);
            yield return null;

            var shown = _harness.View.ShownIndices;
            Assert.That(shown.Count, Is.GreaterThan(0));
            for (int i = 1; i < shown.Count; i++)
            {
                Assert.That(shown[i], Is.EqualTo(shown[i - 1] + 1),
                    "folding items into rows must not change that the window is one contiguous run");
            }
        }

        /// <summary>
        /// The budget defect, seen from the outside: a grid whose iteration budget counted rows
        /// instead of cells logs "failed to converge" and leaves the window short. Asserting on the
        /// cell count catches it without depending on the message.
        /// </summary>
        [UnityTest]
        public IEnumerator AWideGrid_FillsItsViewportInOneTick()
        {
            _harness = RecyclerViewHarness.Build(viewportSize: 500f, cellSize: 100f, crossAxisCount: 5);
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(5000);
            yield return null;

            // Exact, not a floor. A 500px viewport with 200px create bands spans offsets 0..700,
            // which is rows 0-6 at 100px each: seven rows of five cells. A ">" threshold passes on a
            // window that stopped short and topped up on later frames, which is the very symptom of
            // a budget that counted rows instead of cells.
            Assert.That(_harness.View.ShownIndices.Count, Is.EqualTo(35));
        }

        /// <summary>
        /// The last row must be whole. Every item in a row shares one offset, so the create band is
        /// satisfied by that row's first cell — and at a create distance smaller than a row, the
        /// half-empty row is on screen rather than safely below it.
        /// </summary>
        [UnityTest]
        public IEnumerator TheTrailingRow_IsRealisedInFull()
        {
            const int columns = 4;
            _harness = RecyclerViewHarness.Build(viewportSize: 400f, cellSize: 100f, crossAxisCount: columns);
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(4000);
            yield return null;

            Assert.That(_harness.View.ShownIndices.Count % columns, Is.Zero,
                "the window settled on a fraction of a row");
        }

        [UnityTest]
        public IEnumerator SetCrossAxisCount_RelaysTheGridOut()
        {
            _harness = RecyclerViewHarness.Build(cellSize: 100f, crossAxisCount: 1);
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(30);
            yield return null;

            Assert.That(_harness.ContentSize, Is.EqualTo(3000f).Within(0.5f), "30 rows as a list");

            _harness.View.SetCrossAxisCount(3);
            yield return null;

            Assert.That(_harness.View.CrossAxisCount, Is.EqualTo(3));
            Assert.That(_harness.ContentSize, Is.EqualTo(1000f).Within(0.5f), "ten rows as a grid");
            Assert.That(_harness.View.ShownIndices.Count, Is.EqualTo(_harness.InstantiatedCells),
                "no cell may survive the relayout without being tracked");
        }

        /// <summary>
        /// Going back to one column must undo the grid geometry on cells that already exist. The
        /// fractional column anchors are written per bind and survive recycling, so without an
        /// explicit reset every reused cell keeps a third of the width and sits off-centre — and the
        /// column-width check skips lists, so nothing would say a word.
        /// </summary>
        [UnityTest]
        public IEnumerator SetCrossAxisCount_BackToOne_RestoresFullWidthCells()
        {
            _harness = RecyclerViewHarness.Build(viewportSize: 600f, cellSize: 100f, crossAxisCount: 3);
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(300);
            yield return null;

            Assert.That(_harness.CellCrossSizeOf(0), Is.EqualTo(200f).Within(0.5f), "three columns");

            // Scroll first so the pool is full of cells that were laid out as grid cells; the
            // relayout then has to repair reused instances, not just freshly instantiated ones.
            _harness.View.ScrollToIndex(150);
            yield return null;

            _harness.View.SetCrossAxisCount(1);
            yield return null;

            foreach (int index in _harness.View.ShownIndices)
            {
                Assert.That(_harness.CellCrossSizeOf(index), Is.EqualTo(600f).Within(0.5f),
                    $"index {index} kept a grid column width after the view became a list");
                Assert.That(_harness.CellCrossCentreOf(index), Is.EqualTo(0f).Within(0.5f),
                    $"index {index} is still offset into a column band");
            }
        }

        /// <summary>
        /// Column 0 is the first column in both orientations. A horizontal list scrolls along X and
        /// stacks its columns along Y, which grows upward — so the bands have to be mirrored or the
        /// grid reads bottom-to-top.
        /// </summary>
        [UnityTest]
        public IEnumerator HorizontalGrid_PutsColumnZeroFirst()
        {
            _harness = RecyclerViewHarness.Build(
                viewportSize: 600f, cellSize: 100f, direction: ScrollDirection.LeftToRight,
                crossAxisCount: 3);
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(300);
            yield return null;

            // Cross axis is Y here, so "first" means the highest Y.
            Assert.That(_harness.CellCrossCentreOf(0), Is.GreaterThan(_harness.CellCrossCentreOf(1)),
                "column 0 must sit above column 1 in a horizontal grid");
            Assert.That(_harness.CellCrossCentreOf(1), Is.GreaterThan(_harness.CellCrossCentreOf(2)));
            Assert.That(_harness.CellCrossSizeOf(0), Is.EqualTo(200f).Within(0.5f));
        }

        [Test]
        public void SetCrossAxisCount_RejectsZero()
        {
            _harness = RecyclerViewHarness.Build();

            Assert.Throws<System.ArgumentOutOfRangeException>(() => _harness.View.SetCrossAxisCount(0));
        }

        /// <summary>
        /// The regression anchor. A view left at one column must be laid out by the same code path it
        /// always was — not an equivalent one.
        /// </summary>
        [UnityTest]
        public IEnumerator OneColumn_PlacesCellsExactlyAsBefore()
        {
            _harness = RecyclerViewHarness.Build(viewportSize: 500f, cellSize: 100f, crossAxisCount: 1);
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(50);
            yield return null;

            Assert.That(_harness.View.CrossAxisCount, Is.EqualTo(1));
            Assert.That(_harness.ContentSize, Is.EqualTo(5000f).Within(0.5f));
            Assert.That(_harness.CellSizeOf(0), Is.EqualTo(100f).Within(0.5f));

            // Full-width cells: the cross size is the viewport's, not a fraction of it.
            Assert.That(_harness.CellCrossSizeOf(0), Is.EqualTo(500f).Within(0.5f));
            Assert.That(_harness.CellCrossCentreOf(0), Is.EqualTo(0f).Within(0.01f));
        }
    }
}
