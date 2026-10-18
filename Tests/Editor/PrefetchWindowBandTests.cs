using NUnit.Framework;

namespace Sinkii09.UIFramework.Tests.Editor
{
    /// <summary>
    /// Which indices the prefetch band covers, and which side of the window it sits on. Pure, so the
    /// boundary rules — closed upper reach, gaps belonging to the earlier stride, whole grid rows,
    /// the short-circuits that keep a huge distance away from <c>IndexAt</c> — are pinned exactly.
    /// </summary>
    public class PrefetchWindowBandTests
    {
        private static IndexRange Range(int start, int end) => new IndexRange(start, end);

        // ---- forward ------------------------------------------------------------------------

        [Test]
        public void ForwardBand_List_CoversStridesStartingWithinTheDistance()
        {
            var offsets = new UniformOffsets(100, 100f, 0f);

            Assert.AreEqual(Range(10, 13), PrefetchWindow.ForwardBand(offsets, 9, 250f));
            // Item 13 starts exactly at tailEnd + d: the reach is closed.
            Assert.AreEqual(Range(10, 14), PrefetchWindow.ForwardBand(offsets, 9, 300f));
        }

        [Test]
        public void ForwardBand_AtOrNearTheLastItem_StopsAtTheEnd()
        {
            var offsets = new UniformOffsets(100, 100f, 0f);

            Assert.IsTrue(PrefetchWindow.ForwardBand(offsets, 99, 500f).IsEmpty);
            Assert.AreEqual(Range(99, 100), PrefetchWindow.ForwardBand(offsets, 98, 50f));
        }

        [Test]
        public void ForwardBand_HugeDistance_ReachesTheEndWithoutOverflow()
        {
            var offsets = new UniformOffsets(100, 100f, 0f);

            Assert.AreEqual(Range(10, 100), PrefetchWindow.ForwardBand(offsets, 9, 1e30f));
            Assert.AreEqual(Range(10, 100), PrefetchWindow.ForwardBand(offsets, 9, float.MaxValue));
        }

        [Test]
        public void ForwardBand_ZeroNegativeOrNaNDistance_IsEmpty()
        {
            var offsets = new UniformOffsets(100, 100f, 0f);

            Assert.IsTrue(PrefetchWindow.ForwardBand(offsets, 9, 0f).IsEmpty);
            Assert.IsTrue(PrefetchWindow.ForwardBand(offsets, 9, -10f).IsEmpty);
            Assert.IsTrue(PrefetchWindow.ForwardBand(offsets, 9, float.NaN).IsEmpty);
        }

        [Test]
        public void Bands_OnAnEmptyList_AreEmpty()
        {
            Assert.IsTrue(PrefetchWindow.ForwardBand(UniformOffsets.Empty, 0, 500f).IsEmpty);
            Assert.IsTrue(PrefetchWindow.BackwardBand(UniformOffsets.Empty, 0, 500f).IsEmpty);
        }

        /// <summary>
        /// A gap belongs to the stride before it. Forward, a distance shorter than the spacing stays
        /// inside the tail's own stride and reaches nothing; backward, the same distance lands in the
        /// previous stride's gap and reaches that whole stride. Asymmetric on purpose — pinned so a
        /// change to it is a decision, not an accident.
        /// </summary>
        [Test]
        public void Bands_DistanceShorterThanSpacing_AreAsymmetric()
        {
            var offsets = new UniformOffsets(100, 100f, 20f); // stride 120

            Assert.IsTrue(PrefetchWindow.ForwardBand(offsets, 9, 10f).IsEmpty);
            Assert.AreEqual(Range(10, 11), PrefetchWindow.ForwardBand(offsets, 9, 20f));
            Assert.AreEqual(Range(9, 10), PrefetchWindow.BackwardBand(offsets, 10, 10f));
        }

        [Test]
        public void Bands_VariableSizes_FollowThePrefixSums()
        {
            // 0:[0,50) 1:[50,200) 2:[200,250) 3:[250,400) 4:[400,450) ...
            var offsets = new PrefixSumOffsets(10, i => i % 2 == 0 ? 50f : 150f, 0f);

            Assert.AreEqual(Range(2, 4), PrefetchWindow.ForwardBand(offsets, 1, 100f));
            Assert.AreEqual(Range(2, 4), PrefetchWindow.BackwardBand(offsets, 4, 160f));
        }

