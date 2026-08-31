using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Sinkii09.UIFramework.Tests
{
    // Asserts the two invariants that survive any screen size, rather than pinning exact
    // coordinates: the tooltip never leaves the layer, and it flips off a side it would overflow.
    // A ScreenSpaceOverlay canvas sizes itself to the game view, so hard-coded expected positions
    // would flake on a different resolution.
    public class TooltipPositioningTests
    {
        private GameObjectTracker _tracker;
        private RectTransform _layer;
        private RectTransform _tooltip;

        [SetUp]
        public void SetUp()
        {
            _tracker = new GameObjectTracker();

            var canvas = _tracker.Track(new GameObject(
                "Canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            _layer = Stretch(NewRect("Layer", canvas.transform));
            _tooltip = NewRect("Tooltip", _layer);
            _tooltip.sizeDelta = new Vector2(200f, 100f);
        }

        [TearDown]
        public void TearDown() => _tracker.DestroyAll();

        private RectTransform NewRect(string name, Transform parent)
        {
            var go = _tracker.Track(new GameObject(name, typeof(RectTransform)));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static RectTransform Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        // Anchors the rect at a normalised point of the layer so "near the top edge" means the
        // same thing at any resolution.
        private RectTransform AnchorAt(Vector2 normalized)
        {
            var anchor = NewRect("Anchor", _layer);
            anchor.anchorMin = anchor.anchorMax = normalized;
            anchor.pivot = new Vector2(0.5f, 0.5f);
            anchor.sizeDelta = new Vector2(50f, 50f);
            anchor.anchoredPosition = Vector2.zero;
            return anchor;
        }

        private static Rect WorldRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return new Rect(corners[0], corners[2] - corners[0]);
        }

        [Test]
        public void Position_AnchorNearTopEdge_FlipsBelowTheAnchor()
        {
            var anchor = AnchorAt(new Vector2(0.5f, 1f));

            TooltipPositioner.Position(_tooltip, anchor, _layer, TooltipPlacement.Above);

            // Above would overflow the top, so the preferred side must flip.
            Assert.Less(WorldRect(_tooltip).center.y, WorldRect(anchor).center.y,
                "A tooltip that cannot fit above its anchor must flip below it.");
        }

        [Test]
        public void Position_AnchorMidScreen_HonoursThePreferredSide()
        {
            var anchor = AnchorAt(new Vector2(0.5f, 0.5f));

            TooltipPositioner.Position(_tooltip, anchor, _layer, TooltipPlacement.Above);

            Assert.Greater(WorldRect(_tooltip).center.y, WorldRect(anchor).center.y,
                "With room on the preferred side there is no reason to flip.");
        }

        [Test]
        public void Position_AnchorInCorner_ClampsTooltipInsideTheLayer()
        {
            var anchor = AnchorAt(new Vector2(1f, 1f));

            TooltipPositioner.Position(_tooltip, anchor, _layer, TooltipPlacement.Auto);

            Rect bounds = WorldRect(_layer);
            Rect tooltip = WorldRect(_tooltip);

            // Sub-pixel tolerance: the layer rect and the clamp both round through screen space.
            const float Tolerance = 0.5f;
            Assert.GreaterOrEqual(tooltip.xMin, bounds.xMin - Tolerance);
            Assert.LessOrEqual(tooltip.xMax, bounds.xMax + Tolerance);
            Assert.GreaterOrEqual(tooltip.yMin, bounds.yMin - Tolerance);
            Assert.LessOrEqual(tooltip.yMax, bounds.yMax + Tolerance);
        }
    }
}
