using System;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Every item the same declared size — the no-size-provider default, and phase 1's behaviour
    /// verbatim.
    ///
    /// <para>This type exists to be the regression anchor: each member below is the literal
    /// expression the pre-variable-size <see cref="RecyclerView"/> inlined, so a view without a size
    /// provider computes exactly what it always did. If a member here has to change to make a test
    /// pass, uniform behaviour has drifted and the change is wrong.</para>
    /// </summary>
    internal readonly struct UniformOffsets : IItemOffsets
    {
        private readonly float _cellSize;
        private readonly float _spacing;
        private readonly int _count;

        /// <summary>
        /// The field initializer for a view's offsets, so <c>TotalSize</c> answers before
        /// <c>OnInitialize</c> has run — it is public and another component's <c>Awake</c> can
        /// reach it.
        /// </summary>
        public static readonly UniformOffsets Empty = new UniformOffsets(0, 0f, 0f);

        public UniformOffsets(int count, float cellSize, float spacing)
        {
            _count = count < 0 ? 0 : count;
            _cellSize = cellSize;
            _spacing = spacing;
        }

        private float Stride => _cellSize + _spacing;

        public int Count => _count;

        // Was: _itemCount * _settings.Stride - _settings.Spacing
        public float TotalSize => _count <= 0 ? 0f : _count * Stride - _spacing;

        public float MinStride => _count <= 0 ? 0f : Stride;

        // Was: index * _settings.Stride
        public float OffsetOf(int index) => index * Stride;

        // Was: _settings.CellSize
        public float SizeOf(int index) => _cellSize;

        // Was: Mathf.Clamp(Mathf.FloorToInt(viewportStart / _settings.Stride), 0, _itemCount - 1)
        public int IndexAt(float offset)
        {
            if (_count <= 0) return 0;

            float stride = Stride;
            if (stride <= 0f) return 0;

            int index = (int)Math.Floor(offset / stride);
            if (index < 0) return 0;
            return index > _count - 1 ? _count - 1 : index;
        }
    }
}