        [Test]
        public void ForwardBand_Grid_AnnouncesWholeRows_AndAPartialLastRow()
        {
            var offsets = new GridOffsets(new UniformOffsets(4, 100f, 0f), 3, 10); // rows of 3, last row = {9}

            Assert.AreEqual(Range(3, 6), PrefetchWindow.ForwardBand(offsets, 2, 50f));
            Assert.AreEqual(Range(6, 10), PrefetchWindow.ForwardBand(offsets, 5, 150f));
        }

        [Test]
        public void ForwardBand_GridTailMidRow_TakesTheRestOfThatRow()
        {
            var offsets = new GridOffsets(new UniformOffsets(4, 100f, 0f), 3, 10);

            Assert.AreEqual(Range(5, 9), PrefetchWindow.ForwardBand(offsets, 4, 10f));
        }

        // ---- backward -----------------------------------------------------------------------

        [Test]
        public void BackwardBand_List_ReachesTheStrideCoveringHeadStartMinusDistance()
        {
            var offsets = new UniformOffsets(100, 100f, 20f); // stride 120

            // reach 950 falls in item 7's trailing gap, so item 7 is included.
            Assert.AreEqual(Range(7, 10), PrefetchWindow.BackwardBand(offsets, 10, 250f));
        }

        [Test]
        public void BackwardBand_PastTheStart_ClampsToZeroWithoutIndexAt()
        {
            var offsets = new UniformOffsets(100, 100f, 0f);

            Assert.AreEqual(Range(0, 2), PrefetchWindow.BackwardBand(offsets, 2, 500f));
            Assert.AreEqual(Range(0, 50), PrefetchWindow.BackwardBand(offsets, 50, float.MaxValue));
            Assert.IsTrue(PrefetchWindow.BackwardBand(offsets, 0, 500f).IsEmpty);
        }

        [Test]
        public void BackwardBand_Grid_AnnouncesWholeRows()
        {
            var offsets = new GridOffsets(new UniformOffsets(4, 100f, 0f), 3, 10);

            Assert.AreEqual(Range(3, 6), PrefetchWindow.BackwardBand(offsets, 6, 50f));
            Assert.AreEqual(Range(0, 6), PrefetchWindow.BackwardBand(offsets, 6, 150f));
        }

        // ---- direction ----------------------------------------------------------------------

        [Test]
        public void NextDirection_ListEnds_DecideOutright()
        {
            Assert.IsTrue(PrefetchWindow.NextDirection(false, Range(5, 10), Range(0, 6), 100),
                "at the head there is nothing behind");
            Assert.IsFalse(PrefetchWindow.NextDirection(true, Range(80, 90), Range(90, 100), 100),
                "at the tail there is nothing ahead");
        }

        [Test]
        public void NextDirection_NoPreviousWindow_KeepsTheCurrentDirection()
        {
            Assert.IsTrue(PrefetchWindow.NextDirection(true, IndexRange.Empty, Range(40, 45), 100));
            Assert.IsFalse(PrefetchWindow.NextDirection(false, IndexRange.Empty, Range(40, 45), 100));
            Assert.IsFalse(PrefetchWindow.NextDirection(false, Range(40, 45), IndexRange.Empty, 100),
                "an empty window keeps the direction");
        }

        [Test]
        public void NextDirection_FollowsTheWindowsOwnMovement()
        {
            Assert.IsTrue(PrefetchWindow.NextDirection(false, Range(40, 45), Range(41, 46), 100), "both ends grew");
            Assert.IsTrue(PrefetchWindow.NextDirection(false, Range(40, 45), Range(40, 46), 100), "tail grew");
            Assert.IsFalse(PrefetchWindow.NextDirection(true, Range(40, 45), Range(39, 44), 100), "both shrank");
            Assert.IsFalse(PrefetchWindow.NextDirection(true, Range(40, 45), Range(39, 45), 100), "head shrank");
        }

        [Test]
        public void NextDirection_MixedOrUnchanged_KeepsTheCurrentDirection()
        {
            Assert.IsFalse(PrefetchWindow.NextDirection(false, Range(40, 45), Range(40, 45), 100), "unchanged");
            Assert.IsTrue(PrefetchWindow.NextDirection(true, Range(40, 45), Range(41, 44), 100), "shrank on both edges");
            Assert.IsFalse(PrefetchWindow.NextDirection(false, Range(40, 45), Range(39, 46), 100), "grew on both edges");
        }
    }
}
