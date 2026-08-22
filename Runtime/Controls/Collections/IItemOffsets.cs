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

        /// <summary>Leading edge of an item. Spacing is accumulated into this.</summary>
        float OffsetOf(int index);

        /// <summary>The item's own extent along the scroll axis. Spacing is not part of it.</summary>
        float SizeOf(int index);

        /// <summary>
        /// Greatest index whose start is at or before <paramref name="offset"/> — <i>not</i> a test
        /// for containment within an item's own extent. An offset falling in the gap between two
        /// items belongs to the earlier one, matching the <c>floor(offset / stride)</c> this
        /// replaces; a containment test would find nothing there and leave a reseed with no anchor.
        /// Clamped at both ends: overscroll drives the viewport start negative.
        /// </summary>
        int IndexAt(float offset);
    }
}
