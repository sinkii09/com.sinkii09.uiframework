using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Sinkii09.UIFramework.Tests
{
    /// <summary>
    /// <see cref="RecyclerView.ScrollVelocity"/> is defined against the list, not Unity's axes:
    /// positive means later items are coming into view, in all four directions. Checked against the
    /// viewport's real movement on the next frame, so a sign error cannot hide behind the formula it
    /// was derived from.
    /// </summary>
    public class RecyclerViewScrollVelocityTests
    {
        private const float Speed = 600f;

        private RecyclerViewHarness _harness;

        [TearDown]
        public void TearDown() => _harness?.Destroy();

        [UnityTest] public IEnumerator TopToBottom() => SignMatchesTheViewportsMovement(ScrollDirection.TopToBottom);
        [UnityTest] public IEnumerator BottomToTop() => SignMatchesTheViewportsMovement(ScrollDirection.BottomToTop);
        [UnityTest] public IEnumerator LeftToRight() => SignMatchesTheViewportsMovement(ScrollDirection.LeftToRight);
        [UnityTest] public IEnumerator RightToLeft() => SignMatchesTheViewportsMovement(ScrollDirection.RightToLeft);

        [Test]
        public void BeforeAwake_IsZero()
        {
            _harness = RecyclerViewHarness.Build(activate: false, inertia: true);
            _harness.ScrollRect.velocity = new Vector2(0f, Speed);

            Assert.AreEqual(0f, _harness.View.ScrollVelocity);
        }

        private IEnumerator SignMatchesTheViewportsMovement(ScrollDirection direction)
        {
            _harness = RecyclerViewHarness.Build(direction: direction, inertia: true);
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(1000);
            _harness.View.ScrollToIndex(500);
            yield return null;

            ScrollRect scroll = _harness.ScrollRect;
            ScrollAxis axis = ScrollAxis.From(direction);

            // No decay, so the coast is exactly what was set at any frame rate.
            scroll.decelerationRate = 1f;

            foreach (float unitySign in new[] { 1f, -1f })
            {
                scroll.velocity = axis.Horizontal
                    ? new Vector2(unitySign * Speed, 0f)
                    : new Vector2(0f, unitySign * Speed);

                float reported = _harness.View.ScrollVelocity;
                float before = axis.ViewportStart(_harness.Content.anchoredPosition);
                yield return null;
                yield return null;
                float moved = axis.ViewportStart(_harness.Content.anchoredPosition) - before;

                Assert.AreNotEqual(0f, moved, $"{direction}: the ScrollRect did not coast");
                Assert.AreEqual(Mathf.Sign(moved), Mathf.Sign(reported),
                    $"{direction}: reported {reported} while the viewport moved {moved}");
                Assert.AreEqual(Speed, Mathf.Abs(reported), 1e-3f);
            }

            scroll.velocity = Vector2.zero;
            Assert.AreEqual(0f, _harness.View.ScrollVelocity);
        }
    }
}
