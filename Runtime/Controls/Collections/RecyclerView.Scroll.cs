using UnityEngine;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Programmatic scrolling, including a request made before the view could honour it — before
    /// initialization there is no viewport to measure, and with no items there is nothing to aim at.
    /// Such a request is parked and applied by the first pump that has both.
    /// </summary>
    public partial class RecyclerView
    {
        private bool _hasPendingScroll;
        private int _pendingScrollIndex;
        private float _pendingScrollAlignment;

        /// <summary>
        /// Scrolls so <paramref name="index"/> sits at <paramref name="alignment"/> within the
        /// viewport (0 = leading edge, 1 = trailing edge), clamped to the list's bounds.
        ///
        /// <para>Before initialization, or while the list has no items, the request is kept and
        /// applied by the first pump that can — so "scroll, then set the count" works in either
        /// order. A later request replaces it. A parked request is applied on the first
        /// <c>Update</c>, before that frame's canvas layout: a viewport sized by a layout group may
        /// still report its old size, so an alignment other than 0 can be off for that one frame.</para>
        /// </summary>
        public void ScrollToIndex(int index, float alignment = 0f)
        {
            if (RejectReentrant(nameof(ScrollToIndex))) return;

            if (!IsInitialized)
            {
                RequestScroll(index, alignment);
                return;
            }

            string previous = EnterBusy(CellCallbacks);
            try
            {
                ScrollToIndexCore(index, alignment);
                PumpCore();
            }
            finally
            {
                ExitBusy(previous);
            }
        }

        private void RequestScroll(int index, float alignment)
        {
            _hasPendingScroll = true;
            _pendingScrollIndex = index;
            _pendingScrollAlignment = alignment;
        }

        /// <summary>Moves the content only — no guard, no pump. Callers hold the busy scope.</summary>
        private void ScrollToIndexCore(int index, float alignment)
        {
            if (_itemCount <= 0)
            {
                // Nothing to aim at yet; keep the request for when there is.
                RequestScroll(index, alignment);
                return;
            }

            // Whatever asked for this is newer than any parked request.
            _hasPendingScroll = false;

            index = Mathf.Clamp(index, 0, _itemCount - 1);
            float viewportSize = _axis.SizeOf(_viewport.rect);

            // The alignment term needs THIS row's size: aligning index 400 to the viewport's trailing
            // edge lands short or long by however much that row differs from the declared default.
            float target = OffsetOf(index) - Mathf.Clamp01(alignment) * (viewportSize - _offsets.SizeOf(index));
            target = Mathf.Clamp(target, 0f, Mathf.Max(0f, TotalSize - viewportSize));

            Vector2 current = _content.anchoredPosition;
            float cross = _axis.Horizontal ? current.y : current.x;
            _content.anchoredPosition = _axis.Compose(-_axis.Sign * target, cross);

            // A programmatic move must not leave inertia behind, or the ScrollRect immediately
            // drifts away from the position we just set.
            _scrollRect.velocity = Vector2.zero;
        }
    }
}
