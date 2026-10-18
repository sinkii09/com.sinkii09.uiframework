using System;
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    /// <summary>
    /// A prefetch handler is consumer code running inside the pump. It must not be able to break the
    /// list: a throw is logged and the bookkeeping carries on, a mutation is refused by name, and a
    /// provider that throws mid-grow must not leave a hint the view later cancels on a bound index.
    /// </summary>
    public class RecyclerViewPrefetchSafetyTests
    {
        private const string Refused = "was called from inside a prefetch handler";

        private RecyclerViewHarness _harness;
        private PrefetchLedger _ledger;

        [TearDown]
        public void TearDown() => _harness?.Destroy();

        private IEnumerator Begin()
        {
            _harness = RecyclerViewHarness.Build(prefetchDistance: 250f);
            _ledger = new PrefetchLedger(_harness).Attach();
            _harness.View.SetItemCount(1000);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AThrowingHandler_IsLogged_AndTheListCarriesOn()
        {
            yield return Begin();
            bool throwNext = true;

            // The reconcile on SetPrefetchHandler cancels the old band and announces the new one,
            // synchronously; only the announce throws, and the cancel already went through.
            LogAssert.Expect(LogType.Exception, new Regex("prefetch boom"));
            _harness.View.SetPrefetchHandler((first, count) =>
            {
                _ledger.OnPrefetch(first, count);
                if (!throwNext) return;
                throwNext = false;
                throw new InvalidOperationException("prefetch boom");
            }, _ledger.OnCancel);
            yield return null;

            for (int frame = 0; frame < 20; frame++)
            {
                _harness.ScrollTo(frame * 40f);
                yield return null;
            }

            Assert.Greater(_ledger.ConsumedHints, 0, "binds after the throw still consumed announced hints");
            _ledger.AssertExact();
            RecyclerWindowAssert.Intact(_harness);
        }

        [UnityTest]
        public IEnumerator MutationsFromAHandler_AreRefusedByName()
        {
            yield return Begin();
            int calls = 0;

            LogAssert.Expect(LogType.Error, new Regex("SetItemCount " + Refused));
            LogAssert.Expect(LogType.Error, new Regex("ScrollToIndex " + Refused));
            LogAssert.Expect(LogType.Error, new Regex("SetPrefetchHandler " + Refused));
            _harness.View.SetPrefetchHandler((first, count) =>
            {
                if (calls++ > 0) return;
                _harness.View.SetItemCount(5);
                _harness.View.ScrollToIndex(500);
                _harness.View.SetPrefetchHandler(null);
            });
            Assert.AreEqual(1, calls, "turning the hook on announced synchronously");
            yield return null;

            Assert.AreEqual(1000, _harness.View.ItemCount);
            Assert.AreEqual(0, _harness.View.ShownIndices[0], "the nested ScrollToIndex went through");
            RecyclerWindowAssert.Intact(_harness);
        }

        /// <summary>
        /// 7 binds, 8's provider throws: 8 was CALLED, so its hint ended there. It is re-announced as a
        /// new episode, and the jump that follows cancels only hints that are still outstanding —
        /// never a bound index, never 8's first episode.
        /// </summary>
        [UnityTest]
        public IEnumerator AProviderThrowingMidGrow_ThenAJump_KeepsTheLedgerExact()
        {
            yield return Begin();
            int tail = _harness.View.ShownIndices.Last();
            Assert.IsTrue(_ledger.Outstanding.Contains(tail + 2), "the throwing index must have been announced");
            _ledger.ThrowOn = tail + 2;

            Assert.Throws<InvalidOperationException>(() => _harness.View.ScrollToIndex(2));

            Assert.AreEqual(tail + 1, _harness.View.ShownIndices.Last(), "grew one cell, then the provider threw");
            _ledger.AssertExact("after the throw");
            Assert.IsTrue(_ledger.Outstanding.Contains(tail + 2), "the thrown index is announced again");

            _ledger.ClearCalls();
            _harness.View.ScrollToIndex(500);

            Assert.IsNotEmpty(_ledger.Cancels);
            _ledger.AssertExact("after the jump");
            RecyclerWindowAssert.Intact(_harness);
        }
    }
}
