using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    /// <summary>
    /// <see cref="RecyclerCell.BindCancellationToken"/> and <see cref="RecyclerCell.Index"/> during a
    /// bind — what an async cell needs to drop a result that arrives after the cell moved on to
    /// another item. Every path that ends a bind must cancel the token: scroll-out, RefreshIndex,
    /// a count change, the view going away.
    /// </summary>
    public class RecyclerCellBindTokenTests
    {
        private RecyclerViewHarness _harness;
        private readonly Dictionary<int, CancellationToken> _tokens = new();
        private readonly Dictionary<int, int> _indexSeenInProvider = new();

        [TearDown]
        public void TearDown()
        {
            _harness?.Destroy();
            _tokens.Clear();
            _indexSeenInProvider.Clear();
        }

        private void BuildWithTokenRecordingProvider()
        {
            _harness = RecyclerViewHarness.Build();
            _harness.UseProvider(index =>
            {
                TestCell cell = _harness.View.RentCell<TestCell>(0);
                _indexSeenInProvider[index] = cell.Index;
                _tokens[index] = cell.BindCancellationToken;
                return cell;
            });
        }

        [Test]
        public void Index_IsAlreadyValidInsideTheProvider()
        {
            BuildWithTokenRecordingProvider();
            _harness.View.SetItemCount(10);

            foreach (int index in _harness.View.ShownIndices)
                Assert.AreEqual(index, _indexSeenInProvider[index], "Index was still -1 inside the bind");
        }

        [Test]
        public void TheToken_IsLiveWhileTheCellShowsItsItem()
        {
            BuildWithTokenRecordingProvider();
            _harness.View.SetItemCount(100);

            Assert.IsFalse(_tokens[_harness.View.ShownIndices[0]].IsCancellationRequested);
        }

        [Test]
        public void TheToken_IsCancelled_WhenTheCellScrollsOut()
        {
            BuildWithTokenRecordingProvider();
            _harness.View.SetItemCount(1000);
            CancellationToken first = _tokens[0];

            _harness.View.ScrollToIndex(500);

            Assert.IsTrue(first.IsCancellationRequested, "a cell recycled away from #0 kept a live token");
            Assert.IsFalse(_tokens[500].IsCancellationRequested);
        }

        [Test]
        public void TheToken_IsCancelled_OnRefreshIndex()
        {
            BuildWithTokenRecordingProvider();
            _harness.View.SetItemCount(100);
            int target = _harness.View.ShownIndices[1];
            CancellationToken before = _tokens[target];

            _harness.View.RefreshIndex(target);

            Assert.IsTrue(before.IsCancellationRequested, "the re-bind did not end the previous bind");
            Assert.IsFalse(_tokens[target].IsCancellationRequested, "the new bind got a dead token");
        }

        [Test]
        public void TheToken_IsCancelled_OnSetItemCount()
        {
            BuildWithTokenRecordingProvider();
            _harness.View.SetItemCount(100);
            CancellationToken before = _tokens[0];

            _harness.View.SetItemCount(100);

            Assert.IsTrue(before.IsCancellationRequested);
        }

        [UnityTest]
        public IEnumerator TheToken_IsCancelled_WhenTheViewIsDestroyed()
        {
            BuildWithTokenRecordingProvider();
            _harness.View.SetItemCount(100);
            CancellationToken token = _tokens[0];

            Object.Destroy(_harness.Root);
            yield return null;

            Assert.IsTrue(token.IsCancellationRequested);
        }

        [Test]
        public void TheToken_ReadInsideOnRecycled_IsAlreadyCancelled_AndCreatesNothing()
        {
            var seenInsideOnRecycled = new List<bool>();
            _harness = RecyclerViewHarness.Build();
            _harness.UseProvider(_ =>
            {
                TestCell cell = _harness.View.RentCell<TestCell>(0);
                cell.OnRecycledHook = () => seenInsideOnRecycled.Add(cell.BindCancellationToken.IsCancellationRequested);
                return cell;
            });
            _harness.View.SetItemCount(100);

            _harness.View.SetItemCount(0);

            CollectionAssert.IsNotEmpty(seenInsideOnRecycled);
            CollectionAssert.DoesNotContain(seenInsideOnRecycled, false, "OnRecycled saw a live token");
            foreach (TestCell cell in _harness.Content.GetComponentsInChildren<TestCell>(true))
                Assert.IsNull(BindSourceOf(cell), "reading the token while recycling created a fresh source");
        }

        /// <summary>
        /// The linked source registers on destroyCancellationToken; a recycle that cancelled without
        /// disposing would pile one registration per bind onto a cell that lives as long as the list.
        /// </summary>
        [Test]
        public void ARecycle_DisposesTheBindsSource()
        {
            BuildWithTokenRecordingProvider();
            _harness.View.SetItemCount(100);

            _harness.View.SetItemCount(0);

            foreach (TestCell cell in _harness.Content.GetComponentsInChildren<TestCell>(true))
                Assert.IsNull(BindSourceOf(cell), "a recycled cell still holds its bind's source");
        }

        [Test]
        public void ACellOutsideABind_HasACancelledToken()
        {
            var cell = new GameObject("Unbound", typeof(RectTransform)).AddComponent<TestCell>();

            Assert.IsTrue(cell.BindCancellationToken.IsCancellationRequested);
            Object.DestroyImmediate(cell.gameObject);
        }

        [Test]
        public void ACellThatNeverReadsTheToken_AllocatesNothing()
        {
            _harness = RecyclerViewHarness.Build();
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(100);

            _harness.View.ForEachShownCell(cell => Assert.IsNull(BindSourceOf(cell)));
        }

        private static object BindSourceOf(RecyclerCell cell) =>
            typeof(RecyclerCell).GetField("_bindCancellation", BindingFlags.Instance | BindingFlags.NonPublic)
                                .GetValue(cell);
    }
}
