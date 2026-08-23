using System;
using NUnit.Framework;
using Sinkii09.UIFramework;

namespace Sinkii09.UIFramework.Tests.Editor
{
    /// <summary>
    /// The offset table decides where every cell goes, so a sign or an off-by-one here is a
    /// misplaced list rather than a crash.
    ///
    /// <para>Every case that can distinguish the two spacing conventions runs at <b>non-zero
    /// spacing</b>. At <c>spacing == 0</c> "offset includes the gap" and "offset excludes the gap"
    /// are the same function, so a suite written that way proves nothing about the one thing this
    /// interface exists to pin down.</para>
    /// </summary>
    public class ItemOffsetsTests
    {
        private const float Spacing = 10f;
        private const float CellSize = 100f;

        private static IItemOffsets Uniform(int count, float cellSize = CellSize, float spacing = Spacing)
            => new UniformOffsets(count, cellSize, spacing);

        private static IItemOffsets Prefix(int count, float cellSize = CellSize, float spacing = Spacing)
            => new PrefixSumOffsets(count, _ => cellSize, spacing);

        /// <summary>Alternating short/tall rows — the one size rule the mixed cases share.</summary>
        private static float MixedSizeAt(int index) => index % 2 == 0 ? 40f : 200f;

        /// <summary>A list whose sizes actually vary, so uniform arithmetic cannot fake a pass.</summary>
        private static PrefixSumOffsets Mixed(int count, float spacing = Spacing)
            => new PrefixSumOffsets(count, MixedSizeAt, spacing);

        // ---- parity with the expressions phase 1 inlined -------------------------------------

        [TestCase(10, 100f, 10f)]
        [TestCase(1, 100f, 10f)]
        [TestCase(50000, 37.5f, 3f)]
        [TestCase(7, 64f, 0f)]
        public void UniformOffsets_ReproducesThePhase1Arithmetic(int count, float cellSize, float spacing)
        {
            var offsets = Uniform(count, cellSize, spacing);
            float stride = cellSize + spacing;

            // The literal expressions from v1.4.1: RecyclerView.TotalSize and OffsetOf.
            Assert.AreEqual(count * stride - spacing, offsets.TotalSize, 0.01f, "TotalSize");
            for (int i = 0; i < count; i++)
                Assert.AreEqual(i * stride, offsets.OffsetOf(i), 0.01f, $"OffsetOf({i})");
        }

        [Test]
        public void UniformOffsets_TotalSize_ExcludesTheTrailingSpacing()
        {
            // 10 cells of 100 with 9 gaps of 10 between them.
            Assert.AreEqual(1090f, Uniform(10).TotalSize, 0.01f);
        }

        [Test]
        public void PrefixSumOffsets_TotalSize_ExcludesTheTrailingSpacing()
        {
            Assert.AreEqual(1090f, Prefix(10).TotalSize, 0.01f);
        }

        // ---- the spacing convention itself ---------------------------------------------------

        [Test]
        public void Uniform_OffsetAdvancesBySizePlusSpacing()
        {
            AssertAdvanceInvariant(Uniform(20));
        }

        [Test]
        public void PrefixSum_OffsetAdvancesBySizePlusSpacing()
        {
            AssertAdvanceInvariant(Mixed(20));
        }

        /// <summary>
        /// The one invariant that ties <c>OffsetOf</c> to <c>SizeOf</c>: sizes exclude the gap,
        /// offsets accumulate it.
        /// </summary>
        private static void AssertAdvanceInvariant(IItemOffsets offsets)
        {
            for (int i = 0; i < offsets.Count - 1; i++)
            {
                Assert.AreEqual(
                    offsets.OffsetOf(i) + offsets.SizeOf(i) + Spacing,
                    offsets.OffsetOf(i + 1),
                    0.01f,
                    $"advance from {i} to {i + 1}");
            }
        }

        [Test]
        public void PrefixSum_SizeOf_ReturnsTheDeclaredSize_NotTheStride()
        {
            var offsets = Mixed(6);

            Assert.AreEqual(40f, offsets.SizeOf(0), 0.01f);
            Assert.AreEqual(200f, offsets.SizeOf(1), 0.01f);
        }

        // ---- IndexAt -------------------------------------------------------------------------

        [Test]
        public void Uniform_IndexAt_RoundTripsEveryStart()
        {
            AssertIndexAtRoundTrips(Uniform(25));
        }

        [Test]
        public void PrefixSum_IndexAt_RoundTripsEveryStart()
        {
            AssertIndexAtRoundTrips(Mixed(25));
        }

        private static void AssertIndexAtRoundTrips(IItemOffsets offsets)
        {
            for (int i = 0; i < offsets.Count; i++)
                Assert.AreEqual(i, offsets.IndexAt(offsets.OffsetOf(i)), $"IndexAt(OffsetOf({i}))");
        }

        [Test]
        public void Uniform_IndexAt_InTheGap_ReturnsThePrecedingIndex()
        {
            AssertGapBelongsToPrecedingIndex(Uniform(10));
        }

