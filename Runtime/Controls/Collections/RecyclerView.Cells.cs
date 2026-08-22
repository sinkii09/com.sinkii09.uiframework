using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Cell lifecycle: turning a data index into a placed, bound cell and handing it back again.
    /// Kept apart from the pump so the loop reads as decisions, not bookkeeping.
    /// </summary>
    public partial class RecyclerView
    {
        /// <summary>
        /// Recycled <see cref="CellHandle"/> records. Handles are plain C# objects allocated once
        /// per window slot; without this, steady-state scrolling would allocate one per created
        /// cell and defeat the zero-GC goal.
        /// </summary>
        private readonly Stack<CellHandle> _handlePool = new();

        private CellHandle CreateAt(int index)
        {
            RecyclerCell cell = Bind(index);

            CellHandle handle = _handlePool.Count > 0 ? _handlePool.Pop() : new CellHandle();
            handle.Cell = cell;
            handle.Rect = (RectTransform)cell.transform;
            handle.Index = index;
            handle.PrefabId = _pendingPrefabId;
            handle.Offset = OffsetOf(index);
            handle.DeclaredSize = _offsets.SizeOf(index);
            handle.CreatedTick = _tick;

            ContentLayout.SizeCell(handle.Rect, handle.DeclaredSize, _axis);
            ContentLayout.PlaceCell(handle.Rect, handle.Offset, _axis);
            return handle;
        }

        private void ReleaseAt(int slot)
        {
            CellHandle handle = _shown[slot];
            _shown.RemoveAt(slot);
            _pool.Recycle(handle.Cell, handle.PrefabId);

            handle.Cell = null;
            handle.Rect = null;
            _handlePool.Push(handle);
        }

        private void ReleaseAllShownInternal()
        {
            for (int i = _shown.Count - 1; i >= 0; i--) ReleaseAt(i);
        }

        /// <summary>Recycles every live cell. Safe to call from the public API, outside the pump.</summary>
        private void ReleaseAllShown()
        {
            if (_pool == null) return;

            ReleaseAllShownInternal();
            _pool.FlushRecycled();
            _shownIndices.Clear();
        }

        /// <summary>
        /// Re-asks the provider for an index already on screen, keeping its slot and position. The
        /// old cell is staged first so the provider's <see cref="RentCell{T}"/> call reuses it
        /// rather than instantiating.
        /// </summary>
        private void Rebind(int slot)
        {
            CellHandle handle = _shown[slot];
            int index = handle.Index;
            _pool.Recycle(handle.Cell, handle.PrefabId);

            RecyclerCell cell;
            try
            {
                cell = Bind(index);
            }
            catch
            {
                // The old cell is already staged and FlushRecycled is about to deactivate it. Drop
                // the slot rather than leave the window pointing at a cell that is on its way back
                // to the pool — the next pump refills the gap.
                _shown.RemoveAt(slot);
                handle.Cell = null;
                handle.Rect = null;
                _handlePool.Push(handle);
                _pool.FlushRecycled();
                RebuildShownIndices();
                throw;
            }

            handle.Cell = cell;
            handle.Rect = (RectTransform)cell.transform;
            handle.PrefabId = _pendingPrefabId;

            // Size here too, not only in CreateAt: this is the second bind path, and it re-rents —
            // on a multi-prefab list the replacement comes from a different pool carrying whatever
            // size its previous index had. Invisible to any single-prefab test.
            //
            // Read the table rather than handle.DeclaredSize: the two agree today only because every
            // path that changes sizes also releases every cell, which is an invariant elsewhere in
            // the file rather than anything this method enforces.
            handle.DeclaredSize = _offsets.SizeOf(index);
            ContentLayout.SizeCell(handle.Rect, handle.DeclaredSize, _axis);
            ContentLayout.PlaceCell(handle.Rect, handle.Offset, _axis);
            _pool.FlushRecycled();
        }

        /// <summary>
        /// Runs the consumer's provider for one index and validates what comes back. Both failures
        /// here are contract violations that would otherwise corrupt the window silently.
        /// </summary>
        private RecyclerCell Bind(int index)
        {
            _pendingPrefabId = -1;
            _pendingCell = null;

            RecyclerCell cell;
            _binding = true;
            try
            {
                cell = _provider(index);
            }
            finally
            {
                _binding = false;
            }

            if (cell == null)
            {
                // A provider that rented and then failed would otherwise leak that cell for the
                // lifetime of the view: nothing else holds a reference to it.
                ReturnPendingCell();

                throw new InvalidOperationException(
                    $"[RecyclerView] '{name}' cell provider returned null for index {index}. " +
                    "The item count is authoritative — every index in [0, ItemCount) must bind.");
            }

            if (_pendingPrefabId < 0)
                throw new InvalidOperationException(
                    $"[RecyclerView] '{name}' cell provider for index {index} did not obtain its " +
                    $"cell from {nameof(RentCell)}, so it could never be recycled.");

            cell.Index = index;
            _pendingCell = null;
            return cell;
        }

        /// <summary>Hands a cell the provider rented but never returned back to the pool.</summary>
        private void ReturnPendingCell()
        {
            if (_pendingCell == null) return;

            _pool.Recycle(_pendingCell, _pendingPrefabId);
            _pendingCell = null;
        }
    }
}
