using System;
using System.Threading;
using R3;
using R3.Collections;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // The UI's own frame clock. One work item is registered on a host FrameProvider (Unity's
    // PostLateUpdate by default) and it pumps every registration made against this scheduler.
    //
    // PostLateUpdate is deliberate: Unity renders after it, so a value written during Update is
    // flushed and drawn in the SAME frame. A view never shows a frame of stale state.
    //
    // GetFrameCount() returns this scheduler's own counter, incremented once per pump — not
    // Time.frameCount. The distinction is what lets rendering be suspended later without every
    // throttle window silently expiring while nothing is being drawn.
    public sealed class UIRenderScheduler : FrameProvider, IUIRenderScheduler, IDisposable
    {
        private readonly object _gate = new object();

        // MUST remain a field. FreeListCore<T> is a mutable struct — copying it into a local
        // would silently discard every Add/Remove made through the copy.
        private FreeListCore<IFrameRunnerWorkItem> _items;

        private readonly FrameProvider _host;
        private readonly PumpWorkItem _pump;
        private readonly int _maxSuspendedFrames;
        private bool _registered;
        private long _frame;
        private volatile bool _disposed;

        // _suspendCount is written only through Interlocked, which supplies its own barriers, so it
        // must NOT also be volatile. The three diagnostic fields below are written from both the
        // pump thread (TrackSuspendedFrame) and whichever thread releases a handle
        // (ReleaseSuspend), so they are volatile — without that, an ARM device can miss the
        // per-episode reset and either suppress the next episode's warning or report frames
        // accumulated in a previous one.
        private int _suspendCount;
        private volatile int _suspendedFrames;
        private volatile bool _capWarned;
        private volatile bool _underflowWarned;

        public const int DefaultMaxSuspendedFrames = 600;

        public FrameProvider Frames => this;

        // Reports false once disposed. A disposed scheduler can never pump again, so a game polling
        // this for liveness would otherwise read "permanently suspended" forever after teardown —
        // a false leak alarm from the very API added to detect real ones.
        public bool IsSuspended => !_disposed && Volatile.Read(ref _suspendCount) > 0;

        public int SuspendedFrames => _disposed ? 0 : _suspendedFrames;

        // host is injectable purely so tests can drive (and evict) the pump deterministically.
        // Production always wants the default.
        public UIRenderScheduler(FrameProvider host = null, int maxSuspendedFrames = DefaultMaxSuspendedFrames)
        {
            _host = host ?? UnityFrameProvider.PostLateUpdate;
            _maxSuspendedFrames = maxSuspendedFrames;
            _items = new FreeListCore<IFrameRunnerWorkItem>(_gate);
            _pump = new PumpWorkItem(this);
            EnsureRegistered();
        }

        public override long GetFrameCount() => _frame;

        // INVARIANT: registration is NEVER gated on suspension.
        //
        // Gating this on _suspendCount is the obvious-looking optimisation, and it is a silent
        // data-loss bug: a binding that went quiet before the suspension has already unregistered
        // itself, so it re-registers HERE when its next value arrives. Refuse that registration and
        // the binding is stranded holding an unapplied value — it would never flush, not even on
        // resume, and nothing would report it.
        public override void Register(IFrameRunnerWorkItem callback)
        {
            if (_disposed) return;
            _items.Add(callback, out _);
        }

        // Non-null registrations currently held. Test-only: the free list's span includes empty
        // slots, so Length is not a count.
        internal int RegisteredCount
        {
            get
            {
                var span = _items.AsSpan();
                int count = 0;
                for (int i = 0; i < span.Length; i++)
                    if (span[i] != null) count++;
                return count;
            }
        }

        // See IUIRenderScheduler.Suspend for semantics and the deliberate absence of framework
        // call sites.
        public IDisposable Suspend()
        {
            // Suspending a disposed scheduler is inert rather than an error: it can never pump
            // again, and ReleaseSuspend early-returns once disposed, so an increment here could
            // never be undone — stranding the count and pinning IsSuspended true forever.
            if (_disposed) return new SuspendHandle(null);

            Interlocked.Increment(ref _suspendCount);
            return new SuspendHandle(this);
        }

        private void ReleaseSuspend()
        {
            if (_disposed) return;

            int remaining = Interlocked.Decrement(ref _suspendCount);

            if (remaining < 0)
            {
                // Only reachable if a handle decremented twice through a path the idempotency guard
                // missed. Flooring it silently would cancel a still-live suspension elsewhere, so
                // this is reported rather than swallowed.
                Interlocked.Exchange(ref _suspendCount, 0);
                if (!_underflowWarned)
                {
                    _underflowWarned = true;
                    Debug.LogError("[UIRenderScheduler] Suspend refcount went negative — a handle was " +
                                   "released more than once. Rendering has been force-resumed.");
                }
                remaining = 0;
            }

            if (remaining == 0)
            {
                // End of the episode. All three reset together — _underflowWarned included, so a
                // second, genuinely distinct double-release later in the session is still reported
                // rather than silently swallowed by the first one's latch.
                _suspendedFrames = 0;
                _capWarned = false;
                _underflowWarned = false;
            }
        }

        // Idempotent by design. A second registration would put TWO pumps on the host, advancing
        // the frame counter twice per frame and silently halving every ThrottleLastFrame(N) window
        // pointed at this scheduler.
        internal void EnsureRegistered()
        {
            if (_registered || _disposed) return;
            _registered = true;
            _host.Register(_pump);
        }

        // Runs every host frame. MUST NOT THROW and MUST NOT return false while alive: this single
        // work item covers every coalesced binding in the process, and the host FrameProvider
        // evicts a work item permanently on both. An escape here would kill all coalescing for the
        // rest of the session after one logged error, with the UI simply never updating again.
        internal void Pump()
        {
            if (_disposed) return;

            if (Volatile.Read(ref _suspendCount) > 0)
            {
                TrackSuspendedFrame();
                // _frame is deliberately NOT advanced. Nothing is pumped while suspended, so no
                // window could expire anyway — but a frozen counter keeps GetFrameCount()
                // consistent with "no frames happened here" for anything that reads it later.
                return;
            }

            _frame++;

            try
            {
                var span = _items.AsSpan();
                int captured = span.Length;

                for (int i = 0; i < captured; i++)
                {
                    // Re-acquired every iteration: an item's MoveNext can register re-entrantly
                    // (a binding whose setter feeds back into its own source), which may resize
                    // the backing array and invalidate a span held across the loop.
                    span = _items.AsSpan();

                    // The span can also SHRINK (Clear, or a tail-freeing Remove). Indexing past
                    // it would throw, inside the one method that must never throw.
                    if (i >= span.Length) break;

                    var item = span[i];
                    if (item == null) continue;

                    bool keep;
                    try
                    {
                        keep = item.MoveNext(_frame);
                    }
                    catch (Exception ex)
                    {
                        // R3's FrameProvider contract, honoured uniformly: a throwing work item is
                        // removed and forwarded. Deliberately NOT the framework's own keep-alive
                        // policy — foreign items (ObserveOn, DebounceFrame, and ThrottleLastFrame
                        // itself) land in this list too, and re-entering a corrupt one every frame
                        // forever is worse than dropping it. CoalescedBinding keeps ITSELF alive
                        // by catching internally, so it never reaches this path.
                        _items.Remove(i);

                        // Nested guard, mirroring UnityFrameProvider.Run: the unhandled-exception
                        // handler is user-installable and can itself throw.
                        try { ObservableSystem.GetUnhandledExceptionHandler().Invoke(ex); }
                        catch { }
                        continue;
                    }

                    if (!keep) _items.Remove(i);
                }
            }
            catch (Exception ex)
            {
                // Whole-loop backstop. The per-item guard above cannot cover the span arithmetic,
                // Remove, or the handler lookup itself — and this method escaping is the one
                // failure that takes the entire binding system down silently.
                try { Debug.LogException(ex); }
                catch { }
            }
        }

        // Reports a suspension that has outlived any plausible bulk-simulation window, which almost
        // always means a leaked handle and therefore a permanently frozen UI.
        //
        // It deliberately does NOT force-resume: the framework cannot distinguish a leak from a
        // legitimately long catch-up, and cutting the latter short would corrupt exactly the case
        // this feature exists for. LogError rather than LogWarning because a frozen UI is not a
        // warning-severity outcome — it needs to reach build logs and crash reporters. Games that
        // want a hard liveness guarantee can poll SuspendedFrames and act on it themselves.
        private void TrackSuspendedFrame()
        {
            _suspendedFrames++;

            if (_capWarned || _maxSuspendedFrames <= 0 || _suspendedFrames <= _maxSuspendedFrames)
                return;

            // Once per episode, not once per frame — reset by ReleaseSuspend.
            _capWarned = true;
            Debug.LogError(
                $"[UIRenderScheduler] UI rendering has been suspended for {_suspendedFrames} frames " +
                $"(cap {_maxSuspendedFrames}). This usually means a Suspend() handle was never " +
                "disposed, leaving the UI permanently frozen. Rendering stays suspended — the " +
                "framework cannot tell a leak from a long simulation.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // The host offers no Unregister; the pump removes itself by returning false on its
            // next tick (see PumpWorkItem).
            _items.Clear(removeArray: true);
        }

        private sealed class SuspendHandle : IDisposable
        {
            private UIRenderScheduler _owner;

            internal SuspendHandle(UIRenderScheduler owner) => _owner = owner;

            // Idempotent. A `using` block plus an explicit Dispose (or any double-release) must
            // decrement exactly once — otherwise the refcount underflows and a suspension still
            // held by someone else is silently cancelled. Interlocked so a background simulation
            // thread releasing its own handle is safe.
            public void Dispose()
            {
                UIRenderScheduler owner = Interlocked.Exchange(ref _owner, null);
                owner?.ReleaseSuspend();
            }
        }

        private sealed class PumpWorkItem : IFrameRunnerWorkItem
        {
            private readonly UIRenderScheduler _owner;

            internal PumpWorkItem(UIRenderScheduler owner) => _owner = owner;

            public bool MoveNext(long _)
            {
                if (_owner._disposed)
                {
                    // Let the host drop us, and allow a later EnsureRegistered to re-attach.
                    _owner._registered = false;
                    return false;
                }

                _owner.Pump();

                // Always true while alive — including when the list is empty. Returning false on
                // an idle frame would have the host evict the pump, and nothing would ever
                // re-register it.
                return true;
            }
        }
    }
}