        [Test]
        public void PrefixSum_IndexAt_InTheGap_ReturnsThePrecedingIndex()
        {
            AssertGapBelongsToPrecedingIndex(Mixed(10));
        }

        /// <summary>
        /// A containment test over <c>[start, start + size)</c> would find nothing between two cells
        /// and leave a reseed with no anchor. <c>floor(offset / stride)</c> never had that hole, and
        /// the replacement must not introduce one.
        /// </summary>
        private static void AssertGapBelongsToPrecedingIndex(IItemOffsets offsets)
        {
            for (int i = 0; i < offsets.Count - 1; i++)
            {
                float inTheGap = offsets.OffsetOf(i) + offsets.SizeOf(i) + Spacing * 0.5f;
                Assert.AreEqual(i, offsets.IndexAt(inTheGap), $"offset in the gap after {i}");
            }
        }

        [Test]
        public void IndexAt_ClampsBothEnds()
        {
            // Overscroll drives the viewport start negative; a jump past the end must not run off it.
            foreach (IItemOffsets offsets in new[] { Uniform(10), (IItemOffsets)Mixed(10) })
            {
                Assert.AreEqual(0, offsets.IndexAt(-500f), "negative offset");
                Assert.AreEqual(9, offsets.IndexAt(offsets.TotalSize * 2f), "past the end");
            }
        }

        // ---- untrusted sizes -----------------------------------------------------------------

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        public void PrefixSum_RejectsNonPositiveSize_NamingTheIndex(float bad)
        {
            var ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => new PrefixSumOffsets(10, i => i == 4 ? bad : CellSize, Spacing));

            StringAssert.Contains("index 4", ex.Message);
        }

        [Test]
        public void PrefixSum_ThrowingProvider_LeavesTheCallerFreeToKeepTheOldTable()
        {
            IItemOffsets previous = Mixed(10);
            float totalBefore = previous.TotalSize;

            Assert.Throws<InvalidOperationException>(
                () => new PrefixSumOffsets(10, _ => throw new InvalidOperationException("boom"), Spacing));

            // The constructor either produces a whole table or throws; nothing partial escapes it,
            // so a caller assigning only on success still answers from the previous table.
            Assert.AreEqual(totalBefore, previous.TotalSize, 0.01f);
            Assert.AreEqual(9, previous.IndexAt(previous.TotalSize * 2f));
        }

        // ---- accumulation ---------------------------------------------------------------------

        [Test]
        public void PrefixSum_OverFiftyThousandRows_DoesNotDriftFromTheAnalyticTotal()
        {
            const int count = 50000;

            // Deliberately NOT 100/10. Those are exactly representable and every partial sum lands on
            // an integer well inside float's exact range, so a pure-float accumulator passes such a
            // test identically — it would prove nothing about the double. These do not divide evenly
            // into binary fractions, so per-step rounding accumulates visibly if it is allowed to.
            const float size = 37.3f;
            const float spacing = 3.7f;

            var offsets = new PrefixSumOffsets(count, _ => size, spacing);

            double stride = (double)size + spacing;
            Assert.AreEqual(count * stride - spacing, offsets.TotalSize, 0.05f, "TotalSize");
            Assert.AreEqual((count - 1) * stride, offsets.OffsetOf(count - 1), 0.05f, "last offset");
        }

        // ---- degenerate shapes -----------------------------------------------------------------

        [Test]
        public void Empty_AnswersWithoutThrowing()
        {
            foreach (IItemOffsets offsets in new[] { UniformOffsets.Empty, (IItemOffsets)Prefix(0) })
            {
                Assert.AreEqual(0, offsets.Count);
                Assert.AreEqual(0f, offsets.TotalSize, 0.01f);
                Assert.AreEqual(0f, offsets.MinStride, 0.01f, "empty MinStride must be 0 so the budget guard catches it");
                Assert.AreEqual(0, offsets.IndexAt(123f));
            }
        }

        [Test]
        public void MinStride_IsTheSmallestAdvance_NotTheSmallestSize()
        {
            // Smallest cell is 40; the advance it produces is 40 + spacing.
            Assert.AreEqual(40f + Spacing, Mixed(10).MinStride, 0.01f);
            Assert.AreEqual(CellSize + Spacing, Uniform(10).MinStride, 0.01f);
        }

        /// <summary>
        /// Rebuilding under a changed provider is how a size change reaches the table — there is no
        /// per-index setter, deliberately. Earlier items must not move; later ones shift by the delta.
        /// </summary>
        [Test]
        public void RebuildingWithAChangedProvider_ShiftsOnlyWhatFollows()
        {
            PrefixSumOffsets before = Mixed(10);
            float offsetOfTwoBefore = before.OffsetOf(2);

            var after = new PrefixSumOffsets(10, i => i == 4 ? 500f : MixedSizeAt(i), Spacing);

            Assert.AreEqual(offsetOfTwoBefore, after.OffsetOf(2), 0.01f, "earlier items must not move");
            Assert.AreEqual(500f, after.SizeOf(4), 0.01f);
            Assert.AreEqual(before.OffsetOf(5) + (500f - before.SizeOf(4)), after.OffsetOf(5), 0.01f);
        }
    }
}
