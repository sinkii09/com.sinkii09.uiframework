using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Run bookkeeping for OneShotAnimator: starting, validating, snapping and completing a run.
    public sealed partial class OneShotAnimator
    {
        // Returns the run it created — never re-reads _run: completing the previous run resumes its awaiters
        // synchronously, one may call Play again, and that newer run must not be mistaken for this one.
        // Every completion happens after the last field write.
        private UniTaskCompletionSource BeginRun(string state)
        {
            // Decided before any field write: a destroyed object's native properties (isActiveAndEnabled)
            // throw, which would skip completing `previous`. `this == null` also catches a never-activated
            // object Unity destroyed without OnDestroy, before the destroy watch has polled it.
            if (this == null) _destroyed = true;

            UniTaskCompletionSource previous = _run;
            var run = new UniTaskCompletionSource();
            _run = run;
            _runState = state;
            _runHash = Animator.StringToHash(state ?? string.Empty);
            _interrupted = false;

            bool started;
            if (_destroyed)
            {
                _pendingPlay = false;
                started = false;
            }
            else if (!isActiveAndEnabled)
            {
                // Validation waits for OnEnable: the Animator may not be initialised yet.
                _pendingPlay = true;
                WatchDestroyWhileUnawoken();
                previous?.TrySetResult();
                return run;
            }
            else
            {
                _pendingPlay = false;
                started = StartRun();
            }

            if (!started) _run = null;
            previous?.TrySetResult();
            if (!started) run.TrySetResult();
            return run;
        }

        private void StartPending()
        {
            _pendingPlay = false;
            if (StartRun()) return;
            EndRun();
        }

        private bool StartRun()
        {
            Animator animator = CachedAnimator;
            animator.enabled = true;   // validate after enabling: an Animator binds its controller on enable
            if (!IsValid(animator))
            {
                animator.enabled = false;
                _playing = false;
                return false;
            }

            animator.Play(_runHash, _layer, 0f);
            animator.Update(0f);   // pose and state info current now — no stale state, no old pose for a frame
            _playing = true;
            return true;
        }

        // Uses the interrupted run's state, not _state; a pending run was never validated, so check first.
        private void SnapToEnd()
        {
            _interrupted = false;
            Animator animator = CachedAnimator;
            animator.enabled = true;
            if (IsValid(animator))
            {
                animator.Play(_runHash, _layer, 1f);
                animator.Update(0f);
            }
            animator.enabled = false;
        }

        private bool IsValid(Animator animator)
        {
            string problem =
                animator.runtimeAnimatorController == null ? "has no Animator Controller" :
                _layer >= animator.layerCount ? $"has no layer {_layer}" :
                string.IsNullOrEmpty(_runState) || !animator.HasState(_layer, _runHash) ? $"has no state '{_runState}' on layer {_layer}" :
                null;
            if (problem == null) return true;

            if (!_reportedError)
            {
                _reportedError = true;
                Debug.LogError($"[OneShotAnimator] '{name}' {problem}.", this);
            }
            return false;
        }

        // Unity never calls OnDestroy on an object that was never active, so a run deferred on one would
        // never end. UniTask's GameObject destroy trigger polls for that case. The registration is kept for
        // the object's lifetime on purpose: disposing it per run while _watchingDestroy stays set would
        // strand the next deferred run.
        private void WatchDestroyWhileUnawoken()
        {
            if (_awoken || _watchingDestroy) return;

            _watchingDestroy = true;
            gameObject.GetCancellationTokenOnDestroy().Register(() =>
            {
                _destroyed = true;
                _pendingPlay = false;
                EndRun();
            });
        }

        // Always the LAST statement of its caller: TrySetResult resumes awaiters synchronously and one may
        // call Play again — nothing after this line may touch run state.
        private void EndRun()
        {
            UniTaskCompletionSource run = _run;
            _run = null;
            run?.TrySetResult();
        }
    }
}
