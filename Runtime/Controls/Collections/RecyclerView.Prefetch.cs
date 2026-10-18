using System;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Prefetch hook: tells the consumer, ahead of the window, which indices are about to be bound so
    /// it can warm its own cache — without binding a cell — and which of those hints are no longer
    /// wanted. Off unless a handler is set AND <see cref="RecyclerViewSettings.PrefetchDistance"/> is
    /// above zero; a view that never opts in pays one null check per pump.
    ///
    /// <para>One contiguous range is outstanding at a time and it never overlaps the shown window
    /// after a pump: it sits ahead of the window in the direction the window last moved.</para>
    /// </summary>
    public partial class RecyclerView
    {
        private Action<int, int> _onPrefetch;
        private Action<int, int> _onCancelPrefetch;

        /// <summary>Announced through the prefetch handler, and neither consumed nor cancelled since.</summary>
        private IndexRange _prefetchOutstanding;

        /// <summary>Every index the provider was called for since the last reconcile.</summary>
        private IndexRange _prefetchConsumed;

        /// <summary>The window as of the last reconcile. Empty = no baseline, keep the direction.</summary>
        private IndexRange _prefetchBaseline;

        private bool _prefetchForward = true;

        /// <summary>
        /// How far ahead of the realized window (which already reaches
        /// <see cref="RecyclerViewSettings.CreateDistance"/> past the viewport) indices are announced,
        /// in offset-space units. <c>0</c> = off. Read from the serialized settings; there is no
        /// runtime setter — <see cref="SetPrefetchHandler"/> with <c>null</c> is the off switch.
        /// </summary>
        public float PrefetchDistance => _settings.PrefetchDistance;

        /// <summary>
        /// Scroll speed along the list, in offset-space units per second: positive toward later
        /// items, negative toward earlier ones, in every <see cref="ScrollDirection"/>. Use it to hold
        /// off expensive loads during a hard fling.
        ///
        /// <para>It is the ScrollRect's own velocity, so it is <c>0</c> with inertia off, for a
        /// mouse-wheel step, and for a programmatic jump — the ScrollRect only tracks velocity while
        /// it is dragged or coasting. <c>0</c> before initialization and after destruction.</para>
        /// </summary>
        public float ScrollVelocity
            => IsInitialized && _scrollRect != null ? -_axis.Sign * _axis.Along(_scrollRect.velocity) : 0f;

        /// <summary>
        /// Sets the prefetch hook. Both callbacks take <c>(firstIndex, count)</c>: an ascending run,
        /// <c>count ≥ 1</c>. When the list moves backward the run ends next to the window, so its
        /// LAST index is the nearest — iterate from the end for nearest-first.
        ///
        /// <para><b>Episode rules.</b> Every index announced by <paramref name="onPrefetch"/> ends in
        /// exactly one of two ways: the cell provider is called for it (the bind takes the hint over;
        /// the view never cancels it afterwards — release your prefetch bookkeeping there, even if the
        /// provider throws), or <paramref name="onCancelPrefetch"/> reports it (the list moved away,
        /// reversed, jumped, or the data was reset). Cancel must tolerate indices it no longer tracks.
        /// Key your work by your own record of what the index meant when it was announced, never by
        /// re-reading data at cancel time — it may have changed in between.</para>
        ///
        /// <para><b>Resets.</b> <see cref="SetItemCount"/>, <see cref="SetCellProvider"/> and
        /// <see cref="RefreshAll"/> cancel everything outstanding and the following pump re-announces,
        /// because the data behind an index may have changed (so "append one item" cancels and
        /// re-announces the same indices). Layout changes do not reset. Calling this method again,
        /// even with the same callbacks, cancels through the OLD cancel callback and re-announces —
        /// the way to force a re-issue after a data change that kept the count.</para>
        ///
        /// <para>Callbacks run inside the view's busy window: a mutating call from one is refused and
        /// logged, an exception is logged and the list carries on — a hint never breaks the list.
        /// A disabled view keeps its outstanding hints until it is re-enabled or destroyed; destroying
        /// the view cancels them.</para>
        ///
        /// <para>Callable before <c>Awake</c>. Turning the hook on announces immediately.</para>
        /// </summary>
        /// <param name="onPrefetch">Indices about to be bound. <c>null</c> turns prefetch off.</param>
        /// <param name="onCancelPrefetch">Announced indices no longer wanted. Optional.</param>
        public void SetPrefetchHandler(Action<int, int> onPrefetch, Action<int, int> onCancelPrefetch = null)
        {
            if (onPrefetch == null && onCancelPrefetch != null)
                throw new ArgumentException(
                    "A cancel callback without a prefetch callback can never fire. Pass both, or null to turn prefetch off.",
                    nameof(onCancelPrefetch));

            if (RejectReentrant(nameof(SetPrefetchHandler))) return;

            if (!IsInitialized)
            {
                // Nothing pumps before Awake or after OnDispose, so nothing is outstanding to cancel.
                _onPrefetch = onPrefetch;
                _onCancelPrefetch = onCancelPrefetch;
                ClearPrefetchState();
                return;
            }

            string previous = EnterBusy(PrefetchCallbacks);
            try
            {
                ResetPrefetch();
                _onPrefetch = onPrefetch;
                _onCancelPrefetch = onCancelPrefetch;

                // The same early-outs as PumpCore: no provider means no window to prefetch around.
                if (_provider != null && _content != null) UpdatePrefetch();
            }
            finally
            {
                ExitBusy(previous);
            }
        }

        /// <summary>Records that the provider is being called for an index. Called before the bind, so a throwing provider still consumes.</summary>
        private void NotePrefetchConsumed(int index)
        {
            if (_onPrefetch == null) return;
            _prefetchConsumed = _prefetchConsumed.Include(index);
        }

        /// <summary>
        /// Reconciles the outstanding hints with the window this pump produced. Runs in the pump's
        /// <c>finally</c>, after the flush and the <see cref="ShownIndices"/> rebuild, so a handler sees
        /// this pump's window and a provider throw cannot leave a hint overlapping a shown cell.
        /// </summary>
        private void UpdatePrefetch()
        {
            if (_onPrefetch == null) return;

            IndexRange shown = _shown.Count == 0
                ? IndexRange.Empty
                : new IndexRange(_shown[0].Index, _shown[_shown.Count - 1].Index + 1);

            IndexRange desired = IndexRange.Empty;
            if (!shown.IsEmpty)
            {
                _prefetchForward = PrefetchWindow.NextDirection(_prefetchForward, _prefetchBaseline, shown, _itemCount);

                float distance = _settings.PrefetchDistance;
                desired = _prefetchForward
                    ? PrefetchWindow.ForwardBand(_offsets, shown.End - 1, distance)
                    : PrefetchWindow.BackwardBand(_offsets, shown.Start, distance);
            }

            IndexRange outstanding = _prefetchOutstanding;
            IndexRange consumed = _prefetchConsumed;

            // Adopt before notifying: a callback that throws, or calls back in and is refused, cannot
            // leave the bookkeeping half-updated.
            _prefetchBaseline = shown;
            _prefetchOutstanding = desired;
            _prefetchConsumed = IndexRange.Empty;

            if (outstanding.IsEmpty && desired.IsEmpty) return;

            // Cancels first: they free whatever concurrency the consumer budgets for prefetching.
            PrefetchDiff diff = PrefetchWindow.Diff(outstanding, consumed, desired);
            for (int i = 0; i < diff.CancelCount; i++) NotifyPrefetch(_onCancelPrefetch, diff.Cancel(i));
            for (int i = 0; i < diff.PrefetchCount; i++) NotifyPrefetch(_onPrefetch, diff.Prefetch(i));
        }

        /// <summary>
        /// Cancels every outstanding hint and forgets the baseline; the next pump re-announces. For
        /// the signals that the data behind an index may have changed. Callers hold the busy scope
        /// and have already released the shown cells.
        /// </summary>
        private void ResetPrefetch()
        {
            IndexRange outstanding = _prefetchOutstanding;
            ClearPrefetchState();
            NotifyPrefetch(_onCancelPrefetch, outstanding);
        }

        /// <summary>
        /// A layout change re-packs the same indices: forget the window to compare against, keep the
        /// direction. Repacking is not scrolling, and comparing a list window with a grid window would
        /// read as a jump.
        /// </summary>
        private void RebaselinePrefetch() => _prefetchBaseline = IndexRange.Empty;

        /// <summary>From <c>OnDispose</c>, inside its busy scope: cancel what is outstanding, then let go of the callbacks.</summary>
        private void DisposePrefetch()
        {
            ResetPrefetch();
            _onPrefetch = null;
            _onCancelPrefetch = null;
        }

        private void ClearPrefetchState()
        {
            _prefetchOutstanding = IndexRange.Empty;
            _prefetchConsumed = IndexRange.Empty;
            _prefetchBaseline = IndexRange.Empty;
        }

        /// <summary>
        /// One consumer callback, under its own busy scope so a refusal names it. Caught and logged,
        /// like <see cref="RecyclerCell.OnRecycled"/>: the bookkeeping is already adopted, and in the
        /// pump's <c>finally</c> a throw here would replace the provider's own exception.
        /// </summary>
        private void NotifyPrefetch(Action<int, int> handler, IndexRange range)
        {
            if (handler == null || range.IsEmpty) return;

            string previous = EnterBusy(PrefetchCallbacks);
            try
            {
                handler(range.Start, range.Count);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
            finally
            {
                ExitBusy(previous);
            }
        }
    }
}
