using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    /// <summary>
    /// The provider contract and the public mutators. Every violation here is one that would
    /// otherwise leak a cell or corrupt the window silently — none of them throws on its own, which
    /// is exactly why the view has to refuse them loudly.
    /// </summary>
    public class RecyclerViewContractTests
    {
        private RecyclerViewHarness _harness;

        [TearDown]
        public void TearDown() => _harness?.Destroy();

        [Test]
        public void Provider_ReturningNull_IsRefused()
        {
            _harness = RecyclerViewHarness.Build();
            _harness.UseProvider(_ => null);

            Assert.Throws<InvalidOperationException>(() => _harness.View.SetItemCount(10));
            _harness.Freeze();
        }

        [Test]
        public void Provider_NotRentingItsCell_IsRefused()
        {
            _harness = RecyclerViewHarness.Build();
            var stray = new GameObject("Stray", typeof(RectTransform)).AddComponent<TestCell>();

            _harness.UseProvider(_ => stray);

            Assert.Throws<InvalidOperationException>(() => _harness.View.SetItemCount(10),
                "a cell the view never rented could never be recycled");

            _harness.Freeze();
            UnityEngine.Object.DestroyImmediate(stray.gameObject);
        }

        [Test]
        public void Provider_RentingTwiceForOneIndex_IsRefused()
        {
            _harness = RecyclerViewHarness.Build();
            _harness.UseProvider(_ =>
            {
                _harness.View.RentCell<TestCell>(0);
                return _harness.View.RentCell<TestCell>(0); // the first is now untracked
            });

            Assert.Throws<InvalidOperationException>(() => _harness.View.SetItemCount(10));
            _harness.Freeze();
        }

        [Test]
        public void RentCell_OutsideTheProvider_IsRefused()
        {
            _harness = RecyclerViewHarness.Build();

            Assert.Throws<InvalidOperationException>(() => _harness.View.RentCell<TestCell>(0));
        }

        [Test]
        public void RentCell_WithAnUnknownPrefabId_IsRefused()
        {
            _harness = RecyclerViewHarness.Build();
            _harness.UseProvider(_ => _harness.View.RentCell<TestCell>(7));

            Assert.Throws<ArgumentOutOfRangeException>(() => _harness.View.SetItemCount(1));
            _harness.Freeze();
        }

        /// <summary>
        /// Ordering used to matter silently: a provider installed after the count left the list
        /// blank until something else happened to tick it.
        /// </summary>
        [Test]
        public void SetCellProvider_AfterSetItemCount_FillsWithoutWaitingForAFrame()
        {
            _harness = RecyclerViewHarness.Build();

            _harness.View.SetItemCount(100);
            Assert.IsEmpty(_harness.View.ShownIndices, "nothing can bind before a provider exists");

            _harness.UseDefaultProvider();

            Assert.IsNotEmpty(_harness.View.ShownIndices,
                "installing the provider must fill the list immediately, not next Update");
        }

        [UnityTest]
        public IEnumerator SetItemCount_Zero_ReleasesEveryCell()
        {
            _harness = RecyclerViewHarness.Build();
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(100);
            yield return null;

            Assert.IsNotEmpty(_harness.View.ShownIndices);

            _harness.View.SetItemCount(0);
            yield return null;

            Assert.IsEmpty(_harness.View.ShownIndices);
            Assert.AreEqual(0, _harness.Content.GetComponentsInChildren<TestCell>(false).Length,
                "released cells must be deactivated, not left on screen");
        }

        [UnityTest]
        public IEnumerator SetItemCount_Shrinking_DropsIndicesPastTheNewEnd()
        {
            _harness = RecyclerViewHarness.Build();
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(1000);
            _harness.ScrollTo(50000f);
            yield return null;

            _harness.View.SetItemCount(5);
            yield return null;

            foreach (int index in _harness.View.ShownIndices)
                Assert.Less(index, 5, "a shown index survived past the new item count");
        }

        [UnityTest]
        public IEnumerator RefreshIndex_RebindsOnlyThatIndex()
        {
            _harness = RecyclerViewHarness.Build();
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(100);
            yield return null;

            int target = _harness.View.ShownIndices[1];
            _harness.BindCalls.Clear();

            _harness.View.RefreshIndex(target);

            CollectionAssert.AreEqual(new[] { target }, _harness.BindCalls);
        }

        [UnityTest]
        public IEnumerator RefreshIndex_ForAnIndexNotShown_DoesNothing()
        {
            _harness = RecyclerViewHarness.Build();
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(1000);
            yield return null;

            _harness.BindCalls.Clear();
            _harness.View.RefreshIndex(900);

            CollectionAssert.IsEmpty(_harness.BindCalls);
        }

        [UnityTest]
        public IEnumerator ScrollToIndex_BringsThatIndexIntoTheWindow()
        {
            _harness = RecyclerViewHarness.Build();
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(1000);
            yield return null;

            _harness.View.ScrollToIndex(400);
            yield return null;

            CollectionAssert.Contains(_harness.View.ShownIndices, 400);
        }

        [UnityTest]
        public IEnumerator ForEachShownCell_VisitsEveryLiveCellExactlyOnce()
        {
            _harness = RecyclerViewHarness.Build();
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(100);
            yield return null;

            var visited = new List<int>();
            _harness.View.ForEachShownCell(cell => visited.Add(cell.Index));

            CollectionAssert.AreEqual(_harness.View.ShownIndices, visited);
        }

        [UnityTest]
        public IEnumerator TotalSize_ExcludesTheTrailingSpacing()
        {
            _harness = RecyclerViewHarness.Build(cellSize: 100f, spacing: 10f);
            _harness.UseDefaultProvider();

            _harness.View.SetItemCount(10);
            yield return null;

            // 10 cells of 100 with 9 gaps of 10 between them.
            Assert.AreEqual(1090f, _harness.View.TotalSize, 0.01f);
        }
    }
}
