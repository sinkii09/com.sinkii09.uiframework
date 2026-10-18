using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace Sinkii09.UIFramework.Tests
{
    /// <summary>
    /// A consumer that checks the prefetch contract per index: every announced index ends in exactly
    /// one provider call or one cancel; nothing shown or already outstanding is announced; nothing
    /// shown or no-longer-outstanding is cancelled.
    ///
    /// <para>Violations are recorded, not asserted, inside the callbacks: the view catches and logs
    /// whatever a handler throws, which would turn a clear failure into an unexpected-log one.</para>
    /// </summary>
    internal sealed class PrefetchLedger
    {
        private readonly RecyclerViewHarness _harness;

        public readonly HashSet<int> Outstanding = new();
        public readonly List<(int First, int Count)> Prefetches = new();
        public readonly List<(int First, int Count)> Cancels = new();
        public readonly List<string> Violations = new();

        /// <summary>Provider calls for an index that was outstanding — hints the bind took over.</summary>
        public int ConsumedHints;

        /// <summary>Provider calls for an index that was NOT outstanding.</summary>
        public int UnannouncedBinds;

        /// <summary>When set, the provider throws for this index (once) after recording the call.</summary>
        public int ThrowOn = -1;

        public PrefetchLedger(RecyclerViewHarness harness) => _harness = harness;

        /// <summary>Installs the ledger as the prefetch handler and as the cell provider.</summary>
        public PrefetchLedger Attach()
        {
            _harness.View.SetPrefetchHandler(OnPrefetch, OnCancel);
            _harness.UseProvider(Provide);
            return this;
        }

        public RecyclerCell Provide(int index)
        {
            _harness.BindCalls.Add(index);
            if (Outstanding.Remove(index)) ConsumedHints++;
            else UnannouncedBinds++;

            if (index == ThrowOn)
            {
                ThrowOn = -1;
                throw new InvalidOperationException($"provider boom at {index}");
            }

            return _harness.View.RentCell<TestCell>(0);
        }

        public void OnPrefetch(int first, int count)
        {
            Prefetches.Add((first, count));
            if (count < 1) Violations.Add($"announced an empty run ({first}, {count})");

            for (int i = first; i < first + count; i++)
            {
                if (IsShown(i)) Violations.Add($"announced {i}, which is shown");
                if (!Outstanding.Add(i)) Violations.Add($"announced {i} twice");
            }
        }

        public void OnCancel(int first, int count)
        {
            Cancels.Add((first, count));
            if (count < 1) Violations.Add($"cancelled an empty run ({first}, {count})");

            for (int i = first; i < first + count; i++)
            {
                if (IsShown(i)) Violations.Add($"cancelled {i}, which is shown");
                if (!Outstanding.Remove(i)) Violations.Add($"cancelled {i}, which was not outstanding");
            }
        }

        public void ClearCalls()
        {
            Prefetches.Clear();
            Cancels.Clear();
        }

        /// <summary>Every index a list of runs covers, in order.</summary>
        public static List<int> Expand(List<(int First, int Count)> runs)
        {
            var indices = new List<int>();
            foreach ((int first, int count) in runs)
                for (int i = first; i < first + count; i++) indices.Add(i);
            return indices;
        }

        /// <summary>No violation, nothing outstanding is shown, and the ledger matches the view's own record.</summary>
        public void AssertExact(string context = "")
        {
            Assert.IsEmpty(Violations, $"{context}\n" + string.Join("\n", Violations));

            foreach (int index in _harness.View.ShownIndices)
                Assert.IsFalse(Outstanding.Contains(index), $"{context}: {index} is shown and still outstanding");

            var viewRange = (IndexRange)typeof(RecyclerView)
                .GetField("_prefetchOutstanding", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(_harness.View);

            Assert.AreEqual(viewRange.Count, Outstanding.Count, $"{context}: ledger {Outstanding.Count} vs view {viewRange}");
            for (int i = viewRange.Start; i < viewRange.End; i++)
                Assert.IsTrue(Outstanding.Contains(i), $"{context}: view holds {i} outstanding, ledger does not");
        }

        private bool IsShown(int index)
        {
            IReadOnlyList<int> shown = _harness.View.ShownIndices;
            for (int i = 0; i < shown.Count; i++)
                if (shown[i] == index) return true;
            return false;
        }
    }
}
