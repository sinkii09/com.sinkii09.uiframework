using System;
using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    /// <summary>
    /// Consumer code throwing — the provider, OnRecycled, the size provider. The exception is the
    /// consumer's to see; the view's job is that nothing else breaks with it. Before v3.7 each of
    /// these left an active cell on screen that nothing tracked, and the provider case repeated it
    /// every frame.
    /// </summary>
    public class RecyclerViewExceptionSafetyTests
    {
        private RecyclerViewHarness _harness;
        private int _poison = -1;

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            _harness?.Destroy();
            _poison = -1;
        }

        /// <summary>The usual provider shape: rent first, then bind data that turns out to be bad.</summary>
        private void UseProviderThatFailsAfterRenting()
        {
            _harness.UseProvider(index =>
            {
                TestCell cell = _harness.View.RentCell<TestCell>(0);
                if (index == _poison) throw new InvalidOperationException("bind failed");
                return cell;
            });
        }

        [UnityTest]
        public IEnumerator AProviderThrowingAfterRent_EveryFrame_LeaksNoCells()
        {
            _harness = RecyclerViewHarness.Build();
            UseProviderThatFailsAfterRenting();
            _harness.View.SetItemCount(100);
            yield return null;

            _poison = _harness.View.ShownIndices[2];
            Assert.Throws<InvalidOperationException>(() => _harness.View.RefreshAll());

            // Update re-throws every frame until the provider is fixed; that is expected here.
            LogAssert.ignoreFailingMessages = true;
            yield return null;
            int instances = _harness.InstantiatedCells;

            // More frames than the pool holds: a leak drains pooled cells first and only then starts
            // instantiating, so a short run would hide it from this count.
            for (int i = 0; i < 20; i++) yield return null;

            Assert.AreEqual(instances, _harness.InstantiatedCells, "a cell was instantiated per failing frame");
            RecyclerWindowAssert.Intact(_harness);

            int poisoned = _poison;
            _poison = -1;
            yield return null;

            RecyclerWindowAssert.Intact(_harness);
            CollectionAssert.Contains(_harness.View.ShownIndices, poisoned, "the list did not recover");
        }

        [UnityTest]
        public IEnumerator ARebindThatThrows_KeepsTheWindowWhole()
        {
            _harness = RecyclerViewHarness.Build();
            UseProviderThatFailsAfterRenting();
            _harness.View.SetItemCount(100);
            yield return null;

            int target = _harness.View.ShownIndices[2];
            _poison = target;
            Assert.Throws<InvalidOperationException>(() => _harness.View.RefreshIndex(target));

            // Checked before any pump runs: a hole or a ghost here is what the next frame inherits.
            RecyclerWindowAssert.Intact(_harness);

            _poison = -1;
            yield return null;

            RecyclerWindowAssert.Intact(_harness);
            CollectionAssert.Contains(_harness.View.ShownIndices, target);
        }

        [Test]
        public void AReseedThatThrows_LeavesNoStaleCellsOnScreen()
        {
            _harness = RecyclerViewHarness.Build();
            _harness.UseProvider(index =>
            {
                TestCell cell = _harness.View.RentCell<TestCell>(0);
                if (index >= 500 && index < 520) throw new InvalidOperationException("bind failed");
                return cell;
            });
            _harness.View.SetItemCount(1000);

            Assert.Throws<InvalidOperationException>(() => _harness.View.ScrollToIndex(500));

            RecyclerWindowAssert.Intact(_harness);
            _harness.Freeze();
        }

        [UnityTest]
        public IEnumerator OnRecycledThrowing_IsLogged_AndEveryCellStillGoesBack()
        {
            bool throwOnce = false;
            _harness = RecyclerViewHarness.Build();
            _harness.UseProvider(_ =>
            {
                TestCell cell = _harness.View.RentCell<TestCell>(0);
                cell.OnRecycledHook = () =>
                {
                    if (!throwOnce) return;
                    throwOnce = false;
                    throw new InvalidOperationException("recycle failed");
                };
                return cell;
            });
            _harness.View.SetItemCount(100);
            yield return null;

            throwOnce = true;
            LogAssert.Expect(LogType.Exception, new Regex("recycle failed"));
            _harness.View.SetItemCount(0);

            Assert.IsEmpty(_harness.View.ShownIndices);
            Assert.AreEqual(0, _harness.ActiveCells, "a bulk release stopped at the cell that threw");
        }

        [Test]
        public void RentCell_ForTheWrongCellType_LeaksNothing()
        {
            _harness = RecyclerViewHarness.Build();
            _harness.UseProvider(_ => _harness.View.RentCell<OtherTestCell>(0));

            Assert.Throws<InvalidOperationException>(() => _harness.View.SetItemCount(10));
            Assert.AreEqual(0, _harness.ActiveCells, "the type was checked after the cell was rented");
            _harness.Freeze();
        }

        [Test]
        public void Provider_ReturningADifferentCellThanItRented_IsRefused_AndLeaksNothing()
        {
            _harness = RecyclerViewHarness.Build();
            var stray = new GameObject("Stray", typeof(RectTransform)).AddComponent<TestCell>();
            _harness.UseProvider(_ =>
            {
                _harness.View.RentCell<TestCell>(0);
                return stray;
            });

            Assert.Throws<InvalidOperationException>(() => _harness.View.SetItemCount(10));
            Assert.AreEqual(0, _harness.ActiveCells, "the rented cell leaked");

            _harness.Freeze();
            UnityEngine.Object.DestroyImmediate(stray.gameObject);
        }

        [Test]
        public void ASizeProviderThrowingInSetItemCount_LeavesCountAndLayoutUnchanged()
        {
            bool failPast50 = false;
            _harness = RecyclerViewHarness.Build();
            _harness.UseDefaultProvider();
            _harness.View.SetItemSizeProvider(i =>
                failPast50 && i >= 50 ? throw new InvalidOperationException("size failed") : 100f);
            _harness.View.SetItemCount(40);
            float before = _harness.View.TotalSize;

            failPast50 = true;
            Assert.Throws<InvalidOperationException>(() => _harness.View.SetItemCount(80));

            Assert.AreEqual(40, _harness.View.ItemCount, "the count moved without the table");
            Assert.AreEqual(before, _harness.View.TotalSize, 0.5f);
            Assert.DoesNotThrow(() => _harness.View.ScrollToIndex(79),
                "a count ahead of its table indexes past the table's end");
        }
    }
}
