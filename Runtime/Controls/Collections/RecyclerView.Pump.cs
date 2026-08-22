using UnityEngine;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// The per-frame loop: reads the scroll position, then grows and shrinks the shown-cell window
    /// until <see cref="RecycleWindow"/> reports nothing left to do.
    /// </summary>
    public partial class RecyclerView
    {
        private void Update() => Pump();

        private void Pump()
        {
            // Re-entrancy: a provider that mutates the list while we are walking it would corrupt
            // the window mid-iteration. Bail rather than recurse.
            if (_inPump || _provider == null || _content == null) return;

            _inPump = true;
            try
            {
                PumpCore();
            }
            finally
            {
                _inPump = false;
            }
        }

        private void PumpCore()
        {
            _tick++;

            if (_itemCount <= 0)
            {
                ReleaseAllShownInternal();
                _pool.FlushRecycled();
                _shownIndices.Clear();
                return;
            }

            float viewportStart = _axis.ViewportStart(_content.anchoredPosition);
            float viewportSize = _axis.SizeOf(_viewport.rect);

            if (RecycleWindow.NeedsReseed(BuildState(viewportStart, viewportSize), _settings.RecycleDistance))
                Reseed(viewportStart);

            // Budgeted from the actual geometry: a reseed grows the window one cell per iteration,
            // so the ceiling has to scale with how many cells the viewport plus its create bands
            // hold. See RecycleWindow.MaxIterationsFor.
            int maxIterations = RecycleWindow.MaxIterationsFor(
                viewportSize, _settings.CreateDistance, _offsets.MinStride, _itemCount);

            int iterations = 0;
            while (true)
            {
                WindowAction action = RecycleWindow.Decide(
                    BuildState(viewportStart, viewportSize),
                    _settings.RecycleDistance, _settings.CreateDistance);

                if (action == WindowAction.None) break;

                if (++iterations > maxIterations)
                {
                    Debug.LogError(
                        $"[RecyclerView] '{name}' window failed to converge in {maxIterations} steps " +
                        $"(shown={_shown.Count}, viewportStart={viewportStart:F1}, " +
                        $"minStride={_offsets.MinStride:F1}). Aborting this tick.", this);
                    break;
                }

                Apply(action);
            }

            _pool.FlushRecycled();
            RebuildShownIndices();
            VerifyMeasurements();
        }

        private WindowState BuildState(float viewportStart, float viewportSize)
        {
            if (_shown.Count == 0)
            {
                return new WindowState(viewportStart, viewportSize, _itemCount, 0, _tick,
                    0, 0f, 0f, 0, 0, 0f, 0f, 0);
            }

            CellHandle head = _shown[0];
            CellHandle tail = _shown[_shown.Count - 1];

            return new WindowState(
                viewportStart, viewportSize, _itemCount, _shown.Count, _tick,
                head.Index, head.Offset, head.DeclaredSize, head.CreatedTick,
                tail.Index, tail.Offset, tail.DeclaredSize, tail.CreatedTick);
        }

        private void Apply(WindowAction action)
        {
            switch (action)
            {
                case WindowAction.RecycleHead:
                    ReleaseAt(0);
                    break;
                case WindowAction.RecycleTail:
                    ReleaseAt(_shown.Count - 1);
                    break;
                case WindowAction.CreateBeforeHead:
                    _shown.Insert(0, CreateAt(_shown[0].Index - 1));
                    break;
                case WindowAction.CreateAfterTail:
                    _shown.Add(CreateAt(_shown[_shown.Count - 1].Index + 1));
                    break;
            }
        }

        /// <summary>
        /// Rebuilds the window from scratch at the current scroll position. Used after a jump, where
        /// stepping the window across the gap one cell at a time would cost one create per skipped
        /// item.
        /// </summary>
        private void Reseed(float viewportStart)
        {
            ReleaseAllShownInternal();

            int first = _offsets.IndexAt(viewportStart);
            _shown.Add(CreateAt(first));
        }

        private void RebuildShownIndices()
        {
            _shownIndices.Clear();
            for (int i = 0; i < _shown.Count; i++) _shownIndices.Add(_shown[i].Index);
        }

        private bool RejectReentrant(string caller)
        {
            // Two distinct windows during which the view is mid-build. The pump is the long-standing
            // one; the offset rebuild is separate because the size provider runs outside the pump,
            // so _inPump alone leaves that path unguarded.
            if (!_inPump && !_rebuildingOffsets) return false;

            string from = _inPump ? "a cell provider" : "a size provider";
            Debug.LogError(
                $"[RecyclerView] '{name}': {caller} was called from inside {from}. " +
                "Mutating the list while it is being built is not supported — the call was ignored.",
                this);
            return true;
        }

        /// <summary>
        /// Sizes are declared, so a cell whose real rect disagrees means it self-sized (a
        /// ContentSizeFitter or LayoutGroup on the cell). The window still reasons about the declared
        /// size, so the list silently develops gaps and overlaps — surface it loudly rather than let
        /// it ship.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void VerifyMeasurements()
        {
            if (_shown.Count == 0) return;

            VerifyMeasurement(_shown[0]);
            if (_shown.Count > 1) VerifyMeasurement(_shown[_shown.Count - 1]);
        }

        private void VerifyMeasurement(CellHandle handle)
        {
            float measured = ContentLayout.MeasureCell(handle.Rect, _axis);
            if (Mathf.Abs(measured - handle.DeclaredSize) < 0.5f) return;

            Debug.LogError(
                $"[RecyclerView] '{name}' cell at index {handle.Index} measured {measured:F1} but " +
                $"was declared {handle.DeclaredSize:F1}. The view owns cell size — fix the size " +
                "provider (or CellSize) to match, and remove any ContentSizeFitter/LayoutGroup " +
                "driving the scroll axis.", this);
        }
    }
}
