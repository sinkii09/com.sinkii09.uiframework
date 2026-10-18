using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    /// <summary>
    /// Consumer code calling back into the view while it is mid-walk. Each path below used to be
    /// unguarded — the call went through, rebuilt the window underneath the walk, and could hand one
    /// pooled handle to two slots. Refusing loudly is the contract; the window must come out intact.
    /// </summary>
    public class RecyclerViewReentrancyTests
    {
        private const string Refused = "was called from inside a cell provider or OnRecycled";

        private RecyclerViewHarness _harness;

        [TearDown]
        public void TearDown() => _harness?.Destroy();

        [UnityTest]
        public IEnumerator AMutationFromARefreshIndexProvider_IsRefused()
        {
            int mutateOn = -1;
            _harness = RecyclerViewHarness.Build();
            _harness.UseProvider(index =>
            {
                TestCell cell = _harness.View.RentCell<TestCell>(0);
                if (index == mutateOn) _harness.View.SetItemCount(5);
                return cell;
            });
            _harness.View.SetItemCount(100);
            yield return null;

            mutateOn = _harness.View.ShownIndices[2];
            LogAssert.Expect(LogType.Error, new Regex("SetItemCount " + Refused));
            _harness.View.RefreshIndex(mutateOn);

            Assert.AreEqual(100, _harness.View.ItemCount, "the nested mutation went through");
            RecyclerWindowAssert.Intact(_harness);
        }

        [UnityTest]
        public IEnumerator AMutationFromOnRecycled_DuringABulkRelease_IsRefused()
        {
            bool callBack = false;
            _harness = RecyclerViewHarness.Build();
            _harness.UseProvider(_ =>
            {
                TestCell cell = _harness.View.RentCell<TestCell>(0);
                cell.OnRecycledHook = () =>
                {
                    if (!callBack) return;
                    callBack = false;
                    _harness.View.RefreshAll();
                };
                return cell;
            });
            _harness.View.SetItemCount(100);
            yield return null;

            callBack = true;
            LogAssert.Expect(LogType.Error, new Regex("RefreshAll " + Refused));
            _harness.View.SetItemCount(50);

            Assert.AreEqual(50, _harness.View.ItemCount);
            RecyclerWindowAssert.Intact(_harness);
        }

        /// <summary>
        /// ForEachShownCell borrowed the pump's flag and cleared it on exit — so called from a
        /// provider, it switched the guard off for the rest of that pump.
        /// </summary>
        [UnityTest]
        public IEnumerator ForEachShownCell_CalledFromAProvider_LeavesTheGuardInPlace()
        {
            int trigger = -1;
            _harness = RecyclerViewHarness.Build();
            _harness.UseProvider(index =>
            {
                TestCell cell = _harness.View.RentCell<TestCell>(0);
                if (index == trigger)
                {
                    _harness.View.ForEachShownCell(_ => { });
                    _harness.View.SetItemCount(3);
                }
                return cell;
            });
            _harness.View.SetItemCount(100);
            yield return null;

            trigger = _harness.View.ShownIndices[1];
            LogAssert.Expect(LogType.Error, new Regex("SetItemCount " + Refused));
            _harness.View.RefreshAll();

            Assert.AreEqual(100, _harness.View.ItemCount, "the guard was off after ForEachShownCell returned");
            RecyclerWindowAssert.Intact(_harness);
        }

        /// <summary>SetItemCount's own reset path calls the scroll core — the guard must not refuse it.</summary>
        [UnityTest]
        public IEnumerator SetItemCount_WithResetPosition_StillScrollsToTheTop()
        {
            _harness = RecyclerViewHarness.Build();
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(1000);
            _harness.View.ScrollToIndex(500);
            yield return null;

            _harness.View.SetItemCount(1000, resetPosition: true);

            CollectionAssert.Contains(_harness.View.ShownIndices, 0);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ScrollToIndex_ShowsTheTargetWithoutWaitingForAFrame()
        {
            _harness = RecyclerViewHarness.Build();
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(1000);

            _harness.View.ScrollToIndex(400);

            CollectionAssert.Contains(_harness.View.ShownIndices, 400);
        }

        [UnityTest]
        public IEnumerator SetCellProvider_Swapped_RebindsTheCellsOnScreen()
        {
            var boundBy = new Dictionary<RecyclerCell, string>();
            _harness = RecyclerViewHarness.Build();
            _harness.UseProvider(_ => Tag(_harness.View.RentCell<TestCell>(0), "old", boundBy));
            _harness.View.SetItemCount(100);
            yield return null;

            _harness.UseProvider(_ => Tag(_harness.View.RentCell<TestCell>(0), "new", boundBy));

            Assert.IsNotEmpty(_harness.View.ShownIndices, "an empty window would pass the check below vacuously");
            _harness.View.ForEachShownCell(cell =>
                Assert.AreEqual("new", boundBy[cell], $"index {cell.Index} still shows the old provider's data"));
            RecyclerWindowAssert.Intact(_harness);
        }

        private static RecyclerCell Tag(RecyclerCell cell, string provider, Dictionary<RecyclerCell, string> boundBy)
        {
            boundBy[cell] = provider;
            return cell;
        }
    }
}
