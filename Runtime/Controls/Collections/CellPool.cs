using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Two-tier cell pool, one tier pair per prefab id.
    ///
    /// <para><b>Why two tiers.</b> Cells recycled during the current pump go to <c>_tmp</c> and stay
    /// active. Only <see cref="FlushRecycled"/>, at the end of the pump, deactivates them and moves
    /// them to <c>_pooled</c>. A cell recycled off the head and immediately needed at the tail in the
    /// same frame is therefore reused with no SetActive churn and no Instantiate.</para>
    ///
    /// <para>The factory is injected so the pool can be exercised without prefabs or a scene.</para>
    /// </summary>
    internal class CellPool
    {
        private readonly Func<int, RecyclerCell> _factory;
        private readonly List<RecyclerCell>[] _pooled;
        private readonly List<RecyclerCell>[] _tmp;

        private int _createdCount;
        private int _liveCount;

        /// <summary>Total cells ever instantiated by this pool. Per-instance, never static.</summary>
        public int CreatedCount => _createdCount;

        /// <summary>Cells currently rented out to the view.</summary>
        public int LiveCount => _liveCount;

        public CellPool(int prefabCount, Func<int, RecyclerCell> factory)
        {
            if (prefabCount <= 0) throw new ArgumentOutOfRangeException(nameof(prefabCount));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));

            _pooled = new List<RecyclerCell>[prefabCount];
            _tmp = new List<RecyclerCell>[prefabCount];
            for (int i = 0; i < prefabCount; i++)
            {
                _pooled[i] = new List<RecyclerCell>();
                _tmp[i] = new List<RecyclerCell>();
            }
        }

        public int PooledCount(int prefabId) => _pooled[prefabId].Count;

        public int RecycledThisTickCount(int prefabId) => _tmp[prefabId].Count;

        /// <summary>Instantiates <paramref name="count"/> cells and parks them in the pool.</summary>
        public void Prewarm(int prefabId, int count)
        {
            for (int i = 0; i < count; i++)
            {
                RecyclerCell cell = Create(prefabId);
                cell.gameObject.SetActive(false);
                _pooled[prefabId].Add(cell);
            }
        }

        /// <summary>
        /// Takes a cell for <paramref name="prefabId"/>: same-tick recycled first, then the pool,
        /// then a fresh instantiate.
        /// </summary>
        public RecyclerCell Rent(int prefabId)
        {
            RecyclerCell cell = TakeLiveFrom(_tmp[prefabId]) ?? TakeLiveFrom(_pooled[prefabId]);
            if (cell == null)
            {
                cell = Create(prefabId);
            }

            if (!cell.gameObject.activeSelf) cell.gameObject.SetActive(true);
            _liveCount++;
            return cell;
        }

        /// <summary>Stages a cell for return. It stays active until <see cref="FlushRecycled"/>.</summary>
        public void Recycle(RecyclerCell cell, int prefabId)
        {
            if (cell == null) return;

            cell.OnRecycled();
            cell.Index = -1;
            _tmp[prefabId].Add(cell);
            _liveCount--;
        }

        /// <summary>Ends the tick: deactivates everything staged and moves it to the real pool.</summary>
        public void FlushRecycled()
        {
            for (int id = 0; id < _tmp.Length; id++)
            {
                List<RecyclerCell> staged = _tmp[id];
                for (int i = 0; i < staged.Count; i++)
                {
                    RecyclerCell cell = staged[i];
                    if (cell == null) continue; // destroyed under us (scene unload)

                    cell.gameObject.SetActive(false);
                    _pooled[id].Add(cell);
                }
                staged.Clear();
            }
        }

        /// <summary>Destroys every pooled cell. Rented cells are the view's responsibility.</summary>
        public void DestroyAll()
        {
            for (int id = 0; id < _pooled.Length; id++)
            {
                DestroyAll(_pooled[id]);
                DestroyAll(_tmp[id]);
            }
            _liveCount = 0;
        }

        private static void DestroyAll(List<RecyclerCell> cells)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                // Object.Destroy, never DestroyImmediate — this runs at runtime.
                if (cells[i] != null) UnityEngine.Object.Destroy(cells[i].gameObject);
            }
            cells.Clear();
        }

        /// <summary>
        /// Pops the last entry, skipping any cell Unity has already destroyed (scene unload leaves
        /// non-null C# references whose Unity lifetime is over).
        /// </summary>
        private static RecyclerCell TakeLiveFrom(List<RecyclerCell> cells)
        {
            while (cells.Count > 0)
            {
                int last = cells.Count - 1;
                RecyclerCell cell = cells[last];
                cells.RemoveAt(last);
                if (cell != null) return cell;
            }
            return null;
        }

        private RecyclerCell Create(int prefabId)
        {
            RecyclerCell cell = _factory(prefabId);
            if (cell == null)
                throw new InvalidOperationException($"Cell factory returned null for prefab id {prefabId}.");

            _createdCount++;
            return cell;
        }
    }
}
