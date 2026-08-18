using NUnit.Framework;
using Sinkii09.UIFramework;

namespace Sinkii09.UIFramework.Tests.Editor
{
    /// <summary>
    /// Single-step decisions. Each test pins one rule that, if broken, produces a defect that still
    /// looks plausible on screen: cells churning every frame, cells vanishing at the ends, or the
    /// window walking off the data.
    /// </summary>
    public class RecycleWindowTests
    {
        [Test]
        public void RecyclesHead_OnceItClearsTheRecycleBand()
        {
            var window = new FakeWindow();
            window.Seed(0);
            window.Add(1);
            window.Settle();

            // Head spans [0,100). Viewport start 401 leaves its end 301 past the band (>300).
            Assert.AreEqual(WindowAction.RecycleHead, window.Decide(401f));
        }

        [Test]
        public void DoesNotRecycle_WhileParkedInsideTheHysteresisBand()
        {
            var window = new FakeWindow();
            window.Seed(0);
            window.Add(1);
            window.Settle();

            // Head end (100) sits between the create band (start-200) and the recycle band
            // (start-300). Neither edge may fire, or the cell churns every single frame.
            WindowAction action = window.Decide(250f);

            Assert.AreNotEqual(WindowAction.RecycleHead, action);
            Assert.AreNotEqual(WindowAction.CreateBeforeHead, action);
        }

        [Test]
        public void DoesNotRecycle_ACellCreatedOnTheCurrentTick()
        {
            var window = new FakeWindow();
            window.Seed(0);
            window.Add(1); // created this tick, never settled

            Assert.AreNotEqual(WindowAction.RecycleHead, window.Decide(401f),
                "recycling a cell created this tick lets the loop churn it in and out forever");
        }

        [Test]
        public void CreatesBeforeHead_WhenTheLeadingEdgeIsWithinTheCreateBand()
        {
            var window = new FakeWindow();
            window.Seed(5);
            window.Settle();

            // Head offset 500 is inside [viewportStart-200, ...) when viewportStart is 550.
            Assert.AreEqual(WindowAction.CreateBeforeHead, window.Decide(550f));
        }

        [Test]
        public void CreatesAfterTail_WhenTheTrailingEdgeIsWithinTheCreateBand()
        {
            var window = new FakeWindow();
            window.Seed(0);
            window.Settle();

            Assert.AreEqual(WindowAction.CreateAfterTail, window.Decide(0f));
        }

        [Test]
        public void NeverCreatesBeforeTheFirstItem()
        {
            var window = new FakeWindow();
            window.Seed(0);
            window.Settle();

            for (int i = 0; i < window.IterationBudget; i++)
            {
                WindowAction action = window.Decide(0f);
                if (action == WindowAction.None) break;

                Assert.AreNotEqual(WindowAction.CreateBeforeHead, action);
                window.Apply(action);
            }

            Assert.AreEqual(0, window.Head);
        }

        [Test]
        public void NeverCreatesPastTheLastItem()
        {
            var window = new FakeWindow { ItemCount = 10 };
            window.Seed(9);
            window.Settle();
            window.Converge(900f);

            Assert.AreEqual(9, window.Tail);
        }

        [Test]
        public void NeverRecyclesTheLastRemainingCell()
        {
            var window = new FakeWindow();
            window.Seed(0);
            window.Settle();

            // Far outside the recycle band, but the pump still needs an anchor to rebuild from.
            Assert.AreNotEqual(WindowAction.RecycleHead, window.Decide(9000f));
            Assert.AreNotEqual(WindowAction.RecycleTail, window.Decide(9000f));
        }

        [Test]
        public void DoesNothing_WhenThereAreNoItems()
        {
            var window = new FakeWindow { ItemCount = 0 };
            window.Seed(0);
            window.Settle();

            Assert.AreEqual(WindowAction.None, window.Decide(0f));
        }
    }
}
