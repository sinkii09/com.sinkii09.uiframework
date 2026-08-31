using R3;
using System;
using System.Threading;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    public abstract class ViewModelBase : IViewModel
    {
        // Lifetime bindings — disposed only when the ViewModel is discarded (scope.Dispose via VContainer).
        protected DisposableBag _disposables = new();

        // Per-show bindings — disposed + replaced on every OnHide() call.
        // DisposableBag has no Clear(); always Dispose() then reassign to reset.
        protected DisposableBag _showDisposables = new();

        // The token half of _showDisposables: cancelled when the view hides, replaced on the next
        // show. Never read this field directly — go through ShowToken, which handles the disposed case.
        private CancellationTokenSource _showCts = new();

        private bool _disposed;
        private bool _hiding;

        /// <summary>
        /// Cancelled when the view hides; a fresh token is issued for the next show. Pass this to
        /// any async work started from <see cref="OnShow"/> so it stops when the view goes away —
        /// without it the work outlives the view, and for a cached view it may never stop at all.
        /// </summary>
        /// <remarks>
        /// Returns an already-cancelled token once the ViewModel is disposed: work started against
        /// a dead ViewModel should stop immediately, and reading a disposed source's
        /// <c>Token</c> would throw <see cref="ObjectDisposedException"/>.
        /// <para>PRECONDITION — the reset depends on this ViewModel being owned by the view's child
        /// scope, which is how <c>UIViewFactory</c> creates it: the cache-reuse path calls
        /// <c>FactoryReset()</c> and then resolves a brand-new ViewModel from a brand-new scope
        /// (<c>Lifetime.Scoped</c>), so a re-shown view never inherits a cancelled token. A
        /// ViewModel kept alive outside that ownership model must call <see cref="NotifyHide"/>
        /// itself, or its token stays cancelled forever.</para>
        /// <para>Main thread only, like the rest of the view layer. A reader on another thread can
        /// load the source before <see cref="NotifyHide"/> swaps it and dereference it after the
        /// old one is disposed, which would throw.</para>
        /// </remarks>
        protected CancellationToken ShowToken =>
            _disposed ? new CancellationToken(true) : _showCts.Token;

        public virtual void OnShow() { }

        public void NotifyHide()
        {
            if (_disposed) return;
            // Re-entrancy guard, matching Dispose(): a cancellation callback that calls back into
            // NotifyHide() would otherwise run OnHide() twice and leak the inner call's fresh source.
            if (_hiding) return;
            _hiding = true;

            // Cancel BEFORE OnHide() so an override sees an already-cancelled token — and do NOT
            // dispose the source here: ShowToken would then throw for anything OnHide() touches.
            var previous = _showCts;
            TryCancel(previous);

            try
            {
                OnHide();
                _showDisposables.Dispose();
            }
            finally
            {
                // The swap lives in a finally because a throw from either call above would
                // otherwise strand this ViewModel on the cancelled token forever — every later
                // show would silently no-op all ShowToken-gated work, with nothing to explain why.
                // That is reachable, not theoretical: Button.onClick.RemoveListener throws
                // MissingReferenceException once the Button has been destroyed, and
                // BindButtonAsync registers exactly that disposable into _showDisposables.
                // The exception itself still propagates — only the stranding is prevented.
                _showDisposables = new DisposableBag();
                _showCts = new CancellationTokenSource();

                // Only now is `previous` unreachable. An abandoned source keeps its registrations —
                // and any linked source a consumer built from it — rooted until it is disposed, so
                // a cached view toggled repeatedly would leak in proportion to its show count.
                previous.Dispose();
                _hiding = false;
            }
        }

        protected virtual void OnHide()
        {

        }

        // Idempotent — safe if VContainer calls Dispose() after OnHide() already ran.
        public void Dispose()
        {
            if (_disposed) return;
            // Set FIRST, before cancelling: a cancellation callback that re-enters Dispose() or
            // NotifyHide() must find the guard already closed, or both bags get disposed twice.
            // ShowToken reads this flag, so it never reaches the source disposed just below.
            _disposed = true;

            var previous = _showCts;
            TryCancel(previous);
            previous.Dispose();

            _showDisposables.Dispose();
            _disposables.Dispose();
        }

        // Cancel() invokes registered callbacks synchronously and rethrows whatever they throw,
        // wrapped in an AggregateException. Letting that escape would skip the teardown that
        // follows every call site above and leak every per-show subscription, so it is contained
        // here and logged instead.
        private static void TryCancel(CancellationTokenSource cts)
        {
            try { cts.Cancel(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
