namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Where every item sits in offset space, and how big it is. Pure — no Unity types, no state
    /// beyond what a constructor was given.
    ///
    /// <para><b>Spacing is included in offsets and excluded from sizes.</b> The two are not
    /// interchangeable and conflating them is silent: <see cref="SizeOf"/> feeds
    /// <c>CellHandle.EndOffset</c>, which <see cref="RecycleWindow.Decide"/> compares against the
    /// recycle and create bands, so folding a gap into it would widen every hysteresis threshold.
    /// <see cref="OffsetOf"/> accumulates the gaps, because that is where cells actually go.
    /// The invariant tying them together is
    /// <c>OffsetOf(i + 1) == OffsetOf(i) + SizeOf(i) + spacing</c>.</para>
    ///
    /// <para>Anything wanting "how far one item advances" asks for <see cref="MinStride"/>, never a
    /// spacing-less minimum.</para>
    ///
    /// <para><b>The invariant above holds per STRIDE, not per item.</b> A grid puts
    /// <see cref="ItemsPerStride"/> items at the same offset, so consecutive indices within one row
    /// advance by nothing and the equality only applies across a row boundary. Offsets are therefore
    /// non-decreasing rather than strictly increasing, and anything deriving an index from an offset
    /// has to say which end of the run it means — see <see cref="IndexAt"/>.
    /// <see cref="RecycleWindow.Decide"/> is unaffected: it grows and shrinks the window by index
    /// adjacency and only compares offsets against the viewport, never against each other.</para>
    /// </summary>
    internal interface IItemOffsets
    {
        int Count { get; }

        /// <summary>
        /// Span of the whole list — the last item's end. Excludes the trailing gap, so it matches the
        /// content rect a consumer expects to scroll through. Equal to
        /// <c>OffsetOf(Count - 1) + SizeOf(Count - 1)</c> up to a rounding step: an implementation may
        /// carry it at higher precision than the per-item values it hands back.
        /// </summary>
        float TotalSize { get; }

        /// <summary>
        /// Smallest <c>SizeOf(i) + spacing</c> in the list — the least distance one pump iteration
        /// can advance, which is what bounds the iteration budget. <c>0f</c> when empty, so
        /// <see cref="RecycleWindow.MaxIterationsFor"/> falls into its non-positive guard.
        /// </summary>
        float MinStride { get; }

        /// <summary>
        /// How many items share one stride — <c>1</c> for a list, the column count for a grid.
        ///
        /// <para>Exists because the pump realises one <i>item</i> per iteration while
        /// <see cref="MinStride"/> measures one <i>row</i>. Without this factor
        /// <see cref="RecycleWindow.MaxIterationsFor"/> budgets rows for a loop that spends cells,
        /// comes up short by exactly this number, so the pump logs an error and abandons the tick.
        /// The window resumes growing on the next Update, so the list is not stuck — it is one error
        /// per frame until it catches up, which is loud, slow, and entirely avoidable.</para>
        ///
        /// <para>The table knows the packing; the caller does not, which is why this is read from
        /// here rather than passed in.</para>
        /// </summary>
        int ItemsPerStride { get; }

        /// <summary>Leading edge of an item. Spacing is accumulated into this.</summary>
        float OffsetOf(int index);

        /// <summary>The item's own extent along the scroll axis. Spacing is not part of it.</summary>
        float SizeOf(int index);

        /// <summary>
        /// <b>First</b> index of the stride whose start is at or before <paramref name="offset"/> —
        /// <i>not</i> a test for containment within an item's own extent. An offset falling in the
        /// gap between two strides belongs to the earlier one, matching the
        /// <c>floor(offset / stride)</c> this replaces; a containment test would find nothing there
        /// and leave a reseed with no anchor. Clamped at both ends: overscroll drives the viewport
        /// start negative.
        ///
        /// <para>"First of the stride" rather than "greatest index at or before" because the two
        /// differ in a grid: every item in a row starts at the same offset, and the greatest of them
        /// is the row's <i>last</i> item. Reseeding from there would drop the rest of the row, and
        /// the pump only grows the window outward from its anchor — so those cells would never come
        /// back until the row left the viewport entirely. For a list the two definitions coincide.</para>
        /// </summary>
        int IndexAt(float offset);
    }
}
