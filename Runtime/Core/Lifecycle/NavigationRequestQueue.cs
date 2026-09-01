using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Single-consumer FIFO queue for GameLifecycleManager's navigation requests.
    //
    // GLM's awaitable entry points refuse a request that arrives while another transition is
    // running. That is correct for a caller who is waiting on the result, but it silently discards
    // code-driven navigation races — a win condition and a timeout firing in the same frame, a
    // gameplay script changing state mid-transition. This queue runs those requests after the
    // in-flight one instead of dropping them.
    //
    // GLM ONLY. UINavigator is deliberately not a participant: GLM calls into the navigator, so a
    // shared queue would deadlock immediately.
    //
    // Main-thread only — same threading contract as GLM's _isTransitioning bool.
    //
    // WHY EVERY ENQUEUE PATH IS void: a queued item runs after the current one, so a caller that
    // is itself running inside the current item (a state's OnEnterAsync, a view's OnHideAsync
    // during the stack clear, a ViewModel teardown) would deadlock the drain if it could await its
    // own queued request. There is no way to detect that caller — UniTask does not flow
    // ExecutionContext, so AsyncLocal cannot identify a request's causal chain, and every flag
    // wide enough to be safe is also wide enough to reject nearly everything. Removing the await
    // handle makes the deadlock inexpressible through this API instead.
    internal sealed class NavigationRequestQueue : IDisposable
    {
        internal enum Kind { ChangeState, LoadScene, Restart }

        // Identifies "the same request" for dedup. Target is null for Restart; SceneName is null
        // for everything except LoadScene, where two loads of different scenes must NOT collapse.
        internal readonly struct Identity : IEquatable<Identity>
        {
            internal readonly Kind Kind;
            internal readonly Type Target;
            internal readonly string SceneName;

            internal Identity(Kind kind, Type target = null, string sceneName = null)
            {
                Kind = kind;
                Target = target;
                SceneName = sceneName;
            }

            public bool Equals(Identity other)
                => Kind == other.Kind && Target == other.Target && SceneName == other.SceneName;

            public override bool Equals(object obj) => obj is Identity other && Equals(other);

            public override int GetHashCode()
                => ((int)Kind * 397) ^ (Target?.GetHashCode() ?? 0) ^ (SceneName?.GetHashCode() ?? 0);

            public override string ToString()
                => SceneName != null
                    ? $"{Kind}<{Target?.Name}>('{SceneName}')"
                    : Target != null ? $"{Kind}<{Target.Name}>" : Kind.ToString();
        }

        private sealed class Item
        {
            internal Identity Id;
            internal Func<CancellationToken, UniTask<NavigationResult>> Work;
            internal CancellationToken CallerCt;
        }

        // A queue this deep already means something is spamming navigation; running all of them
        // would be worse than telling the caller (in the console) that the tail was dropped.
        internal const int MaxPendingRequests = 8;

        // List rather than Queue<T> so dedup can scan, and so a future need to drop a specific
        // item does not force a data-structure change.
        private readonly List<Item> _pending = new();
        private readonly Func<bool> _isIdle;
        private readonly CancellationTokenSource _shutdownCts;
        private readonly CancellationToken _shutdownToken;

        private bool _isDraining;
        private bool _shuttingDown;
        private bool _ctsDisposed;

        // True once shutdown has begun by either route: an explicit Dispose (VContainer tearing the
        // scope down) or the application exit token firing.
        private bool ShuttingDown => _shuttingDown || _shutdownToken.IsCancellationRequested;

        internal bool IsDraining => _isDraining;
        internal int PendingCount => _pending.Count;

        // isIdle must report whether a queued item can run RIGHT NOW without being refused by a
        // guard. GLM passes a predicate covering its own transition flag, the navigator's, and
        // whether boot has started — see GameLifecycleManager's constructor.
        internal NavigationRequestQueue(Func<bool> isIdle, CancellationToken exitToken = default)
        {
            _isIdle = isIdle ?? throw new ArgumentNullException(nameof(isIdle));
            _shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(exitToken);
            _shutdownToken = _shutdownCts.Token;
        }

        // Fire-and-forget. See the void rationale on the class.
        //
        // callerCt is read when the item comes up to run, which is after this method returns. If
        // the caller's CancellationTokenSource has been disposed by then the token simply reads as
        // never-cancelled (verified on this runtime: reading, linking and registering all succeed
        // against a disposed source), so the request still runs. Cancel before disposing if the
        // intent was to call the request off.
        internal void Enqueue(Identity id, Func<CancellationToken, UniTask<NavigationResult>> work,
            CancellationToken callerCt)
        {
            if (ShuttingDown)
            {
                Debug.LogWarning($"[NavigationRequestQueue] Shutting down — {id} dropped.");
                return;
            }

            // Dedup requires an equal token as well as an equal identity. Keeping only the older
            // item would otherwise let its cancellation drop a second caller's still-live request.
            // Note this means a caller minting a fresh CancellationTokenSource per call never
            // dedups; the depth cap is what bounds that case.
            // Collapsing is silent by design: it is the intended response to spam, and logging it
            // per occurrence would be noise on exactly the frames it is protecting against.
            // Restart carries no target in its identity, so two queued restarts collapse even if
            // the caller meant them for different current states — harmless, since only the state
            // current at execution time can be restarted anyway.
            for (int i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].Id.Equals(id) && _pending[i].CallerCt == callerCt)
                    return;
            }

            if (_pending.Count >= MaxPendingRequests)
            {
                Debug.LogWarning(
                    $"[NavigationRequestQueue] Queue full ({MaxPendingRequests}) — {id} dropped.");
                return;
            }

            _pending.Add(new Item { Id = id, Work = work, CallerCt = callerCt });

            if (_isDraining) return;
            // Set synchronously, BEFORE starting the loop. A UniTask async method runs synchronously
            // up to its first await, but a second Enqueue in the same frame must not be able to see
            // _isDraining == false and start a second drain.
            _isDraining = true;
            DrainAsync().Forget();
        }

        private async UniTaskVoid DrainAsync()
        {
            try
            {
                while (_pending.Count > 0)
                {
                    // Declared outside the try so every catch can guarantee forward progress by
                    // removing the head — otherwise a throw from the token check would leave the
                    // same item at index 0 and spin the loop forever.
                    Item item = null;

                    // The whole body is guarded: a throw from the peek, the token check or the
                    // wait must not escape the loop, strand every remaining item, and surface only
                    // as a UniTaskScheduler.UnobservedTaskException from the Forget above.
                    try
                    {
                        if (ShuttingDown) break;

                        // PEEK, do not remove. An item waiting for the guards to clear has not run
                        // yet, so a duplicate arriving during that wait must still collapse into
                        // it, and it must still count against the depth cap. Removing it here made
                        // it invisible to both.
                        item = _pending[0];

                        if (item.CallerCt.IsCancellationRequested)
                        {
                            RemoveHead(item);
                            Debug.LogWarning($"[NavigationRequestQueue] {item.Id} cancelled before it ran.");
                            continue;
                        }

                        // The entry points still hold their own guards, so an item invoked while a
                        // transition is in flight would simply be refused. Waiting here is what
                        // makes this a queue rather than a delayed retry — and it is safe only
                        // because nothing can await a queued item.
                        await UniTask.WaitUntil(_isIdle, cancellationToken: _shutdownToken);

                        if (ShuttingDown) break;

                        // Committed to running it now — remove before invoking so the work cannot
                        // observe its own request still pending, and so a re-entrant enqueue of the
                        // same identity is not collapsed into an item that is already executing.
                        RemoveHead(item);

                        // The wait spans frames; the caller may have cancelled during it.
                        if (item.CallerCt.IsCancellationRequested)
                        {
                            Debug.LogWarning($"[NavigationRequestQueue] {item.Id} cancelled while queued.");
                            continue;
                        }

                        // Linked to shutdown as well as the caller: nothing holds a handle to a
                        // fire-and-forget transition, so without this a scope teardown that is not
                        // app exit (a scene-scoped LifetimeScope) would leave it running on and
                        // driving views that are already being destroyed.
                        using var linked =
                            CancellationTokenSource.CreateLinkedTokenSource(item.CallerCt, _shutdownToken);

                        NavigationResult result = await item.Work(linked.Token);
                        if (result != NavigationResult.Completed)
                        {
                            // Without this the item would die inside a generic "Transitioning —
                            // ignored" warning indistinguishable from an ordinary refusal, and a
                            // queue that silently drains into rejections would look like it works.
                            Debug.LogError(
                                $"[NavigationRequestQueue] {item.Id} ran but was {result} — the queued " +
                                "request did not take effect. Either it was legitimately refused (a " +
                                "restart with no current state, a push past the depth limit), or " +
                                "something navigated concurrently, or the idle predicate does not " +
                                "cover the guard that refused it.");
                        }
                    }
                    // Covers both the OperationCanceledException from the wait and the
                    // ObjectDisposedException from a token source torn down underneath it. Neither
                    // is a caller bug during shutdown, so neither should be reported as one.
                    catch (Exception) when (ShuttingDown)
                    {
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        // The caller cancelled a transition that had already started. That is a
                        // designed-for outcome, not a fault — reporting it as an unhandled
                        // exception would both misdiagnose it and fail PlayMode test runs.
                        RemoveHead(item);
                        Debug.LogWarning(
                            $"[NavigationRequestQueue] {item?.Id} was cancelled while running.");
                    }
                    catch (Exception e)
                    {
                        RemoveHead(item);
                        Debug.LogException(e);
                    }
                }
            }
            finally
            {
                _isDraining = false;
                // Dispose deferred to here when shutdown raced an active drain — disposing the
                // source while WaitUntil holds its token is what produced a misleading
                // ObjectDisposedException report.
                if (ShuttingDown) DisposeCts();
            }
        }

        // Idempotent: only removes the item if it is still the head. Dispose clears the list out
        // from under an in-flight drain, so "already gone" is a normal outcome, not an error.
        private void RemoveHead(Item item)
        {
            if (item == null || _pending.Count == 0) return;
            if (ReferenceEquals(_pending[0], item)) _pending.RemoveAt(0);
        }

        private void DisposeCts()
        {
            if (_ctsDisposed) return;
            _ctsDisposed = true;
            _shutdownCts.Dispose();
        }

        public void Dispose()
        {
            if (_shuttingDown) return;
            _shuttingDown = true;
            _pending.Clear();
            if (!_ctsDisposed) _shutdownCts.Cancel();
            // If a drain is running it owns the token and disposes the source in its finally.
            if (!_isDraining) DisposeCts();
        }
    }
}
