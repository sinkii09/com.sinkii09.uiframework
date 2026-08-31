using UnityEngine;
using UnityEngine.UI;

namespace Sinkii09.UIFramework
{
    // Places a tooltip rect against an anchor rect inside the tooltip layer: prefer a side, flip
    // when that side would overflow, clamp inside the layer regardless.
    //
    // Everything is computed in SCREEN space rather than by transforming between the two rects
    // directly, so an anchor living under a different canvas (world-space, or a second overlay
    // canvas with its own scale factor) still resolves correctly.
    internal static class TooltipPositioner
    {
        // Gap between the anchor edge and the tooltip edge, in the layer canvas's reference pixels.
        private const float Gap = 8f;

        public static void Position(RectTransform tooltip, RectTransform anchor, RectTransform layer,
            TooltipPlacement placement)
        {
            if (tooltip == null || anchor == null || layer == null) return;

            // ORDER IS LOAD-BEARING: ContentSizeFitter has not run on the frame the text was set,
            // so measuring before this rebuild clamps the tooltip against a stale size.
            LayoutRebuilder.ForceRebuildLayoutImmediate(tooltip);

            var layerCanvas = layer.GetComponentInParent<Canvas>();
            var layerCam = CameraFor(layerCanvas);
            var anchorCam = CameraFor(anchor.GetComponentInParent<Canvas>());

            Rect anchorRect = ScreenRect(anchor, anchorCam);
            Rect bounds = ScreenRect(layer, layerCam);
            float scale = layerCanvas != null ? layerCanvas.scaleFactor : 1f;
            Vector2 size = tooltip.rect.size * scale;

            Vector2 center = Resolve(placement, anchorRect, bounds, size, Gap * scale);
            center = Clamp(center, size, bounds);

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, center, layerCam, out var local))
                return;

            // Centre-anchored so anchoredPosition is a straight offset from the layer's middle; a
            // stretched tooltip has no single position to solve for anyway.
            tooltip.anchorMin = tooltip.anchorMax = new Vector2(0.5f, 0.5f);
            tooltip.anchoredPosition = local + (tooltip.pivot - new Vector2(0.5f, 0.5f)) * tooltip.rect.size;
        }

        // ScreenSpaceOverlay must pass a null camera; every other mode must pass the canvas's own.
        // Passing the wrong one is the classic silent mis-position.
        private static Camera CameraFor(Canvas canvas)
            => canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        private static readonly Vector3[] Corners = new Vector3[4];

        private static Rect ScreenRect(RectTransform rt, Camera cam)
        {
            rt.GetWorldCorners(Corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, Corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(cam, Corners[2]);
            return new Rect(min, max - min);
        }

        private static Vector2 Resolve(TooltipPlacement placement, Rect anchor, Rect bounds, Vector2 size, float gap)
        {
            // Auto prefers Above: with pointer-driven UI the cursor sits over or below the anchor,
            // so drawing upward keeps the tooltip clear of it.
            if (placement == TooltipPlacement.Auto) placement = TooltipPlacement.Above;

            Vector2 candidate = Place(placement, anchor, size, gap);
            if (Fits(candidate, size, bounds)) return candidate;

            Vector2 flipped = Place(Opposite(placement), anchor, size, gap);
            // If neither side fits, keep the preferred one and let the clamp deal with it —
            // flipping to an equally bad side just moves the overlap somewhere less expected.
            return Fits(flipped, size, bounds) ? flipped : candidate;
        }

        private static Vector2 Place(TooltipPlacement side, Rect anchor, Vector2 size, float gap) => side switch
        {
            TooltipPlacement.Below => new Vector2(anchor.center.x, anchor.yMin - gap - size.y * 0.5f),
            TooltipPlacement.Left  => new Vector2(anchor.xMin - gap - size.x * 0.5f, anchor.center.y),
            TooltipPlacement.Right => new Vector2(anchor.xMax + gap + size.x * 0.5f, anchor.center.y),
            _                      => new Vector2(anchor.center.x, anchor.yMax + gap + size.y * 0.5f),
        };

        private static TooltipPlacement Opposite(TooltipPlacement side) => side switch
        {
            TooltipPlacement.Below => TooltipPlacement.Above,
            TooltipPlacement.Left  => TooltipPlacement.Right,
            TooltipPlacement.Right => TooltipPlacement.Left,
            _                      => TooltipPlacement.Below,
        };

        private static bool Fits(Vector2 center, Vector2 size, Rect bounds)
        {
            Vector2 half = size * 0.5f;
            return center.x - half.x >= bounds.xMin && center.x + half.x <= bounds.xMax
                && center.y - half.y >= bounds.yMin && center.y + half.y <= bounds.yMax;
        }

        private static Vector2 Clamp(Vector2 center, Vector2 size, Rect bounds)
        {
            Vector2 half = size * 0.5f;

            // A tooltip larger than the layer on an axis cannot be clamped into it — pin it to the
            // min edge so the start of the content stays readable instead of centring the overflow.
            center.x = bounds.width  <= size.x ? bounds.xMin + half.x
                     : Mathf.Clamp(center.x, bounds.xMin + half.x, bounds.xMax - half.x);
            center.y = bounds.height <= size.y ? bounds.yMin + half.y
                     : Mathf.Clamp(center.y, bounds.yMin + half.y, bounds.yMax - half.y);
            return center;
        }
    }
}
