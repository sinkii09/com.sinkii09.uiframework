using NUnit.Framework;
using Sinkii09.UIFramework;

namespace Sinkii09.UIFramework.Tests.Editor
{
    /// <summary>
    /// The iteration budget is a safety cap, so the only failure mode that matters is it being too
    /// SMALL: the pump logs an error and abandons the tick, leaving the list permanently short of
    /// cells. A fixed 64 shipped here and was exceeded by ordinary phone-sized lists, so these tests
    /// pin the budget to the geometry rather than to a number someone once picked.
    /// </summary>
    public class RecycleWindowIterationBudgetTests
    {
        private const float CreateDistance = 200f;

        /// <summary>Cells a reseed has to create to span the viewport plus both create bands.</summary>
        private static int CellsToFill(float viewportSize, float stride)
            => (int)System.Math.Ceiling((viewportSize + 2f * CreateDistance) / stride);

        [TestCase(500f, 100f, TestName = "IterationBudget_Covers_DesktopList")]
        [TestCase(1920f, 30f, TestName = "IterationBudget_Covers_TallPhoneListWithSmallRows")]
        [TestCase(1080f, 24f, TestName = "IterationBudget_Covers_ChatSizedRows")]
        [TestCase(2400f, 16f, TestName = "IterationBudget_Covers_PathologicallyDenseList")]
        public void Budget_ExceedsTheWorkAReseedActuallyNeeds(float viewportSize, float stride)
        {
            int needed = CellsToFill(viewportSize, stride);
            int budget = RecycleWindow.MaxIterationsFor(viewportSize, CreateDistance, stride, 100000);

            Assert.Greater(budget, needed,
                $"viewport {viewportSize} with stride {stride} needs {needed} creates to fill; " +
                $"a budget of {budget} aborts the tick before the list is full");
        }

        [Test]
        public void Budget_IsNotTheFixedSixtyFourThatUsedToShip()
        {
            // The exact case that broke: 1920px viewport, 30px rows. Guards against a silent revert
            // to any constant in that neighbourhood.
            int budget = RecycleWindow.MaxIterationsFor(1920f, CreateDistance, 30f, 100000);

            Assert.Greater(budget, 64, "the budget must scale with geometry, not sit at a constant");
        }

        [Test]
        public void Budget_NeverExceedsWhatTheListCouldPossiblyNeed()
        {
            // A 3-item list cannot need dozens of steps however big the viewport is.
            int budget = RecycleWindow.MaxIterationsFor(4000f, CreateDistance, 10f, 3);

            Assert.LessOrEqual(budget, RecycleWindow.MinIterations + 8,
                "budget should collapse to the floor once the item count bounds the window");
        }

        [TestCase(0f)]
        [TestCase(-5f)]
        public void Budget_FallsBackToTheFloorForDegenerateStride(float stride)
        {
            Assert.AreEqual(RecycleWindow.MinIterations,
                RecycleWindow.MaxIterationsFor(500f, CreateDistance, stride, 100));
        }

        [Test]
        public void Budget_FallsBackToTheFloorForAnEmptyList()
        {
            Assert.AreEqual(RecycleWindow.MinIterations,
                RecycleWindow.MaxIterationsFor(500f, CreateDistance, 100f, 0));
        }
    }
}
