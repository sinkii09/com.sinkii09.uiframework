using UnityEngine;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Translates offset space into RectTransform state.
    ///
    /// <para>Positions are written straight to <c>anchoredPosition</c> and sizes straight to
    /// <c>sizeDelta</c>. No LayoutGroup, no ContentSizeFitter: uGUI's layout system rebuilds a whole
    /// subtree whenever a child changes, which is exactly the cost recycling exists to avoid.</para>
    /// </summary>
    internal static class ContentLayout
    {
        /// <summary>
        /// Anchors a rect to the start edge of the scroll axis and stretches it across the cross
        /// axis. Applied to the content root and to every cell instance.
        /// </summary>
        public static void ConfigureRect(RectTransform rect, in ScrollAxis axis)
        {
            if (axis.Horizontal)
            {
                rect.anchorMin = new Vector2(axis.Pivot.x, 0f);
                rect.anchorMax = new Vector2(axis.Pivot.x, 1f);
            }
            else
            {
                rect.anchorMin = new Vector2(0f, axis.Pivot.y);
                rect.anchorMax = new Vector2(1f, axis.Pivot.y);
            }

            rect.pivot = axis.Pivot;
        }

        /// <summary>
        /// Prepares a freshly instantiated cell. Anchors are written to the <b>instance</b>, never
        /// back to the prefab asset — mutating a project asset at runtime is a side effect that
        /// survives play mode and silently rewrites the author's prefab.
        /// </summary>
        public static void ConfigureCell(RectTransform cell, float cellSize, in ScrollAxis axis)
        {
            ConfigureRect(cell, axis);
            cell.localScale = Vector3.one;
            cell.localRotation = Quaternion.identity;
            SetSizeAlongAxis(cell, cellSize, axis);
        }

        /// <summary>Places a cell so its leading edge sits at <paramref name="offset"/>.</summary>
        public static void PlaceCell(RectTransform cell, float offset, in ScrollAxis axis)
        {
            cell.anchoredPosition3D = axis.Compose(axis.ToLocal(offset), 0f);
        }

        /// <summary>Sizes the content root to span the whole list.</summary>
        public static void SetContentSize(RectTransform content, float totalSize, in ScrollAxis axis)
        {
            SetSizeAlongAxis(content, totalSize, axis);
        }

        /// <summary>Reads a cell's current size along the scroll axis.</summary>
        public static float MeasureCell(RectTransform cell, in ScrollAxis axis)
        {
            return axis.SizeOf(cell.rect);
        }

        /// <summary>
        /// Sets the along-axis size, leaving the cross axis at 0 so the stretch anchors set by
        /// <see cref="ConfigureRect"/> make it fill its parent.
        /// </summary>
        private static void SetSizeAlongAxis(RectTransform rect, float size, in ScrollAxis axis)
        {
            rect.sizeDelta = axis.Horizontal ? new Vector2(size, 0f) : new Vector2(0f, size);
        }
    }
}
