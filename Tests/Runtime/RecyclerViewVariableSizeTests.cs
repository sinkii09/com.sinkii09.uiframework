using System;
using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    /// <summary>
    /// Per-item declared sizes, end to end against a real ScrollRect.
    ///
    /// <para>Every case here runs at <b>non-zero spacing</b> and with sizes that actually differ.
    /// Uniform-size arithmetic reproduces most of this behaviour by accident at spacing 0, so a
    /// suite written that way would pass against the bugs it exists to catch.</para>
    /// </summary>
    public class RecyclerViewVariableSizeTests
    {
        private const float Spacing = 10f;
        private const float Small = 40f;
        private const float Large = 200f;

        private RecyclerViewHarness _harness;

        [TearDown]
        public void TearDown() => _harness?.Destroy();

        /// <summary>Alternating short/tall rows — index parity decides the size.</summary>
        private static float MixedSize(int index) => index % 2 == 0 ? Small : Large;

        private static float ExpectedOffset(int index)
        {
            float offset = 0f;
            for (int i = 0; i < index; i++) offset += MixedSize(i) + Spacing;
            return offset;
        }

        private RecyclerViewHarness BuildMixed(int itemCount, int prefabCount = 1)
        {
            _harness = RecyclerViewHarness.Build(spacing: Spacing, prefabCount: prefabCount);
            _harness.UseDefaultProvider();
            _harness.View.SetItemSizeProvider(MixedSize);
            _harness.View.SetItemCount(itemCount);
            return _harness;
        }

        [UnityTest]
        public IEnumerator Cells_TileWithoutGapOrOverlap()
        {
            BuildMixed(200);
            yield return null;

            foreach (int index in _harness.View.ShownIndices)
            {
                Assert.AreEqual(ExpectedOffset(index), _harness.CellOffsetOf(index), 0.5f,
                    $"offset of index {index}");
                Assert.AreEqual(MixedSize(index), _harness.CellSizeOf(index), 0.5f,
                    $"rendered size of index {index}");
            }
        }

        [UnityTest]
        public IEnumerator Scrolling_ReusesCellsAtTheRightSize()
        {
            BuildMixed(500);
            yield return null;

            // Far enough that every cell on screen has been through the pool at least once.
            _harness.ScrollTo(6000f);
            yield return null;
            _harness.ScrollTo(12000f);
            yield return null;

            foreach (int index in _harness.View.ShownIndices)
            {
                Assert.AreEqual(MixedSize(index), _harness.CellSizeOf(index), 0.5f,
                    $"index {index} kept a recycled cell's previous size");
            }
        }

        /// <summary>
        /// Sizing used to happen once per Instantiate, so a pooled cell carried the size of whichever
        /// index first created it. Recycling a short row's cell onto a tall row rendered it short and
        /// reported nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator ACellRecycledFromASmallIndexRendersTheLargeSize()
        {
            BuildMixed(500);
            yield return null;

            // Several jumps, not one: a single jump reseeds (release-all, rebuild from one cell), so
            // it binds barely more than a window's worth and cannot demonstrate reuse on its own.
            // Across repeated visits the pool must serve them all without growing per visit.
            foreach (float offset in new[] { 8000f, 2000f, 14000f, 500f, 11000f })
            {
                _harness.ScrollTo(offset);
                yield return null;

                foreach (int index in _harness.View.ShownIndices)
                {
                    Assert.AreEqual(MixedSize(index), _harness.CellSizeOf(index), 0.5f,
                        $"index {index} at offset {offset} kept a recycled cell's previous size");
                }
            }

            Assert.Greater(_harness.BindCalls.Count, _harness.InstantiatedCells * 2,
                "cells were instantiated rather than recycled, so the size assertions prove nothing about reuse");
        }

        /// <summary>
        /// Installing a provider on a populated list has to rebuild the content rect and every live
        /// cell's cached offset. Phase 1 only ever invalidated on SetItemCount, because count was the
        /// only thing that could move an offset — so a bare Pump() here left both stale.
        /// </summary>
        [UnityTest]
        public IEnumerator SetItemSizeProvider_OnAPopulatedList_RebuildsContentAndOffsets()
        {
            _harness = RecyclerViewHarness.Build(spacing: Spacing);
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(300);
            yield return null;

            float uniformTotal = _harness.View.TotalSize;

            _harness.View.SetItemSizeProvider(MixedSize);
            yield return null;

            float expectedTotal = 0f;
            for (int i = 0; i < 300; i++) expectedTotal += MixedSize(i) + Spacing;
            expectedTotal -= Spacing;

            Assert.AreNotEqual(uniformTotal, _harness.View.TotalSize, "the table did not change");
            Assert.AreEqual(expectedTotal, _harness.View.TotalSize, 0.5f, "TotalSize");
            Assert.AreEqual(expectedTotal, _harness.ContentSize, 0.5f,
                "the content rect still spans the old total, so the scroll extent is wrong");

            foreach (int index in _harness.View.ShownIndices)
                Assert.AreEqual(ExpectedOffset(index), _harness.CellOffsetOf(index), 0.5f, $"index {index}");
        }

        [UnityTest]
        public IEnumerator SetItemSizeProvider_Null_RestoresUniformSizing()
        {
            BuildMixed(300);
            yield return null;

            _harness.View.SetItemSizeProvider(null);
            yield return null;

            // 300 cells of 100 with 299 gaps of 10.
            Assert.AreEqual(300 * 110f - Spacing, _harness.View.TotalSize, 0.5f);
            foreach (int index in _harness.View.ShownIndices)
                Assert.AreEqual(100f, _harness.CellSizeOf(index), 0.5f, $"index {index}");
        }

        /// <summary>
        /// Rebind is a second bind path, and it re-rents — on a multi-prefab list the replacement
        /// comes from a different pool tier carrying whatever size its previous index had. A
        /// single-prefab version of this test passes against the bug.
        /// </summary>
        [UnityTest]
        public IEnumerator RefreshIndex_OnAMultiPrefabList_KeepsTheDeclaredSize()
        {
            _harness = RecyclerViewHarness.Build(spacing: Spacing, prefabCount: 2);
            _harness.View.SetCellProvider(index =>
            {
                _harness.BindCalls.Add(index);
                return _harness.View.RentCell<TestCell>(index % 2); // prefab id tracks parity
            });
            _harness.View.SetItemSizeProvider(MixedSize);
            _harness.View.SetItemCount(300);
            yield return null;

            int target = _harness.View.ShownIndices[2];

            _harness.View.RefreshIndex(target);
            yield return null;

            Assert.AreEqual(MixedSize(target), _harness.CellSizeOf(target), 0.5f,
                "Rebind handed back a pooled cell without writing the declared size");
            Assert.AreEqual(ExpectedOffset(target), _harness.CellOffsetOf(target), 0.5f);
        }

        [UnityTest]
        public IEnumerator ScrollToIndex_AlignsAgainstThatIndexOwnSize()
        {
            BuildMixed(500);
            yield return null;

            const float viewportSize = 500f;
            const int target = 301; // a Large row, so its size differs from the 100f default

            _harness.View.ScrollToIndex(target, 1f);
            yield return null;

            CollectionAssert.Contains(_harness.View.ShownIndices, target);

            // alignment 1 parks the row's trailing edge on the viewport's trailing edge. Using a
            // global cell size here lands short or long by (row size - default size) — 100px on this
            // row, which is why the row picked is a Large one.
            float expected = ExpectedOffset(target) - (viewportSize - MixedSize(target));
            Assert.AreEqual(expected, ViewportStart(), 0.5f,
                "trailing-edge alignment used a global cell size instead of this row's");
        }

        private float ViewportStart()
        {
            ScrollAxis axis = ScrollAxis.From(_harness.Direction);
            return axis.ViewportStart(_harness.Content.anchoredPosition);
        }

        [UnityTest]
        public IEnumerator SetItemSize_MovesLaterCellsAndLeavesEarlierPositionsAlone()
        {
            BuildMixed(300);
            yield return null;

            int changed = _harness.View.ShownIndices[2];

            // A modest growth on purpose. Expanding a row to the full viewport height pushes every
            // later row out of the window, leaving nothing after the change to verify the shift on.
            float newSize = MixedSize(changed) + 60f;
            float delta = newSize - MixedSize(changed);

            _harness.View.SetItemSize(changed, newSize);
            yield return null;

            // Assert over whatever is shown *after* the change rather than indices captured before
            // it: growing a row by 300px evicts later rows from the viewport, so a pre-chosen "later"
            // index may simply no longer exist. Positions, not instances — the release/pump sequence
            // re-binds everything, so asserting the same cell object survived would fail against
            // correct code.
            int seenAfter = 0;
            foreach (int index in _harness.View.ShownIndices)
            {
                float expected = ExpectedOffset(index) + (index > changed ? delta : 0f);
                Assert.AreEqual(expected, _harness.CellOffsetOf(index), 0.5f,
                    index > changed ? $"index {index} did not shift" : $"index {index} moved but precedes the change");
                if (index > changed) seenAfter++;
            }

            Assert.AreEqual(newSize, _harness.CellSizeOf(changed), 0.5f);
            Assert.Greater(seenAfter, 0, "no index after the change was shown, so the shift went unverified");
        }

        [UnityTest]
        public IEnumerator SetItemSize_WithNoProviderInstalled_PromotesTheUniformTable()
        {
            _harness = RecyclerViewHarness.Build(spacing: Spacing);
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(100);
            yield return null;

            _harness.View.SetItemSize(1, 400f);
            yield return null;

            Assert.AreEqual(400f, _harness.CellSizeOf(1), 0.5f);
            Assert.AreEqual(100f, _harness.CellSizeOf(0), 0.5f, "other rows keep the uniform size");
            Assert.AreEqual(100 * 110f - Spacing + 300f, _harness.View.TotalSize, 0.5f);
        }

        [Test]
        public void SetItemSize_RejectsAnIndexOutsideTheList()
        {
            _harness = RecyclerViewHarness.Build(spacing: Spacing);
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(10);

            Assert.Throws<ArgumentOutOfRangeException>(() => _harness.View.SetItemSize(10, 50f));
            Assert.Throws<ArgumentOutOfRangeException>(() => _harness.View.SetItemSize(0, 0f));
        }

        [UnityTest]
        public IEnumerator TotalSize_MatchesTheAnalyticSumOverAThousandRows()
        {
            BuildMixed(1000);
            yield return null;

            float expected = 0f;
            for (int i = 0; i < 1000; i++) expected += MixedSize(i) + Spacing;
            expected -= Spacing;

            Assert.AreEqual(expected, _harness.View.TotalSize, 0.5f);
        }

        /// <summary>
        /// One tiny row among tall ones drags MinStride down, which is what the iteration budget has
        /// to be sized against — the window converges at the rate of the smallest advance.
        /// </summary>
        [UnityTest]
        public IEnumerator OneVeryShortRowAmongTallOnes_StillFillsTheViewport()
        {
            const int tinyIndex = 40;
            _harness = RecyclerViewHarness.Build(spacing: Spacing);
            _harness.UseDefaultProvider();
            _harness.View.SetItemSizeProvider(i => i == tinyIndex ? 4f : Large);
            _harness.View.SetItemCount(500);
            yield return null;

            // Scroll to the tiny row. Sitting at offset 0 among 200px rows exercises no part of the
            // budget claim — that window converges the same for any MinStride, so the assertions
            // below would hold even if the budget were computed wrongly.
            _harness.View.ScrollToIndex(tinyIndex);
            yield return null;

            CollectionAssert.Contains(_harness.View.ShownIndices, tinyIndex);
            Assert.AreEqual(4f, _harness.CellSizeOf(tinyIndex), 0.5f);

            float shownSpan = 0f;
            foreach (int index in _harness.View.ShownIndices)
                shownSpan += (index == tinyIndex ? 4f : Large) + Spacing;

            Assert.GreaterOrEqual(shownSpan, 500f,
                "the window stopped short of covering the viewport — the iteration budget ran out");
        }

        [Test]
        public void AThrowingSizeProvider_LeavesThePreviousTableIntact()
        {
            _harness = RecyclerViewHarness.Build(spacing: Spacing);
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(100);

            float before = _harness.View.TotalSize;

            Assert.Throws<InvalidOperationException>(
                () => _harness.View.SetItemSizeProvider(i => i == 50 ? throw new InvalidOperationException("boom") : 100f));

            Assert.AreEqual(before, _harness.View.TotalSize, 0.5f,
                "a provider that threw mid-rebuild left the view holding a partial table");

            // The table surviving is only half of it. If the failed provider was still installed, the
            // view would re-invoke it on every later mutation and throw forever — usable-looking, but
            // permanently wedged. Asserting only on TotalSize stops one step short of that.
            Assert.DoesNotThrow(() => _harness.View.SetItemCount(60),
                "the throwing provider stayed installed and poisoned every later call");
            Assert.AreEqual(60, _harness.View.ItemCount);

            _harness.Freeze();
        }

        /// <summary>
        /// Pins the documented interaction rather than asserting it is desirable: a count change
        /// re-asks the size provider, so per-index overrides do not survive it.
        /// </summary>
        [UnityTest]
        public IEnumerator SetItemSize_IsDiscardedByALaterSetItemCount()
        {
            BuildMixed(300);
            yield return null;

            int changed = _harness.View.ShownIndices[1];
            _harness.View.SetItemSize(changed, 480f);
            yield return null;
            Assert.AreEqual(480f, _harness.CellSizeOf(changed), 0.5f);

            _harness.View.SetItemCount(300);
            yield return null;

            Assert.AreEqual(MixedSize(changed), _harness.CellSizeOf(changed), 0.5f,
                "the provider is the source of truth after a count change");
        }

        /// <summary>
        /// The size provider runs outside the pump, so the pump's own guard does not cover it. Without
        /// a second flag a provider that mutates the list recurses into a rebuild it is already inside.
        /// </summary>
        [Test]
        public void ASizeProviderThatMutatesTheList_IsRefused()
        {
            _harness = RecyclerViewHarness.Build(spacing: Spacing);
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(50);

            LogAssert.Expect(LogType.Error, new Regex("was called from inside a size provider"));

            _harness.View.SetItemSizeProvider(i =>
            {
                if (i == 10) _harness.View.SetItemCount(999); // must be refused, not recursed into
                return 100f;
            });

            Assert.AreEqual(50, _harness.View.ItemCount, "the reentrant mutation was allowed through");
        }

        [Test]
        public void ANonPositiveDeclaredSize_IsRefused()
        {
            _harness = RecyclerViewHarness.Build(spacing: Spacing);
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(100);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => _harness.View.SetItemSizeProvider(i => i == 7 ? 0f : 100f));

            _harness.Freeze();
        }
    }
}
