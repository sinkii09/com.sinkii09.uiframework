using System;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// The prefetch band and its bookkeeping, as pure functions. Pure for the same reason as
    /// <see cref="RecycleWindow"/>: every rule here can be pinned in EditMode without a scene.
    ///
    /// <para>Bands are measured from the REALIZED window — which already reaches
    /// <c>CreateDistance</c> past the viewport — so the distance is "how far ahead of the cells that
    /// exist", not "how far ahead of the screen".</para>
    /// </summary>
    internal static class PrefetchWindow
    {
        /// <summary>
        /// Items after <paramref name="tail"/> whose stride starts within <paramref name="distance"/>
        /// of the tail's end. Whole strides: a grid announces whole rows, because the last index is
        /// rounded up to its row's end.
        /// </summary>
        public static IndexRange ForwardBand(IItemOffsets offsets, int tail, float distance)
        {
            int count = offsets.Count;
            if (!(distance > 0f) || tail < 0 || tail >= count - 1) return IndexRange.Empty;

            float reach = offsets.OffsetOf(tail) + offsets.SizeOf(tail) + distance;

            // Past the end short-circuits without IndexAt: a huge distance would otherwise feed
            // UniformOffsets a double whose int cast is unspecified.
            int last;
            if (reach >= offsets.TotalSize)
            {
                last = count - 1;
            }
            else
            {
                last = offsets.IndexAt(reach) + offsets.ItemsPerStride - 1;
                if (last > count - 1) last = count - 1;
            }

            return last > tail ? new IndexRange(tail + 1, last + 1) : IndexRange.Empty;
        }

        /// <summary>
        /// Items before <paramref name="head"/> back to the stride covering <c>headStart − distance</c>.
        ///
        /// <para>Not a mirror image of <see cref="ForwardBand"/> when the distance is shorter than the
        /// spacing: a gap belongs to the stride BEFORE it (see <see cref="IItemOffsets.IndexAt"/>), so
        /// the backward band reaches one stride where the forward band reaches none.</para>
        /// </summary>
        public static IndexRange BackwardBand(IItemOffsets offsets, int head, float distance)
        {
            if (!(distance > 0f) || head <= 0 || head >= offsets.Count) return IndexRange.Empty;

            float reach = offsets.OffsetOf(head) - distance;
            int first = reach <= 0f ? 0 : offsets.IndexAt(reach);
            return first < head ? new IndexRange(first, head) : IndexRange.Empty;
        }

        /// <summary>
        /// Which side of the window to prefetch on. Read from the window's own movement rather than
        /// the raw scroll delta, so it inherits the create/recycle hysteresis — a dead band of about
        /// <c>RecycleDistance − CreateDistance − Spacing</c> — instead of needing a constant of its own.
        ///
        /// <para>The list's ends decide outright: at the head there is nothing behind, at the tail
        /// nothing ahead (this also keeps an elastic bounce from flipping it). With no
        /// <paramref name="previous"/> window to compare against, the current direction stands.
        /// A window that only grew moves forward, one that only shrank moves backward; anything else
        /// (a viewport resize on both edges, no change) keeps the current direction.</para>
        /// </summary>
        public static bool NextDirection(bool forward, IndexRange previous, IndexRange current, int itemCount)
        {
            if (current.IsEmpty) return forward;
            if (current.Start <= 0) return true;
            if (current.End >= itemCount) return false;
            if (previous.IsEmpty) return forward;

            bool grew = current.Start > previous.Start || current.End > previous.End;
            bool shrank = current.Start < previous.Start || current.End < previous.End;
            return grew == shrank ? forward : grew;
        }

        /// <summary>
        /// Reconciles the announced range with the band the view now wants.
        ///
        /// <para><paramref name="consumed"/> — indices the provider was called for since the last
        /// reconcile — leaves <paramref name="outstanding"/> silently: a bind ends a hint, and
        /// cancelling it afterwards could abort the very load the bound cell is waiting on. What
        /// remains and is outside <paramref name="desired"/> is cancelled; what <paramref name="desired"/>
        /// holds beyond what remains is announced. The caller's new outstanding range is
        /// <paramref name="desired"/>.</para>
        /// </summary>
        public static PrefetchDiff Diff(IndexRange outstanding, IndexRange consumed, IndexRange desired)
        {
            var diff = new PrefetchDiff();

            Subtract(outstanding, consumed, out IndexRange before, out IndexRange after);

            Subtract(before, desired, out IndexRange cancel0, out IndexRange cancel1);
            Subtract(after, desired, out IndexRange cancel2, out IndexRange cancel3);
            diff.AddCancel(cancel0);
            diff.AddCancel(cancel1);
            diff.AddCancel(cancel2);
            diff.AddCancel(cancel3);

            if (desired.IsEmpty) return diff;

            // desired \ (before ∪ after), walking left to right; before lies entirely below after.
            int cursor = desired.Start;
            cursor = AnnounceUpTo(ref diff, desired, before, cursor);
            cursor = AnnounceUpTo(ref diff, desired, after, cursor);
            if (cursor < desired.End) diff.AddPrefetch(new IndexRange(cursor, desired.End));

            return diff;
        }

        /// <summary><c>a \ b</c> as its ascending pieces. Either may come back empty.</summary>
        internal static void Subtract(IndexRange a, IndexRange b, out IndexRange left, out IndexRange right)
        {
            if (a.IsEmpty)
            {
                left = right = IndexRange.Empty;
                return;
            }

            if (b.IsEmpty || b.End <= a.Start || b.Start >= a.End)
            {
                left = a;
                right = IndexRange.Empty;
                return;
            }

            left = new IndexRange(a.Start, b.Start);
            right = new IndexRange(b.End, a.End);
        }

        /// <summary>Announces the part of <paramref name="desired"/> between the cursor and <paramref name="covered"/>, then skips it.</summary>
        private static int AnnounceUpTo(ref PrefetchDiff diff, IndexRange desired, IndexRange covered, int cursor)
        {
            if (covered.IsEmpty || covered.End <= cursor || covered.Start >= desired.End) return cursor;

            if (covered.Start > cursor) diff.AddPrefetch(new IndexRange(cursor, covered.Start));
            return Math.Max(cursor, covered.End);
        }
    }
}
