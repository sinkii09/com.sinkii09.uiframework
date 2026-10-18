using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Sinkii09.UIFramework.Tests.Editor
{
    /// <summary>
    /// The reconcile between what was announced and what is wanted now. The promise to the consumer
    /// is a ledger: every announced index ends in exactly one bind or one cancel, a bound index is
    /// never cancelled, and nothing outstanding or shown is announced twice.
    /// </summary>
    public class PrefetchWindowDiffTests
    {
        private static IndexRange Range(int start, int end) => new IndexRange(start, end);

        private static void AssertDiff(PrefetchDiff diff, IndexRange[] cancels, IndexRange[] prefetches)
        {
            Assert.AreEqual(cancels.Length, diff.CancelCount, "cancel count");
            for (int i = 0; i < cancels.Length; i++) Assert.AreEqual(cancels[i], diff.Cancel(i), $"cancel {i}");

            Assert.AreEqual(prefetches.Length, diff.PrefetchCount, "prefetch count");
            for (int i = 0; i < prefetches.Length; i++) Assert.AreEqual(prefetches[i], diff.Prefetch(i), $"prefetch {i}");
        }

        private static readonly IndexRange[] None = Array.Empty<IndexRange>();

        [Test]
        public void ColdStart_AnnouncesTheWholeBand()
            => AssertDiff(PrefetchWindow.Diff(IndexRange.Empty, IndexRange.Empty, Range(10, 13)),
                None, new[] { Range(10, 13) });

        [Test]
        public void AtRest_SaysNothing()
            => AssertDiff(PrefetchWindow.Diff(Range(10, 13), IndexRange.Empty, Range(10, 13)), None, None);

        [Test]
        public void StepForward_ConsumesSilently_AndAnnouncesOnlyTheNewEdge()
            => AssertDiff(PrefetchWindow.Diff(Range(10, 13), Range(10, 11), Range(11, 14)),
                None, new[] { Range(13, 14) });

        [Test]
        public void DirectionFlip_CancelsTheOldBand_AndAnnouncesTheOtherSide()
            => AssertDiff(PrefetchWindow.Diff(Range(10, 13), IndexRange.Empty, Range(3, 6)),
                new[] { Range(10, 13) }, new[] { Range(3, 6) });

        [Test]
        public void Jump_CancelsEverything_AndAnnouncesTheNewBand()
            => AssertDiff(PrefetchWindow.Diff(Range(10, 13), Range(500, 506), Range(506, 509)),
                new[] { Range(10, 13) }, new[] { Range(506, 509) });

        [Test]
        public void JumpIntoTheOutstandingBand_CancelsBothRemainders_OutsideTheNewBand()
            => AssertDiff(PrefetchWindow.Diff(Range(10, 20), Range(14, 16), Range(16, 19)),
                new[] { Range(10, 14), Range(19, 20) }, None);

        [Test]
        public void BandShrinksToNothing_CancelsEverything()
            => AssertDiff(PrefetchWindow.Diff(Range(10, 13), IndexRange.Empty, IndexRange.Empty),
                new[] { Range(10, 13) }, None);

        /// <summary>10 and 11 bound, 12's provider threw: 12 is consumed, then re-announced as a new episode.</summary>
        [Test]
        public void ThrowingBind_IsConsumed_AndReannouncedNotCancelled()
            => AssertDiff(PrefetchWindow.Diff(Range(10, 20), Range(10, 13), Range(12, 22)),
                None, new[] { Range(12, 13), Range(20, 22) });

        [Test]
        public void ConsumedIndicesOutsideTheOutstandingRange_AreIgnored()
            => AssertDiff(PrefetchWindow.Diff(Range(10, 13), Range(0, 10), Range(10, 13)), None, None);

        [Test]
        public void Subtract_ReturnsAscendingPieces()
        {
            PrefetchWindow.Subtract(Range(0, 10), Range(3, 5), out IndexRange left, out IndexRange right);
            Assert.AreEqual(Range(0, 3), left);
            Assert.AreEqual(Range(5, 10), right);

            PrefetchWindow.Subtract(Range(0, 10), Range(20, 30), out left, out right);
            Assert.AreEqual(Range(0, 10), left);
            Assert.IsTrue(right.IsEmpty);

            PrefetchWindow.Subtract(Range(0, 10), Range(-5, 50), out left, out right);
            Assert.IsTrue(left.IsEmpty && right.IsEmpty);
        }

        /// <summary>
        /// 2 000 random steps of a window over a list — steps, reversals, resizes, jumps and binds
        /// that throw — driven through the real band, direction and diff functions, with a per-index
        /// ledger checking every announcement and cancel against the episode rules.
        /// </summary>
        [Test]
        public void RandomScrollSession_KeepsTheLedgerExact()
        {
            const int itemCount = 300;
            var offsets = new UniformOffsets(itemCount, 100f, 10f);
            var random = new Random(20261018);
            var ledger = new HashSet<int>();

            IndexRange shown = Range(0, 6), outstanding = IndexRange.Empty, baseline = IndexRange.Empty;
            bool forward = true;
            int announced = 0, cancelled = 0, consumedHints = 0;

            for (int step = 0; step < 2000; step++)
            {
                IndexRange next = NextWindow(random, shown, itemCount);
                IndexRange called = CalledIndices(random, shown, next, itemCount);

                // The provider was called: those hints end here, whatever happens later.
                for (int i = called.Start; i < called.End; i++)
                    if (ledger.Remove(i)) consumedHints++;

                forward = PrefetchWindow.NextDirection(forward, baseline, next, itemCount);
                IndexRange desired = forward
                    ? PrefetchWindow.ForwardBand(offsets, next.End - 1, 350f)
                    : PrefetchWindow.BackwardBand(offsets, next.Start, 350f);

                PrefetchDiff diff = PrefetchWindow.Diff(outstanding, called, desired);
                for (int c = 0; c < diff.CancelCount; c++)
                    for (int i = diff.Cancel(c).Start; i < diff.Cancel(c).End; i++)
                    {
                        Assert.IsTrue(ledger.Remove(i), $"step {step}: cancelled {i}, which was not outstanding");
                        Assert.IsFalse(next.Contains(i) || called.Contains(i), $"step {step}: cancelled bound index {i}");
                        cancelled++;
                    }

                for (int p = 0; p < diff.PrefetchCount; p++)
                    for (int i = diff.Prefetch(p).Start; i < diff.Prefetch(p).End; i++)
                    {
                        Assert.IsFalse(next.Contains(i), $"step {step}: announced shown index {i}");
                        Assert.IsTrue(ledger.Add(i), $"step {step}: announced {i} twice");
                        announced++;
                    }

                Assert.AreEqual(desired.Count, ledger.Count, $"step {step}: ledger drifted from the band");
                for (int i = desired.Start; i < desired.End; i++) Assert.IsTrue(ledger.Contains(i));

                shown = next;
                baseline = next;
                outstanding = desired;
            }

            Assert.Greater(announced, 0);
            Assert.Greater(cancelled, 0, "the session never exercised a cancel");
            Assert.Greater(consumedHints, 0, "the session never bound a prefetched index");
        }

        /// <summary>A step, reversal, resize or jump — contiguous windows only, like the real one.</summary>
        private static IndexRange NextWindow(Random random, IndexRange shown, int itemCount)
        {
            int start = shown.Start, end = shown.End;
            switch (random.Next(6))
            {
                case 0: case 1: { int k = random.Next(1, 4); start += k; end += k; break; }
                case 2: case 3: { int k = random.Next(1, 4); start -= k; end -= k; break; }
                case 4: start += random.Next(-1, 2); end += random.Next(-1, 2); break;
                default: start = random.Next(0, itemCount - 6); end = start + 6; break;
            }

            start = Math.Max(0, Math.Min(start, itemCount - 1));
            end = Math.Max(start + 1, Math.Min(end, itemCount));
            return Range(start, end);
        }

        /// <summary>
        /// The provider calls the move implies, as one hull: the new edges (a disjoint jump binds the
        /// whole new window), plus — one step in eight — a bind just past an edge that threw.
        /// </summary>
        private static IndexRange CalledIndices(Random random, IndexRange shown, IndexRange next, int itemCount)
        {
            bool overlaps = next.Start < shown.End && shown.Start < next.End;
            IndexRange called = IndexRange.Empty;

            if (!overlaps)
            {
                called = next;
            }
            else
            {
                for (int i = next.Start; i < shown.Start; i++) called = called.Include(i);
                for (int i = shown.End; i < next.End; i++) called = called.Include(i);
            }

            if (random.Next(8) == 0)
            {
                int thrown = random.Next(2) == 0 ? next.End : next.Start - 1;
                if (thrown >= 0 && thrown < itemCount) called = called.Include(thrown);
            }

            return called;
        }
    }
}
