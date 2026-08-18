using UnityEngine;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Direction cells are laid out in, starting from the list's first item.
    /// </summary>
    public enum ScrollDirection
    {
        TopToBottom = 0,
        BottomToTop = 1,
        LeftToRight = 2,
        RightToLeft = 3,
    }

    /// <summary>
    /// Collapses the four scroll directions into one code path.
    ///
    /// Everything upstream of this struct works in <b>offset space</b>: a single float that always
    /// grows positively from the start of the list, regardless of direction. Cell <c>i</c> occupies
    /// the half-open range <c>[offset_i, offset_i + size_i)</c>. This struct is the only place that
    /// knows how offset space maps onto Unity's anchored positions.
    /// </summary>
    public readonly struct ScrollAxis
    {
        /// <summary>False = the list scrolls vertically.</summary>
        public readonly bool Horizontal;

        /// <summary>Maps an offset-space value onto the anchored-position component.</summary>
        public readonly float Sign;

        /// <summary>Anchor and pivot preset shared by the content root and every cell.</summary>
        public readonly Vector2 Pivot;

        private ScrollAxis(bool horizontal, float sign, Vector2 pivot)
        {
            Horizontal = horizontal;
            Sign = sign;
            Pivot = pivot;
        }

        public static ScrollAxis From(ScrollDirection direction)
        {
            switch (direction)
            {
                case ScrollDirection.TopToBottom: return new ScrollAxis(false, -1f, new Vector2(0.5f, 1f));
                case ScrollDirection.BottomToTop: return new ScrollAxis(false, +1f, new Vector2(0.5f, 0f));
                case ScrollDirection.LeftToRight: return new ScrollAxis(true, +1f, new Vector2(0f, 0.5f));
                case ScrollDirection.RightToLeft: return new ScrollAxis(true, -1f, new Vector2(1f, 0.5f));
                default: return new ScrollAxis(false, -1f, new Vector2(0.5f, 1f));
            }
        }

        /// <summary>Size of a rect along the scroll axis.</summary>
        public float SizeOf(Rect rect) => Horizontal ? rect.width : rect.height;

        /// <summary>Component of a vector along the scroll axis.</summary>
        public float Along(Vector2 value) => Horizontal ? value.x : value.y;

        /// <summary>Builds an anchored position from an along-axis and a cross-axis component.</summary>
        public Vector3 Compose(float along, float cross)
            => Horizontal ? new Vector3(along, cross, 0f) : new Vector3(cross, along, 0f);

        /// <summary>Converts an offset-space value into an anchored-position component.</summary>
        public float ToLocal(float offsetFromStart) => Sign * offsetFromStart;

        /// <summary>
        /// Converts the content root's own anchored position into the offset of the viewport's
        /// leading edge. As the user scrolls toward later items this always increases, in every
        /// direction.
        ///
        /// <para><b>Not the inverse of <see cref="ToLocal"/>.</b> <c>ToLocal</c> places cells
        /// <i>inside</i> the content; this reads the content's <i>own</i> position, and the content
        /// travels the opposite way to reveal later items — so the two are negatives, and composing
        /// them is meaningless. The value this inverts is the content position written by
        /// <c>RecyclerView.ScrollToIndex</c>, <c>-Sign * offset</c>.</para>
        /// </summary>
        public float ViewportStart(Vector2 contentAnchoredPosition)
            => -Sign * Along(contentAnchoredPosition);

        /// <summary>Axis to pass to <c>RectTransform.SetSizeWithCurrentAnchors</c>.</summary>
        public RectTransform.Axis RectAxis
            => Horizontal ? RectTransform.Axis.Horizontal : RectTransform.Axis.Vertical;
    }
}
