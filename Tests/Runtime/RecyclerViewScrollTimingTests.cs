using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Sinkii09.UIFramework.Tests
{
    /// <summary>
    /// The pump runs in Update; ScrollRect moves the content in its LateUpdate. A window computed in
    /// Update therefore describes LAST frame's position, and the frame renders with the new one. The
    /// create band hides that lag only while a frame's travel is shorter than the band — past it, the
    /// leading edge renders empty.
    /// </summary>
    public class RecyclerViewScrollTimingTests
    {
        private const float ViewportSize = 1000f;
        private const float CellSize = 100f;

        // 300 px a frame: a hard fling of 9 000 units/s on a 30 fps phone, or any frame hitch at
        // ordinary speed. Past the DEFAULT create band (200) on purpose — nothing here is tuned down
        // to manufacture the gap.
        private const float TravelPerFrame = 300f;

        private RecyclerViewHarness _harness;

        [TearDown]
        public void TearDown() => _harness?.Destroy();

        [UnityTest]
        public IEnumerator AFastFlingForward_NeverRendersAnUncoveredEdge()
        {
            return Fling(+1f);
        }

        /// <summary>Back toward index 0 — the head edge, created by the other branch of the window.</summary>
        [UnityTest]
        public IEnumerator AFastFlingBackward_NeverRendersAnUncoveredEdge()
        {
            return Fling(-1f);
        }

        private IEnumerator Fling(float direction)
        {
            _harness = RecyclerViewHarness.Build(viewportSize: ViewportSize, cellSize: CellSize, inertia: true);
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(10_000);
            _harness.View.ScrollToIndex(5_000);
            yield return null;

            var probe = _harness.Root.AddComponent<WindowCoverageProbe>();
            probe.Watch(_harness.View, _harness.Content, ViewportSize, CellSize);
            ScrollRect scroll = _harness.ScrollRect;

            // ScrollRect decays the velocity by decelerationRate^dt BEFORE integrating it, so on a slow
            // frame the travel would shrink below the create band and the test would stop guarding.
            // No decay: the travel is exactly TravelPerFrame at any frame rate.
            scroll.decelerationRate = 1f;

            for (int frame = 0; frame < 30; frame++)
            {
                // Coroutines resume after Update and before LateUpdate, so this is the delta ScrollRect
                // is about to integrate with.
                float delta = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
                scroll.velocity = new Vector2(0f, direction * TravelPerFrame / delta);
                yield return null;
            }

            Assert.Greater(probe.Frames, 20, "the probe did not observe the fling");
            Assert.AreEqual(0, probe.UncoveredFrames,
                $"{probe.UncoveredFrames} of {probe.Frames} frames rendered with an edge uncovered");
        }
    }
}
