namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Lays items out in rows of <see cref="ItemsPerStride"/> by <b>wrapping</b> a table built over
    /// rows rather than over items.
    ///
    /// <para>The scroll axis does not care that a row holds several items: a grid is still a
    /// one-dimensional stack of rows, and the inner table already solves that exactly. So this type
    /// owns nothing but the index arithmetic — every offset, every size, the total extent and the
    /// binary search all come from proven code. The column a cell sits in is not stored here at all;
    /// it is <c>index % ItemsPerStride</c>, computed where the cell is placed.</para>
    ///
    /// <para><b>The inner table is built over the ROW count</b>, <c>ceil(itemCount / columns)</c>,
    /// and a size provider given a row index. Handing it the item count instead makes the content
    /// rect that many times too tall, with nothing to show for it on screen.</para>
    /// </summary>
    internal sealed class GridOffsets : IItemOffsets
    {
        private readonly IItemOffsets _rows;
        private readonly int _columns;
        private readonly int _itemCount;

        /// <param name="rows">Table over ROWS. Its <c>Count</c> is the row count, not the item count.</param>
        /// <param name="columns">Items per row. Must be at least 1.</param>
        /// <param name="itemCount">Items, which the last row may hold fewer of.</param>
        public GridOffsets(IItemOffsets rows, int columns, int itemCount)
        {
            _rows = rows;
            _columns = columns < 1 ? 1 : columns;
            _itemCount = itemCount < 0 ? 0 : itemCount;
        }

        /// <summary>Items, not rows — every index the view speaks in is an item index.</summary>
        public int Count => _itemCount;

        /// <summary>
        /// Extent of the rows. Unchanged by how full the last row is: a row of one item is still a
        /// row, and the content has to be tall enough to scroll to it.
        /// </summary>
        public float TotalSize => _rows.TotalSize;

        /// <summary>One row's advance. The pump spends this on <see cref="ItemsPerStride"/> cells.</summary>
        public float MinStride => _rows.MinStride;

        public int ItemsPerStride => _columns;

        public float OffsetOf(int index) => _rows.OffsetOf(RowOf(index));

        public float SizeOf(int index) => _rows.SizeOf(RowOf(index));

        /// <summary>
        /// First item of the row at <paramref name="offset"/>. See <see cref="IItemOffsets.IndexAt"/>
        /// for why this must be the first and not the last: the pump grows its window outward from
        /// whatever this returns, so anchoring on the row's last item strands the rest of the row.
        /// </summary>
        public int IndexAt(float offset)
        {
            if (_itemCount <= 0) return 0;

            int index = _rows.IndexAt(offset) * _columns;

            // Clamp to the first index of the LAST row, never to the last item. For a consistent
            // table this cannot fire, but if it ever did, clamping to _itemCount - 1 would hand back
            // a mid-row index and quietly break the "first of the row" contract the pump reseeds on.
            int lastRowStart = (_itemCount - 1) / _columns * _columns;
            return index > lastRowStart ? lastRowStart : index;
        }

        private int RowOf(int index)
        {
            if (index < 0) return 0;

            int row = index / _columns;
            int lastRow = _rows.Count - 1;
            return lastRow >= 0 && row > lastRow ? lastRow : row;
        }
    }
}
