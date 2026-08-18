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
        [Tooltip("Declared uniform cell size along the scroll axis. Phase 1 requires this up front: " +
                 "a cell's real size is only knowable after Bind, but content size and the recycle " +
                 "window must be computable before any cell exists.")]
        [SerializeField] private float _cellSize = 100f;

        [Tooltip("Gap between consecutive cells, along the scroll axis.")]
        [SerializeField] private float _spacing;

        [Tooltip("Distance past the viewport edge at which a cell is recycled. Must exceed " +
                 "CreateDistance — the gap is hysteresis that stops cells thrashing in and out " +
                 "when the user scrolls back and forth across the boundary.")]
        [SerializeField] private float _recycleDistance = 300f;

        [Tooltip("Distance from the viewport edge at which the next cell is created.")]
        [SerializeField] private float _createDistance = 200f;

        [Tooltip("Cells instantiated and pooled at init, to avoid an Instantiate spike on first show.")]
        [SerializeField] private int _prewarmCount = 12;

        public float CellSize => _cellSize;
        public float Spacing => _spacing;
        public float RecycleDistance => _recycleDistance;
        public float CreateDistance => _createDistance;
        public int PrewarmCount => _prewarmCount;

        /// <summary>Distance between the starts of two consecutive cells.</summary>
        public float Stride => _cellSize + _spacing;

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

            // Without this gap a cell sitting exactly on the boundary is recycled and recreated
            // every frame.
            if (_recycleDistance <= _createDistance)
                throw new ArgumentException(
                    $"{nameof(RecycleDistance)} ({_recycleDistance}) must exceed " +
                    $"{nameof(CreateDistance)} ({_createDistance}) to provide recycle hysteresis.");

            if (_prewarmCount < 0)
                throw new ArgumentException($"{nameof(PrewarmCount)} must be >= 0 (was {_prewarmCount}).");
        }
    }
}
