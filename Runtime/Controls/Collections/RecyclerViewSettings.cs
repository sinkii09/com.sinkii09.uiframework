using System;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Tuning block for <see cref="RecyclerView"/>. Serialized on the view itself.
    /// </summary>
    [Serializable]
    public class RecyclerViewSettings
    {
        // Grouped under [Header]s that carry the unit; the tooltips say what each number does. The
        // reasoning behind the values lives in these comments.

        // Declared rather than measured: content size and the recycle window must be computable
        // before any cell exists.
        [Header("Cells · px")]
        [Tooltip("Size of every cell along the scroll axis, in px — the height in a vertical list, " +
                 "the width in a horizontal one. SetItemSizeProvider overrides it per item.")]
        [SerializeField] private float _cellSize = 100f;

        [Tooltip("Gap along the scroll axis between consecutive cells (rows, in a grid), in px.")]
        [SerializeField] private float _spacing;

        // 1 is the default so a view left at 1 behaves exactly as it did before grids existed.
        // Cross-axis rather than "columns": a LeftToRight list's columns are its rows.
        [Header("Grid · 1 = plain list")]
        [Tooltip("Cells side by side across the scroll axis. 1 = a plain list; above 1 = a grid with " +
                 "that many columns (rows, in a horizontal list).")]
        [Min(1)]
        [SerializeField] private int _crossAxisCount = 1;

        [Tooltip("Gap between cells across the scroll axis, in px. Grids only; half of it also pads " +
                 "the outside of the first and last columns.")]
        [SerializeField] private float _crossSpacing;

        // Create before Recycle: the window grows out to one and is trimmed back at the other.
        // The gap between them is hysteresis: without it a cell sitting on the boundary is recycled
        // and recreated every frame while the user scrolls back and forth.
        [Header("Cell window · px past viewport · Recycle > Create")]
        [Tooltip("Cells are created and bound this far before they scroll into view, in px. Larger = " +
                 "more time for their content to load, and more live cells.")]
        [SerializeField] private float _createDistance = 200f;

        [Tooltip("A cell this far outside the viewport, in px, is recycled back to the pool. Must be " +
                 "larger than Create Distance.")]
        [SerializeField] private float _recycleDistance = 300f;

        [Header("Pool")]
        [Tooltip("Cells instantiated into the pool at startup, so the first show has no Instantiate " +
                 "spike. Covers the first cell prefab only.")]
        [SerializeField] private int _prewarmCount = 12;

        // Measured from the realized cells, which already reach CreateDistance past the viewport.
        [Header("Prefetch · px · 0 = off")]
        [Tooltip("Items up to this far, in px, past the created cells are announced to the " +
                 "SetPrefetchHandler callback so their data can start loading early. 0 = off (default).")]
        [Min(0f)]
        [SerializeField] private float _prefetchDistance;

        public float CellSize => _cellSize;
        public float Spacing => _spacing;

        /// <summary>
        /// Cells per row across the scroll axis; 1 is a plain list.
        ///
        /// <para><b>Clamped on read rather than trusted, and <c>0</c> is treated as "not authored".</b>
        /// This field did not exist before v3.4, so an asset serialized before it does not carry the
        /// key. Unity is expected to keep the initializer's <c>1</c> in that case — but this is a
        /// package consumed through a pinned tag by projects whose assets cannot be inspected from
        /// here, and if that expectation is ever wrong the raw value is <c>0</c>. Reading through
        /// this clamp costs nothing and makes the difference unobservable; <see cref="Validate"/>
        /// therefore rejects only genuinely negative values, which no absent field can produce.
        /// <c>[Min(1)]</c> on the field stops the Inspector from being the source of a <c>0</c>.</para>
        /// </summary>
        public int CrossAxisCount => _crossAxisCount < 1 ? 1 : _crossAxisCount;

        public float CrossSpacing => _crossSpacing < 0f ? 0f : _crossSpacing;

        /// <summary>True when cells must be laid out in rows rather than one per stride.</summary>
        public bool IsGrid => CrossAxisCount > 1;
        public float RecycleDistance => _recycleDistance;
        public float CreateDistance => _createDistance;
        public int PrewarmCount => _prewarmCount;

        /// <summary>
        /// Offset-space distance past the realized window within which indices are announced to the
        /// prefetch handler. <c>0</c> = off. An asset serialized before v3.8 lacks the key and reads
        /// <c>0</c> either way, which is exactly the off it means.
        /// </summary>
        public float PrefetchDistance => _prefetchDistance;

        /// <summary>
        /// Distance between the starts of two consecutive <b>strides</b> — cells in a list, rows in
        /// a grid.
        /// </summary>
        public float Stride => _cellSize + _spacing;

        /// <summary>
        /// Changes the column count at runtime. Only <see cref="RecyclerView.SetCrossAxisCount"/>
        /// should call this: on its own it rewrites a number the live cells were already laid out
        /// against, and the view has to rebuild its offsets and re-place everything afterwards.
        /// </summary>
        internal void OverrideCrossAxisCount(int count)
        {
            _crossAxisCount = count < 1 ? 1 : count;
        }

        /// <summary>
        /// Throws if the settings are internally inconsistent. Called once at initialization —
        /// these are authoring errors, so fail fast rather than degrade.
        /// </summary>
        public void Validate()
        {
            if (_cellSize <= 0f)
                throw new ArgumentException($"{nameof(CellSize)} must be > 0 (was {_cellSize}).");

            if (_spacing < 0f)
                throw new ArgumentException($"{nameof(Spacing)} must be >= 0 (was {_spacing}).");

            // Negative only, deliberately. Zero is what an asset serialized before this field
            // existed would produce if Unity ever failed to apply the initializer, and throwing on
            // it would break every list shipped before v3.4 the moment its scene opened — a far
            // worse outcome than silently treating it as the 1 it means. [Min(1)] keeps the
            // Inspector from being the source of a 0, so a typed 0 is not a case that can arise.
            if (_crossAxisCount < 0)
                throw new ArgumentException(
                    $"{nameof(CrossAxisCount)} cannot be negative (was {_crossAxisCount}). Use 1 for a list.");

            if (_crossSpacing < 0f)
                throw new ArgumentException($"{nameof(CrossSpacing)} must be >= 0 (was {_crossSpacing}).");

            // Without this gap a cell sitting exactly on the boundary is recycled and recreated
            // every frame.
            if (_recycleDistance <= _createDistance)
                throw new ArgumentException(
                    $"{nameof(RecycleDistance)} ({_recycleDistance}) must exceed " +
                    $"{nameof(CreateDistance)} ({_createDistance}) to provide recycle hysteresis.");

            if (_prewarmCount < 0)
                throw new ArgumentException($"{nameof(PrewarmCount)} must be >= 0 (was {_prewarmCount}).");

            // Written negated so NaN fails too.
            if (!(_prefetchDistance >= 0f) || float.IsInfinity(_prefetchDistance))
                throw new ArgumentException(
                    $"{nameof(PrefetchDistance)} must be a finite value >= 0 (was {_prefetchDistance}). Use 0 for off.");
        }
    }
}
