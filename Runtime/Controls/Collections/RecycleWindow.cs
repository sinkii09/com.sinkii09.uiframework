using System;

namespace Sinkii09.UIFramework
{
    /// <summary>One mutation the pump should apply to the shown-cell window.</summary>
    internal enum WindowAction
    {
        None = 0,
        RecycleHead = 1,
        RecycleTail = 2,
        CreateBeforeHead = 3,
        CreateAfterTail = 4,
    }

    /// <summary>Everything <see cref="RecycleWindow.Decide"/> needs, in offset space.</summary>
    internal readonly struct WindowState
    {
        public readonly float ViewportStart;
        public readonly float ViewportSize;
        public readonly int ItemCount;
        public readonly int ShownCount;
        public readonly int Tick;

        public readonly int HeadIndex;
        public readonly float HeadOffset;
        public readonly float HeadSize;
        public readonly int HeadCreatedTick;

        public readonly int TailIndex;
        public readonly float TailOffset;
        public readonly float TailSize;
        public readonly int TailCreatedTick;

        public WindowState(
            float viewportStart, float viewportSize, int itemCount, int shownCount, int tick,
            int headIndex, float headOffset, float headSize, int headCreatedTick,
            int tailIndex, float tailOffset, float tailSize, int tailCreatedTick)
        {
            ViewportStart = viewportStart;
            ViewportSize = viewportSize;
            ItemCount = itemCount;
            ShownCount = shownCount;
            Tick = tick;
            HeadIndex = headIndex;
            HeadOffset = headOffset;
            HeadSize = headSize;
            HeadCreatedTick = headCreatedTick;
            TailIndex = tailIndex;
            TailOffset = tailOffset;
            TailSize = tailSize;
            TailCreatedTick = tailCreatedTick;
        }

        public float ViewportEnd => ViewportStart + ViewportSize;
        public float HeadEnd => HeadOffset + HeadSize;
        public float TailEnd => TailOffset + TailSize;
    }

    /// <summary>
    /// Decides, one step at a time, whether the shown-cell window should grow or shrink.
    ///
    /// <para>Deliberately pure: no Unity types, no state, no side effects. Every recycling bug worth
    /// catching lives in this decision, and keeping it a function means it is exhaustively testable
    /// in EditMode with no scene, no ScrollRect and no frame waits.</para>
    ///
    /// <para><b>Hysteresis.</b> A cell is recycled once it is <c>recycleDistance</c> past the viewport
    /// edge, but created again as soon as the edge is within <c>createDistance</c>. Because recycle
    /// distance is strictly larger, a cell parked between the two bands is left alone — otherwise a
    /// cell sitting on a single shared boundary would be recycled and recreated every frame.</para>
    /// </summary>
    internal static class RecycleWindow
    {
        /// <summary>
        /// Floor for <see cref="MaxIterationsFor"/>, so a viewport too small to hold even one cell
        /// still gets room to settle.
        /// </summary>
        public const int MinIterations = 16;

        /// <summary>
        /// Safety cap on pump iterations for one tick, derived from the geometry rather than fixed.
        ///
        /// <para><b>Why this cannot be a constant.</b> A reseed leaves a single cell and the window
        /// then grows one cell per iteration, so the work to converge scales with how many cells fit
        /// in the viewport plus a create band at each end. A constant that suits 100px rows on a
        /// 500px viewport is exceeded outright by 30px rows on a 1920px one — and being exceeded is
        /// not benign: the pump logs an error and abandons the tick, leaving the list permanently
        /// short of cells. A fixed cap only looks safe because the number it was chosen against was
        /// never written down.</para>
        ///
        /// <para>Doubling covers the mirror case, where a window arrives oversized and must recycle
        /// about as many cells as it creates before it settles.</para>
        /// </summary>
        public static int MaxIterationsFor(float viewportSize, float createDistance, float stride, int itemCount)
        {
            if (stride <= 0f || itemCount <= 0) return MinIterations;

            double span = viewportSize + 2d * createDistance;
            // +2 for the partial cells the span's two edges can straddle.
            double cells = Math.Ceiling(span / stride) + 2d;

            // Never budget for more cells than the list actually has.
            if (cells > itemCount + 1) cells = itemCount + 1;

            int cap = cells >= int.MaxValue / 2 ? int.MaxValue / 2 : (int)cells * 2;
            return Math.Max(MinIterations, cap);
        }

        public static WindowAction Decide(in WindowState state, float recycleDistance, float createDistance)
        {
            if (state.ShownCount <= 0 || state.ItemCount <= 0) return WindowAction.None;

            float recycleBefore = state.ViewportStart - recycleDistance;
            float recycleAfter = state.ViewportEnd + recycleDistance;
            float createBefore = state.ViewportStart - createDistance;
            float createAfter = state.ViewportEnd + createDistance;

            // Recycle before create: a cell freed this tick is reusable from the pool's staging
            // tier immediately, so shrinking first avoids an Instantiate.
            // Never recycle down to an empty window — the pump has no anchor to rebuild from.
            if (state.ShownCount > 1)
            {
                if (state.HeadEnd < recycleBefore && state.HeadCreatedTick != state.Tick)
                    return WindowAction.RecycleHead;

                if (state.TailOffset > recycleAfter && state.TailCreatedTick != state.Tick)
                    return WindowAction.RecycleTail;
            }

            if (state.HeadIndex > 0 && state.HeadOffset > createBefore)
                return WindowAction.CreateBeforeHead;

            if (state.TailIndex < state.ItemCount - 1 && state.TailEnd < createAfter)
                return WindowAction.CreateAfterTail;

            return WindowAction.None;
        }

        /// <summary>
        /// True when the shown window has drifted entirely outside the recycle bands — the result of
        /// a jump rather than a scroll. The pump reseeds from scratch instead of stepping the window
        /// across the gap one cell at a time.
        /// </summary>
        public static bool NeedsReseed(in WindowState state, float recycleDistance)
        {
            if (state.ShownCount <= 0) return state.ItemCount > 0;

            return state.HeadOffset > state.ViewportEnd + recycleDistance
                   || state.TailEnd < state.ViewportStart - recycleDistance;
        }
    }
}
