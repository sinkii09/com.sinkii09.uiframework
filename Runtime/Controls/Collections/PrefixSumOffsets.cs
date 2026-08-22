using System;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Per-item declared sizes, stored as a prefix sum so <see cref="OffsetOf"/> stays O(1) and
    /// <see cref="IndexAt"/> becomes a binary search.
    ///
    /// <para>Sizes are <i>declared</i> — the provider states them before any cell is realised — so
    /// the whole table is exact from the first frame. Nothing here estimates, corrects, or
    /// re-measures; a list never shifts under the user because a guess turned out wrong.</para>
    /// </summary>
    internal sealed class PrefixSumOffsets : IItemOffsets
    {
        private readonly float[] _starts;
        private readonly float[] _sizes;
        private readonly float _spacing;

        public int Count => _starts.Length;
        public float TotalSize { get; }
        public float MinStride { get; }

        /// <summary>
        /// Builds the table by asking <paramref name="sizeProvider"/> for every index.
        ///
        /// <para>The provider is consumer code and can throw. This constructor either produces a
        /// complete table or throws without side effects, so a caller that assigns the result only
        /// on success can never be left holding a half-built store.</para>
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">A declared size is not positive.</exception>
        public PrefixSumOffsets(int count, Func<int, float> sizeProvider, float spacing)
        {
            if (sizeProvider == null) throw new ArgumentNullException(nameof(sizeProvider));
            if (count < 0) count = 0;

            _spacing = spacing;
            _starts = new float[count];
            _sizes = new float[count];

            if (count == 0)
            {
                TotalSize = 0f;
                MinStride = 0f;
                return;
            }

            // Accumulate in double and store float: 50k rows of ~100px reach totals near 5e6, where
            // float carries about half a pixel of representable error. The drift only shows at the
            // far end of a long list, which is the worst place to debug it and the cheapest to avoid.
            double cursor = 0d;
            float minStride = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                float size = sizeProvider(i);

                // A non-positive size lets the window advance without covering ground: it never
                // reaches the create band, so the pump realises every remaining item instead of the
                // handful the viewport needs. Refuse it here, where the offending index is known.
                if (!(size > 0f))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(sizeProvider),
                        $"Declared size for index {i} must be > 0 (was {size}). Every item's size " +
                        "is untrusted input once a size provider is installed.");
                }

                _starts[i] = (float)cursor;
                _sizes[i] = size;

                float stride = size + spacing;
                if (stride < minStride) minStride = stride;

                // (double)size + spacing, not size + spacing: the latter rounds to float first and
                // then widens, which reintroduces per-step error the double accumulator exists to
                // avoid.
                cursor += (double)size + spacing;
            }

            // The trailing gap is not part of the list's span — it sits past the last item.
            TotalSize = (float)(cursor - spacing);
            MinStride = minStride;
        }

        public float OffsetOf(int index) => _starts[index];

        public float SizeOf(int index) => _sizes[index];

        /// <summary>
        /// Greatest index whose start is at or before <paramref name="offset"/>. See
        /// <see cref="IItemOffsets.IndexAt"/> for why this is not a containment test.
        /// </summary>
        public int IndexAt(float offset)
        {
            int count = _starts.Length;
            if (count == 0) return 0;
            if (offset <= 0f) return 0;
            if (offset >= _starts[count - 1]) return count - 1;

            // Invariant: _starts[low] <= offset < _starts[high].
            int low = 0;
            int high = count - 1;

            while (high - low > 1)
            {
                int mid = low + ((high - low) >> 1);
                if (_starts[mid] <= offset) low = mid;
                else high = mid;
            }

            return low;
        }

        /// <summary>
        /// Re-declares one item's size, shifting everything after it. O(n) in the item count — fine
        /// for a tap-to-expand row, wrong for animating a size every frame.
        /// </summary>
        public PrefixSumOffsets WithSize(int index, float size)
        {
            if (index < 0 || index >= _starts.Length)
                throw new ArgumentOutOfRangeException(nameof(index), $"Index {index} is outside [0, {_starts.Length}).");

            float[] sizes = (float[])_sizes.Clone();
            sizes[index] = size;
            return new PrefixSumOffsets(sizes.Length, i => sizes[i], _spacing);
        }
    }
}
