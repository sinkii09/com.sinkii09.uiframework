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
        ///
        /// <para>Deliberately does not size the cell: this runs once per instantiation, and a pooled
        /// cell serves many indices. See <see cref="SizeCell"/>.</para>
        /// </summary>
        public static void ConfigureCell(RectTransform cell, in ScrollAxis axis)
        {
            ConfigureRect(cell, axis);
            cell.localScale = Vector3.one;
            cell.localRotation = Quaternion.identity;
        }

        /// <summary>Sizes a cell along the scroll axis. Applied per bind, not per instantiation.</summary>
        public static void SizeCell(RectTransform cell, float size, in ScrollAxis axis)
        {
            SetSizeAlongAxis(cell, size, axis);
        }

        /// <summary>Places a cell so its leading edge sits at <paramref name="offset"/>.</summary>
        public static void PlaceCell(RectTransform cell, float offset, in ScrollAxis axis)
        {
            cell.anchoredPosition3D = axis.Compose(axis.ToLocal(offset), 0f);
        }

        /// <summary>
        /// Sizes and places a cell inside one column of a grid, in a single call.
        ///
        /// <para><b>The column anchors are rewritten on every bind, not once per instantiation.</b>
        /// A pooled cell first created for column 0 will later serve column 2, and anchors written
        /// in <see cref="ConfigureCell"/> would travel with it — cells would pile up in one column
        /// after the first recycle. This is the same trap the size path already documents, one field
        /// over.</para>
        ///
        /// <para>The column is expressed as <b>fractional anchors</b> rather than an explicit width,
        /// so a change in the viewport's cross-axis size re-lays the columns out with no code and no
        /// notification — there is no resize hook anywhere in this control. The cross
        /// <c>sizeDelta</c> then insets each cell by <paramref name="crossSpacing"/>, which shows up
        /// as a full gap between columns and half of one outside the first and last.</para>
        /// </summary>
        public static void PlaceCellInGrid(
            RectTransform cell, float offset, float size, int column, int crossAxisCount,
            float crossSpacing, in ScrollAxis axis)
        {
            float lower = (float)column / crossAxisCount;
            float upper = (float)(column + 1) / crossAxisCount;

            if (axis.Horizontal)
            {
                // Bands are mirrored here on purpose. The cross axis of a horizontal list is Y,
                // which grows upward, so using the fractions as-is would put column 0 at the BOTTOM
                // of each row and read backwards. Column 0 is the first column in both orientations.
                cell.anchorMin = new Vector2(axis.Pivot.x, 1f - upper);
                cell.anchorMax = new Vector2(axis.Pivot.x, 1f - lower);
                cell.sizeDelta = new Vector2(size, -crossSpacing);
            }
            else
            {
                cell.anchorMin = new Vector2(lower, axis.Pivot.y);
                cell.anchorMax = new Vector2(upper, axis.Pivot.y);
                cell.sizeDelta = new Vector2(-crossSpacing, size);
            }

            // Cross pivot is 0.5 in all four directions, so 0 centres the cell in its column band.
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
