using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    /// <summary>
    /// The claims that justify the control existing at all: cell count tracks the viewport and not
    /// the data, scrolling reuses instead of instantiating, and a jump does not walk the window
    /// across everything it skipped. These run in PlayMode because they need a real ScrollRect and
    /// real frames — the EditMode suite covers the same decisions as pure functions.
    /// </summary>
    public class RecyclerViewVirtualizationTests
    {
        private RecyclerViewHarness _harness;

        [TearDown]
        public void TearDown() => _harness?.Destroy();

        [UnityTest]
        public IEnumerator CellCount_TracksTheViewport_NotTheItemCount()
        {
            _harness = RecyclerViewHarness.Build();
            _harness.UseDefaultProvider();

            _harness.View.SetItemCount(10000);
            yield return null;

            Assert.Less(_harness.View.ShownIndices.Count, 20,
                "a 10k list must not realise anything close to 10k cells");
            Assert.Greater(_harness.View.ShownIndices.Count, 4,
                "the 500px viewport with 100px cells must be covered");
            Assert.AreEqual(_harness.View.ShownIndices.Count, _harness.InstantiatedCells,
                "no cell should exist beyond the ones currently shown");
        }

        [UnityTest]
        public IEnumerator ShownIndices_StayContiguousAndAscendingWhileScrolling()
        {
            _harness = RecyclerViewHarness.Build();
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(500);
            yield return null;

            for (float offset = 0f; offset <= 3000f; offset += 137f)
            {
                _harness.ScrollTo(offset);
                yield return null;

                var shown = _harness.View.ShownIndices;
                Assert.Greater(shown.Count, 0, $"window emptied at offset {offset}");

                for (int i = 1; i < shown.Count; i++)
                {
                    Assert.AreEqual(shown[i - 1] + 1, shown[i],
                        $"window has a gap at offset {offset}: {string.Join(",", shown)}");
                }
            }
        }

        [UnityTest]
        public IEnumerator Scrolling_ReusesCellsInsteadOfInstantiating()
        {
            _harness = RecyclerViewHarness.Build();
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(1000);
            yield return null;

            // The window settles wider than the initial fill: cells are created once the edge is
            // within CreateDistance (200) but not recycled until RecycleDistance (300), so steady
            // state holds (500 + 2 * 300) / 100 = 11 cells against the first fill's 7. That growth
            // is the hysteresis working, not a leak — so warm up to steady state before measuring.
            for (float offset = 0f; offset <= 1000f; offset += 100f)
            {
                _harness.ScrollTo(offset);
                yield return null;
            }

            int steadyState = _harness.InstantiatedCells;
            Assert.Less(steadyState, 15, "steady-state window is bounded by the recycle band");

            for (float offset = 1000f; offset <= 6000f; offset += 100f)
            {
                _harness.ScrollTo(offset);
                yield return null;
            }

            Assert.AreEqual(steadyState, _harness.InstantiatedCells,
                "scrolling another 50 screens must not instantiate a single extra cell");
            Assert.Greater(_harness.BindCalls.Count, steadyState * 3,
                "cells were rebound as they recycled, so bind calls must far outnumber instances");
        }

        [UnityTest]
        public IEnumerator Jump_ReseedsInsteadOfWalkingTheWindowAcrossTheGap()
        {
            _harness = RecyclerViewHarness.Build();
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(10000);
            yield return null;

            _harness.BindCalls.Clear();
            _harness.ScrollTo(500000f); // index ~5000
            yield return null;

            Assert.Less(_harness.BindCalls.Count, 40,
                "a jump must reseed, not bind its way across the 5000 items it skipped");
            CollectionAssert.Contains(_harness.View.ShownIndices, 5000);
        }

        /// <summary>
        /// Regression for a fixed iteration cap of 64. A tall viewport with small rows needs ~77
        /// creates to fill from a reseed; the pump used to log an error and abandon the tick, leaving
        /// a permanently under-filled list. Nothing about it was visible in the old EditMode suite,
        /// whose FakeWindow only ever used 100px cells in a 500px viewport.
        /// </summary>
        [UnityTest]
        public IEnumerator DenseList_OnATallViewport_FillsCompletely()
        {
            _harness = RecyclerViewHarness.Build(viewportSize: 1920f, cellSize: 30f);
            _harness.UseDefaultProvider();

            _harness.View.SetItemCount(10000);
            yield return null;

            int needed = Mathf.CeilToInt(1920f / 30f);
            Assert.GreaterOrEqual(_harness.View.ShownIndices.Count, needed,
                $"viewport fits {needed} rows but only {_harness.View.ShownIndices.Count} were realised — " +
                "the pump gave up before filling it");
        }

        [UnityTest]
        public IEnumerator EveryDirection_PlacesCellsAtUniformStride()
        {
            foreach (ScrollDirection direction in System.Enum.GetValues(typeof(ScrollDirection)))
            {
                _harness?.Destroy();
                _harness = RecyclerViewHarness.Build(cellSize: 100f, spacing: 10f, direction: direction);
                _harness.UseDefaultProvider();
                _harness.View.SetItemCount(200);
                yield return null;

                foreach (int index in _harness.View.ShownIndices)
                {
                    Assert.AreEqual(index * 110f, _harness.CellOffsetOf(index), 0.01f,
                        $"{direction}: cell {index} is not at its stride position");
                }
            }
        }
    }
}
