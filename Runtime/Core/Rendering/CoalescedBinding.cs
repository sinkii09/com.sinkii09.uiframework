using System;
using R3;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sinkii09.UIFramework
{
    // One coalesced one-way binding: applies the first value synchronously, then at most one write
    // per scheduler frame carrying the newest value.
    //
    // The leading edge is ONE-SHOT per subscription, not per idle period. Per-idle-period would
    // reproduce R3's ThrottleFirstLastFrame shape, which in 1.3.1 emits default(T) immediately
    // after a legitimate value once a previous window has closed — a score label flashing "0".
    // That operator is why this class exists instead of a two-line composition.
    //
    // Why the first value cannot simply be throttled like the rest: view creation is async
    // (loader -> InitializeAsync -> BindViewModel). A bind completing at or after this frame's
    // pump would flush next frame, and the view visibly flashes unbound state.
    internal sealed class CoalescedBinding<T> : IFrameRunnerWorkItem, IDisposable
    {
        private readonly FrameProvider _frames;
        private readonly Action<T> _apply;
        private readonly Object _target;
        private readonly bool _hasUnityTarget;

        private IDisposable _subscription;
        private T _latest;
        private bool _isFirst = true;
        private bool _dirty;
        private bool _registered;
        private bool _disposed;

        // target may be null for a non-Unity target (the generic BindTo overload accepts any
        // class). ReferenceEquals distinguishes "no Unity target supplied" from "a Unity target
        // that has since been destroyed" — plain != null cannot, because Unity overloads it.
        internal CoalescedBinding(Observable<T> source, FrameProvider frames, Action<T> apply, Object target)
        {
            _frames = frames;
            _apply = apply;
            _target = target;
            _hasUnityTarget = !ReferenceEquals(target, null);
            _subscription = source.Subscribe(OnNext);
        }

        private void OnNext(T value)
        {
            if (_disposed) return;

            _latest = value;
            _dirty = true;

            if (_isFirst)
            {
                // ORDER IS LOAD-BEARING: _isFirst is cleared BEFORE Apply so that a value pushed
                // back in during that first apply takes the scheduling branch below rather than
                // recursing into this one. Do not "tidy" this by moving the assignment down.
                _isFirst = false;
                _dirty = false;
                Apply(value);

                // The first apply may itself have pushed a new value in; schedule that one.
                if (_dirty && !_registered)
                {
                    _registered = true;
                    _frames.Register(this);
                }
                return;
            }

            if (!_registered)
            {
                _registered = true;
                _frames.Register(this);
            }
        }

        // The whole body is guarded, not just the apply: the Unity-null check and Dispose can
        // throw too, and an escape would be treated by the scheduler as a foreign work item —
        // removed and forwarded — silently killing this binding for the view's whole life.
        public bool MoveNext(long frame)
        {
            try
            {
                if (_disposed) return false;

                // A value written during this view's lifetime can outlive the view itself.
                // Dropping the binding beats spamming MissingReferenceException every frame.
                if (_hasUnityTarget && _target == null)
                {
                    Dispose();
                    return false;
                }

                _dirty = false;
                Apply(_latest);

                // The apply may have disposed this binding (a write that triggers its own view's
                // hide). Re-checked so a push-back in that same call stack cannot keep a dead
                // binding registered for an extra frame.
                if (_disposed) return false;

                // Apply pushed a value straight back into the source — BindTwoWay's shape: writing
                // toggle.isOn fires onValueChanged, which assigns property.Value. Without this
                // re-check the binding would go idle HOLDING an unapplied value, and the UI would
                // stay stale until some unrelated later emission. _registered stays true across
                // Apply, so the re-entrant push cannot double-register.
                if (_dirty) return true;

                _registered = false;
                return false;
            }
            catch (Exception e)
            {
                // Nested guard, for the same reason Pump has one: a custom ILogHandler can throw.
                // If that escaped, the scheduler would treat this binding as a foreign work item
                // and drop it — the exact keep-alive failure this class exists to prevent.
                try { Debug.LogException(e); }
                catch { }
                _registered = false;
                return false;
            }
        }

        private void Apply(T value)
        {
            try
            {
                _apply(value);
            }
            catch (Exception e)
            {
                // Logged and KEPT ALIVE, matching BindButtonAsync's fault-containment policy.
                // Dropping the binding would silently stop this element updating for the rest of
                // the view's life — the worse of the two failures, and the harder to diagnose.
                Debug.LogException(e);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _subscription?.Dispose();
            _subscription = null;

            // Deliberately does NOT unregister from the scheduler.
            //
            // Dispose can be reached from inside Apply (a binding whose write triggers a hide).
            // Freeing the slot there lets a re-entrant Register immediately reuse it, and the
            // pump's own Remove for this item would then evict that innocent new binding —
            // permanently, silently, and only when both halves land in one call stack.
            //
            // The pump owns ALL removal: the next MoveNext sees _disposed, returns false, and the
            // item is removed exactly once, by the code that knows the correct slot.
        }
    }
}
