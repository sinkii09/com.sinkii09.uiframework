using NUnit.Framework;
using Sinkii09.UIFramework;
using UnityEngine;

namespace Sinkii09.UIFramework.Tests.Editor
{
    /// <summary>
    /// The pump loops until the window reports no work left, so non-convergence is a hang, not a
    /// glitch. SuperScrollView guards this with a 9999-iteration bail and a LogError — a symptom of
    /// never having proved termination. These tests prove it instead.
    /// </summary>
    public class RecycleWindowConvergenceTests
    {
        private const float Stride = RecyclerTestConstants.Stride;
        private const float CellSize = RecyclerTestConstants.CellSize;
        private const float ViewportSize = RecyclerTestConstants.ViewportSize;
        private const float CreateDistance = RecyclerTestConstants.CreateDistance;
        private const float RecycleDistance = RecyclerTestConstants.RecycleDistance;

        /// <summary>Cells needed to span the viewport plus both create bands.</summary>
        private static int ExpectedWindowSize
            => Mathf.CeilToInt((ViewportSize + 2f * CreateDistance) / Stride) + 1;

        [Test]
        public void Converges_AndFillsAContiguousWindowCoveringTheViewport()
        {
            var window = new FakeWindow();
            window.Seed(10);
            window.Settle();

            int steps = window.Converge(1000f);

            Assert.Less(steps, window.IterationBudget, "window never converged");
            Assert.LessOrEqual(steps, ExpectedWindowSize + 2,
                $"converged in {steps} steps, expected at most {ExpectedWindowSize + 2}");

            window.AssertContiguous();
            Assert.LessOrEqual(window.Head * Stride, 1000f, "window does not reach the viewport start");
            Assert.GreaterOrEqual(window.Tail * Stride + CellSize, 1500f,
                "window does not reach the viewport end");
        }

        [Test]
        public void Converges_ThenStaysStableAcrossRepeatedTicks()
        {
            var window = new FakeWindow();
            window.Seed(10);
            window.Settle();
            window.Converge(1000f);

            for (int tick = 0; tick < 5; tick++)
            {
                window.Settle();

                Assert.AreEqual(0, window.Converge(1000f),
                    "a settled window at an unchanged scroll position must need no work at all");
            }
        }

        [Test]
        public void Converges_FromEveryScrollPositionAcrossTheList()
        {
            var probe = new FakeWindow();

            for (float start = 0f; start <= 900f * Stride; start += 337f)
            {
                var window = new FakeWindow();
                window.Seed(Mathf.Clamp(Mathf.FloorToInt(start / Stride), 0, probe.ItemCount - 1));
                window.Settle();

                int steps = window.Converge(start);

                Assert.Less(steps, window.IterationBudget, $"no convergence at viewportStart {start}");
                window.AssertContiguous();
            }
        }

        [Test]
        public void Converges_WhileScrollingSmoothlyForwardAndBack()
        {
            var window = new FakeWindow();
            window.Seed(0);
            window.Settle();

            // Sub-cell steps are the boundary-thrash regime: each tick must settle in a few steps.
            for (float start = 0f; start < 4000f; start += 37f)
            {
                window.Settle();
                Assert.LessOrEqual(window.Converge(start), ExpectedWindowSize + 2);
            }

            for (float start = 4000f; start >= 0f; start -= 37f)
            {
                window.Settle();
                Assert.LessOrEqual(window.Converge(start), ExpectedWindowSize + 2);
            }

            window.AssertContiguous();
        }

        [Test]
        public void NeedsReseed_OnlyAfterTheWindowLeavesTheRecycleBands()
        {
            var window = new FakeWindow();
            window.Seed(10);
            window.Settle();
            window.Converge(1000f);

            Assert.IsFalse(RecycleWindow.NeedsReseed(window.State(1000f), RecycleDistance));
            Assert.IsTrue(RecycleWindow.NeedsReseed(window.State(50000f), RecycleDistance),
                "a jump far past the window must reseed rather than step across the gap");
        }

        [Test]
        public void NeedsReseed_WhenNothingIsShownButItemsExist()
        {
            var window = new FakeWindow();

            Assert.IsTrue(RecycleWindow.NeedsReseed(window.State(0f), RecycleDistance));

            window.ItemCount = 0;
            Assert.IsFalse(RecycleWindow.NeedsReseed(window.State(0f), RecycleDistance));
        }
    }
}
