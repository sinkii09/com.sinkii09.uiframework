using NUnit.Framework;
using Sinkii09.UIFramework;
using UnityEngine;

namespace Sinkii09.UIFramework.Tests.Editor
{
    /// <summary>
    /// Locks down the offset-space contract: for every direction, scrolling toward later items must
    /// make <see cref="ScrollAxis.ViewportStart"/> increase, and <c>ToLocal</c> must be its inverse.
    /// A sign error here shows up as a list that scrolls backwards or renders off-screen.
    /// </summary>
    public class ScrollAxisTests
    {
        private const float Tolerance = 1e-4f;

        [TestCase(ScrollDirection.TopToBottom, false, -1f)]
        [TestCase(ScrollDirection.BottomToTop, false, +1f)]
        [TestCase(ScrollDirection.LeftToRight, true, +1f)]
        [TestCase(ScrollDirection.RightToLeft, true, -1f)]
        public void From_Direction_SetsAxisAndSign(ScrollDirection direction, bool horizontal, float sign)
        {
            ScrollAxis axis = ScrollAxis.From(direction);

            Assert.AreEqual(horizontal, axis.Horizontal);
            Assert.AreEqual(sign, axis.Sign, Tolerance);
        }

        [TestCase(ScrollDirection.TopToBottom, 0.5f, 1f)]
        [TestCase(ScrollDirection.BottomToTop, 0.5f, 0f)]
        [TestCase(ScrollDirection.LeftToRight, 0f, 0.5f)]
        [TestCase(ScrollDirection.RightToLeft, 1f, 0.5f)]
        public void From_Direction_SetsPivotAtListStart(ScrollDirection direction, float pivotX, float pivotY)
        {
            ScrollAxis axis = ScrollAxis.From(direction);

            Assert.AreEqual(pivotX, axis.Pivot.x, Tolerance);
            Assert.AreEqual(pivotY, axis.Pivot.y, Tolerance);
        }

        // The content root's anchored position for a viewport parked at a given offset, per
        // direction. These are the concrete Unity values RecyclerView.ScrollToIndex writes, spelled
        // out rather than derived from Sign so a sign flip in ScrollAxis cannot flip the expectation
        // with it. Content slides *backwards* to reveal later items — hence the mirrored signs.
        [TestCase(ScrollDirection.TopToBottom, 0f, +100f)]
        [TestCase(ScrollDirection.BottomToTop, 0f, -100f)]
        [TestCase(ScrollDirection.LeftToRight, -100f, 0f)]
        [TestCase(ScrollDirection.RightToLeft, +100f, 0f)]
        public void ViewportStart_ReadsTheOffsetTheContentIsParkedAt(
            ScrollDirection direction, float contentX, float contentY)
        {
            ScrollAxis axis = ScrollAxis.From(direction);

            Assert.AreEqual(100f, axis.ViewportStart(new Vector2(contentX, contentY)), Tolerance,
                $"{direction}: content at ({contentX}, {contentY}) means the viewport starts at offset 100");
        }

        /// <summary>
        /// ToLocal places cells *inside* the content; ViewportStart reads the content's *own*
        /// position. They act on different objects and are negatives of each other, so composing
        /// them is meaningless — a test that expected a round-trip here was asserting a bug.
        /// </summary>
        [TestCase(ScrollDirection.TopToBottom)]
        [TestCase(ScrollDirection.BottomToTop)]
        [TestCase(ScrollDirection.LeftToRight)]
        [TestCase(ScrollDirection.RightToLeft)]
        public void ViewportStart_IsInverseOfTheContentPositionRecyclerViewWrites(ScrollDirection direction)
        {
            ScrollAxis axis = ScrollAxis.From(direction);

            foreach (float offset in new[] { 0f, 137f, 5000f })
            {
                // Mirrors RecyclerView.ScrollToIndex — the only writer of content position.
                Vector2 contentPos = axis.Compose(-axis.Sign * offset, 0f);

                Assert.AreEqual(offset, axis.ViewportStart(contentPos), Tolerance,
                    $"round-trip failed for {direction} at offset {offset}");
            }
        }

        [TestCase(ScrollDirection.TopToBottom)]
        [TestCase(ScrollDirection.BottomToTop)]
        [TestCase(ScrollDirection.LeftToRight)]
        [TestCase(ScrollDirection.RightToLeft)]
        public void ViewportStart_IncreasesWhenScrollingTowardLaterItems(ScrollDirection direction)
        {
            ScrollAxis axis = ScrollAxis.From(direction);

            float near = axis.ViewportStart(axis.Compose(-axis.Sign * 100f, 0f));
            float far = axis.ViewportStart(axis.Compose(-axis.Sign * 900f, 0f));

            Assert.Less(near, far, $"{direction}: offset space must grow toward later items");
        }

        /// <summary>
        /// Cells are laid out in the layout direction, which is the opposite sign to the content's
        /// travel. Locks the cell side of the convention that the two tests above lock the content
        /// side of.
        /// </summary>
        [TestCase(ScrollDirection.TopToBottom, 0f, -100f)]
        [TestCase(ScrollDirection.BottomToTop, 0f, +100f)]
        [TestCase(ScrollDirection.LeftToRight, +100f, 0f)]
        [TestCase(ScrollDirection.RightToLeft, -100f, 0f)]
        public void ToLocal_PlacesCellsAwayFromTheListStart(
            ScrollDirection direction, float expectedX, float expectedY)
        {
            ScrollAxis axis = ScrollAxis.From(direction);

            Vector3 placed = axis.Compose(axis.ToLocal(100f), 0f);

            Assert.AreEqual(expectedX, placed.x, Tolerance, $"{direction}: cell x");
            Assert.AreEqual(expectedY, placed.y, Tolerance, $"{direction}: cell y");
        }

        [Test]
        public void SizeOf_And_Along_ReadTheScrollAxisComponent()
        {
            ScrollAxis vertical = ScrollAxis.From(ScrollDirection.TopToBottom);
            ScrollAxis horizontal = ScrollAxis.From(ScrollDirection.LeftToRight);
            var rect = new Rect(0f, 0f, 300f, 80f);

            Assert.AreEqual(80f, vertical.SizeOf(rect), Tolerance);
            Assert.AreEqual(300f, horizontal.SizeOf(rect), Tolerance);

            Assert.AreEqual(7f, vertical.Along(new Vector2(3f, 7f)), Tolerance);
            Assert.AreEqual(3f, horizontal.Along(new Vector2(3f, 7f)), Tolerance);
        }

        [Test]
        public void Compose_PutsAlongOnScrollAxisAndCrossOnTheOther()
        {
            ScrollAxis vertical = ScrollAxis.From(ScrollDirection.TopToBottom);
            ScrollAxis horizontal = ScrollAxis.From(ScrollDirection.LeftToRight);

            Assert.AreEqual(new Vector3(5f, 11f, 0f), vertical.Compose(11f, 5f));
            Assert.AreEqual(new Vector3(11f, 5f, 0f), horizontal.Compose(11f, 5f));
        }
    }
}
