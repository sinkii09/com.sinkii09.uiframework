using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    /// <summary>
    /// Prefetch across the view's lifetime and its mutators: switching the hook, configuring it before
    /// Awake, destroying the view, the three data resets, and the layout changes that deliberately do
    /// NOT reset — re-packing is not a data change, so the diff alone has to keep the ledger exact.
    /// </summary>
    public class RecyclerViewPrefetchLifecycleTests
    {
        private RecyclerViewHarness _harness;
        private PrefetchLedger _ledger;

        [TearDown]
        public void TearDown() => _harness?.Destroy();

        private IReadOnlyList<int> Shown => _harness.View.ShownIndices;

        /// <summary>Mid-list, so a forward band is outstanding and indices behind the window exist.</summary>
        private IEnumerator BeginMidList()
        {
            _harness = RecyclerViewHarness.Build(prefetchDistance: 250f);
            _ledger = new PrefetchLedger(_harness).Attach();
            _harness.View.SetItemCount(1000);
            _harness.View.ScrollToIndex(200);
            yield return null;
            Assert.IsNotEmpty(_ledger.Outstanding);
        }

        [Test]
        public void ACancelCallbackWithoutAPrefetchCallback_IsRefused()
        {
            _harness = RecyclerViewHarness.Build(prefetchDistance: 250f);
            Assert.Throws<ArgumentException>(() => _harness.View.SetPrefetchHandler(null, (_, _) => { }));
        }

        [UnityTest]
        public IEnumerator NullHandler_CancelsEverything_AndStopsAnnouncing()
        {
            yield return BeginMidList();
            var band = new HashSet<int>(_ledger.Outstanding);
            _ledger.ClearCalls();

            _harness.View.SetPrefetchHandler(null);
            CollectionAssert.AreEquivalent(band, PrefetchLedger.Expand(_ledger.Cancels));
            Assert.IsEmpty(_ledger.Outstanding);

            _ledger.ClearCalls();
            _harness.View.ScrollToIndex(600);
            yield return null;
            Assert.IsEmpty(_ledger.Prefetches);
            Assert.IsEmpty(_ledger.Cancels);
        }

        [UnityTest]
        public IEnumerator SwappingTheHandler_CancelsThroughTheOldOne_AndAnnouncesThroughTheNew()
        {
            yield return BeginMidList();
            var band = new HashSet<int>(_ledger.Outstanding);
            var next = new PrefetchLedger(_harness);
            _ledger.ClearCalls();

            _harness.View.SetPrefetchHandler(next.OnPrefetch, next.OnCancel);

            CollectionAssert.AreEquivalent(band, PrefetchLedger.Expand(_ledger.Cancels));
            Assert.IsEmpty(_ledger.Outstanding);
            Assert.IsEmpty(_ledger.Violations);
            CollectionAssert.AreEquivalent(band, next.Outstanding, "the new handler re-announces the same band");
            next.AssertExact();
        }

        [UnityTest]
        public IEnumerator ConfiguredBeforeAwake_AnnouncesOnceTheViewRuns()
        {
            _harness = RecyclerViewHarness.Build(prefetchDistance: 250f, activate: false);
            _ledger = new PrefetchLedger(_harness).Attach();
            _harness.View.SetItemCount(1000);
            Assert.IsEmpty(_ledger.Prefetches, "nothing can be announced before Awake");

            _harness.Activate();
            yield return null;

            Assert.AreEqual(Shown.Last() + 1, _ledger.Prefetches.Last().First);
            _ledger.AssertExact();
        }

        [UnityTest]
        public IEnumerator Destroying_CancelsWhatIsOutstanding_AndLaterCallsAreHarmless()
        {
            yield return BeginMidList();
            var band = new HashSet<int>(_ledger.Outstanding);
            RecyclerView view = _harness.View;
            _ledger.ClearCalls();

            UnityEngine.Object.DestroyImmediate(_harness.Root);

            CollectionAssert.AreEquivalent(band, PrefetchLedger.Expand(_ledger.Cancels));
            Assert.IsEmpty(_ledger.Violations);
            Assert.DoesNotThrow(() => view.SetPrefetchHandler(_ledger.OnPrefetch, _ledger.OnCancel));
            Assert.AreEqual(0f, view.ScrollVelocity);
        }

        [UnityTest]
        public IEnumerator DataResets_CancelThenReannounceTheSameBand()
        {
            yield return BeginMidList();
            var band = new HashSet<int>(_ledger.Outstanding);

            foreach (Action reset in new Action[]
                     {
                         () => _harness.View.RefreshAll(),
                         () => _harness.UseProvider(_ledger.Provide),
                         () => _harness.View.SetItemCount(1000),
                     })
            {
                _ledger.ClearCalls();
                reset();

                CollectionAssert.AreEquivalent(band, PrefetchLedger.Expand(_ledger.Cancels));
                CollectionAssert.AreEquivalent(band, PrefetchLedger.Expand(_ledger.Prefetches));
                _ledger.AssertExact();
            }
        }

        [UnityTest]
        public IEnumerator ShrinkingTheCount_CancelsThenAnnouncesOnlyLiveIndices()
        {
            yield return BeginMidList();
            _ledger.ClearCalls();

            _harness.View.SetItemCount(205);

            Assert.IsNotEmpty(_ledger.Cancels);
            Assert.IsNotEmpty(_ledger.Prefetches);
            Assert.IsTrue(PrefetchLedger.Expand(_ledger.Prefetches).All(i => i < 205));
            _ledger.AssertExact();
        }

        /// <summary>
        /// Layout changes re-pack indices without changing what they mean. No reset: whatever leaves the
        /// band is cancelled by the diff, nothing shown is ever cancelled, and an unchanged layout
        /// says nothing at all.
        /// </summary>
        [UnityTest]
        public IEnumerator LayoutChanges_KeepTheLedgerExact_WithoutResetting()
        {
            yield return BeginMidList();

            _ledger.ClearCalls();
            _harness.View.SetItemSizeProvider(_ => 100f);
            _harness.View.RefreshSizes();
            Assert.IsEmpty(_ledger.Cancels, "re-laying out identical sizes cancelled hints");
            Assert.IsEmpty(_ledger.Prefetches, "re-laying out identical sizes re-announced hints");
            _ledger.AssertExact("identical sizes");

            _harness.View.SetItemSizeProvider(i => i % 2 == 0 ? 50f : 150f);
            _ledger.AssertExact("variable sizes");

            _harness.View.SetCrossAxisCount(3);
            _ledger.AssertExact("three columns");
            yield return null;

            _harness.View.SetCrossAxisCount(1);
            _ledger.AssertExact("back to a list");
            RecyclerWindowAssert.Intact(_harness);
        }
    }
}
