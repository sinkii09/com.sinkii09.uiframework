using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    /// <summary>
    /// The prefetch hook on a live view: which indices are announced, when, and that each announced
    /// index ends in exactly one bind or one cancel. 100 px cells, 500 px viewport, 250 px distance
    /// — a three-cell band.
    /// </summary>
    public class RecyclerViewPrefetchTests
    {
        private const float Distance = 250f;
        private const int BandCells = 3;

        private RecyclerViewHarness _harness;
        private PrefetchLedger _ledger;

        [TearDown]
        public void TearDown() => _harness?.Destroy();

        private IReadOnlyList<int> Shown => _harness.View.ShownIndices;

        private IEnumerator Begin(int itemCount = 1000, float distance = Distance, int crossAxisCount = 1)
        {
            _harness = RecyclerViewHarness.Build(prefetchDistance: distance, crossAxisCount: crossAxisCount);
            _ledger = new PrefetchLedger(_harness).Attach();
            _harness.View.SetItemCount(itemCount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator OffByDefault_AHandlerAloneNeverFires()
        {
            yield return Begin(distance: 0f);

            for (int frame = 0; frame < 20; frame++)
            {
                _harness.ScrollTo(frame * 60f);
                yield return null;
            }
            _harness.View.ScrollToIndex(700);

            Assert.Greater(_harness.BindCalls.Count, 0);
            Assert.IsEmpty(_ledger.Prefetches);
            Assert.IsEmpty(_ledger.Cancels);
        }

        [UnityTest]
        public IEnumerator TurningItOn_AnnouncesTheForwardBand_Immediately()
        {
            _harness = RecyclerViewHarness.Build(prefetchDistance: Distance);
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(1000);
            yield return null;

            _ledger = new PrefetchLedger(_harness);
            _harness.View.SetPrefetchHandler(_ledger.OnPrefetch, _ledger.OnCancel);

            Assert.AreEqual(1, _ledger.Prefetches.Count, "announced synchronously, in one run");
            Assert.AreEqual((Shown.Last() + 1, BandCells), _ledger.Prefetches[0]);
            Assert.AreEqual(Distance, _harness.View.PrefetchDistance);
        }

        /// <summary>
        /// Jiggling one pixel fires onValueChanged too — two pumps a frame, neither may speak. At 50 px,
        /// not 0: at 0 the tail ends exactly on the create boundary, so one pixel really does add a cell.
        /// </summary>
        [UnityTest]
        public IEnumerator AtRest_ManyFramesAndTwoPumpsAFrame_SayNothing()
        {
            yield return Begin();
            _harness.ScrollTo(50f);
            yield return null;
            _ledger.ClearCalls();

            for (int frame = 0; frame < 30; frame++)
            {
                _harness.ScrollTo(50f + frame % 2);
                yield return null;
            }

            Assert.IsEmpty(_ledger.Prefetches);
            Assert.IsEmpty(_ledger.Cancels);
        }

        [UnityTest]
        public IEnumerator SteppingForward_EveryBindWasAnnouncedFirst_AndNothingIsCancelled()
        {
            yield return Begin();
            int bindsBefore = _harness.BindCalls.Count;
            _ledger.UnannouncedBinds = 0;
            _ledger.ClearCalls();

            for (int frame = 0; frame < 40; frame++)
            {
                _harness.ScrollTo(frame * 30f);
                yield return null;
            }

            Assert.Greater(_harness.BindCalls.Count - bindsBefore, 3, "the steps never bound a cell");
            Assert.AreEqual(0, _ledger.UnannouncedBinds, "a cell bound without being announced first");
            Assert.IsEmpty(_ledger.Cancels);
            _ledger.AssertExact();
        }

        [UnityTest]
        public IEnumerator Reversal_CancelsExactlyTheForwardBand_AndAnnouncesBehind()
        {
            yield return Begin();
            _harness.View.ScrollToIndex(50);
            yield return null;

            var forwardBand = new HashSet<int>(_ledger.Outstanding);
            Assert.IsNotEmpty(forwardBand);
            Assert.Greater(forwardBand.Min(), Shown.Last(), "the band sat ahead of the window");
            _ledger.ClearCalls();

            _harness.ScrollTo(4700f);
            yield return null;

            CollectionAssert.AreEquivalent(forwardBand, PrefetchLedger.Expand(_ledger.Cancels));
            (int first, int count) = _ledger.Prefetches.Last();
            Assert.AreEqual(Shown[0], first + count, "the backward band ends at the head");
            _ledger.AssertExact();
        }

        [UnityTest]
        public IEnumerator Jump_CancelsTheOldBand_AndAnnouncesAheadOfTheNewWindow()
        {
            yield return Begin();
            var oldBand = new HashSet<int>(_ledger.Outstanding);
            _ledger.ClearCalls();

            _harness.View.ScrollToIndex(500);

            CollectionAssert.AreEquivalent(oldBand, PrefetchLedger.Expand(_ledger.Cancels));
            Assert.AreEqual((Shown.Last() + 1, BandCells), _ledger.Prefetches.Last());
            _ledger.AssertExact();
        }

        [UnityTest]
        public IEnumerator ListEnds_DecideTheSide_IncludingAResetToTheTop()
        {
            yield return Begin();

            _harness.View.ScrollToIndex(999);
            (int first, int count) = _ledger.Prefetches.Last();
            Assert.AreEqual(Shown[0], first + count, "at the end the band is behind the head");

            _harness.View.ScrollToIndex(0);
            Assert.AreEqual(Shown.Last() + 1, _ledger.Prefetches.Last().First, "at the top the band is ahead");

            _harness.View.ScrollToIndex(999);
            _harness.View.SetItemCount(1000, resetPosition: true);
            Assert.AreEqual(Shown.Last() + 1, _ledger.Prefetches.Last().First, "a reset to the top prefetches forward");
            _ledger.AssertExact();
        }

        [UnityTest]
        public IEnumerator Grid_AnnouncesWholeRows()
        {
            yield return Begin(itemCount: 100, crossAxisCount: 3);

            for (int frame = 0; frame < 60; frame++)
            {
                _harness.ScrollTo(frame * 40f);
                yield return null;
            }

            Assert.Greater(_ledger.Prefetches.Count, 3);
            foreach ((int first, int count) in _ledger.Prefetches)
            {
                Assert.AreEqual(0, first % 3, $"run ({first}, {count}) starts mid-row");
                int end = first + count;
                Assert.IsTrue(end % 3 == 0 || end == 100, $"run ({first}, {count}) ends mid-row");
            }
            _ledger.AssertExact();
        }

        [UnityTest]
        public IEnumerator ScriptedSession_KeepsTheLedgerExact()
        {
            yield return Begin();
            float[] path = { 300f, 900f, 2400f, 2100f, 1500f, 1600f, 50_000f, 49_800f, 99_000f, 0f, 700f, 650f };

            foreach (float target in path)
            {
                float from = _harness.Content.anchoredPosition.y;
                for (int frame = 1; frame <= 6; frame++)
                {
                    _harness.ScrollTo(UnityEngine.Mathf.Lerp(from, target, frame / 6f));
                    yield return null;
                }
                _ledger.AssertExact($"after scrolling to {target}");
                RecyclerWindowAssert.Intact(_harness);
            }

            Assert.Greater(_ledger.ConsumedHints, 0);
            Assert.Greater(_ledger.Cancels.Count, 0);
        }
    }
}
