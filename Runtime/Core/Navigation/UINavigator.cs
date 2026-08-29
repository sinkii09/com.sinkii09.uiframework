using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;

namespace Sinkii09.UIFramework
{
    // Facade over NavigationStack + UIStateMachine.
    // Views are auto-wired from UIViewRegistry. Call Register<>() only for manual overrides.
    // All navigation ops are main-thread only — _isTransitioning bool is not async-safe across threads.
    public class UINavigator : IUINavigator
    {
        private readonly INavigationStack _stack;
        private readonly IUIStateMachine _stateMachine;
        private readonly IUIViewFactory _factory;
        private readonly UIRootLayerRefs _layers;
        private readonly UIBackdrop _backdrop;

        private readonly Dictionary<Type, Func<CancellationToken, UniTask<IUIView>>> _creators = new();
        private readonly Dictionary<Type, Func<object, CancellationToken, UniTask<IUIView>>> _argsCreators = new();

        private bool _isTransitioning;
        // Set during the state machine portion of ChangeStateAsync while _isTransitioning is still true.
        // ShowAsync skips the block when this is set so state OnEnterAsync can push views.
        // External callers also bypass the check during this window (unavoidable without redesigning
        // the IGameState.OnEnterAsync → ShowAsync call chain), but layer blocking prevents actual user
        // input from reaching buttons while _isTransitioning is true.
        private bool _stateTransitionActive;

        public IUIView Current => _stack.Peek();
        public bool IsTransitioning => _isTransitioning;

        // `backdrop` is trailing-optional for SOURCE compatibility only — existing tests construct
        // this positionally. It is NOT an optional dependency: VContainer ignores C# default
        // parameter values, so UIFrameworkLifetimeScope registers a UIBackdrop unconditionally.
        // Every use below is null-guarded purely so hand-built test containers stay simple.
        [Inject]
        public UINavigator(INavigationStack stack, IUIStateMachine stateMachine,
            IUIViewFactory factory, IReadOnlyList<UIViewRegistration> registrations,
            UIRootLayerRefs layers, UIBackdrop backdrop = null)
        {
            _stack = stack;
            _stateMachine = stateMachine;
            _factory = factory;
            _layers = layers;
            _backdrop = backdrop;
            foreach (var r in registrations)
                _creators[r.ViewType] = ct => _factory.CreateAsync(r.ViewType, r.VmType, r.Key, ct);
        }

        // Manual override — use when a view needs args or a custom creation path.
        public void Register<TView, TViewModel>()
            where TView : IUIView
            where TViewModel : class, IViewModel
        {
            _creators[typeof(TView)] = ct => CreateViewAsync<TView, TViewModel>(ct);
        }

        public void Register<TView, TViewModel, TArgs>()
            where TView : IUIView
            where TViewModel : class, IViewModel<TArgs>
            where TArgs : IViewArgs
        {
            _argsCreators[typeof(TView)] = (args, ct) =>
                CreateViewWithArgsAsync<TView, TViewModel, TArgs>((TArgs)args, ct);
        }

        public async UniTask ShowAsync<T>(CancellationToken ct = default) where T : IUIView
        {
            if (_isTransitioning && !_stateTransitionActive)
            {
                Debug.LogWarning($"[UINavigator] Transitioning — ShowAsync<{typeof(T).Name}> ignored.");
                return;
            }
            if (!_creators.TryGetValue(typeof(T), out var creator))
                throw new InvalidOperationException(
                    $"[UINavigator] No creator for {typeof(T).Name}. Call Register<{typeof(T).Name}, TViewModel>() in installer.");

            // Don't claim ownership of _isTransitioning if ChangeStateAsync already holds it.
            bool ownsFlag = !_isTransitioning;
            if (ownsFlag) _isTransitioning = true;
            try
            {
                var view = await creator(ct);
                // Block layers below this view BEFORE the push animation — prevents click-through
                // on lower layers during the entrance transition.
                RefreshLayerBlocking(view);
                await _stack.PushAsync(view, ct);

                // PushAsync can decline silently (max navigation depth) or throw. Either way the
                // `pending` refresh above is now describing a view that is not on the stack, so
                // re-derive from the real top. Without this, blocking (and the backdrop, which is
                // a full-screen raycast target) stays applied for a view nobody can see or close.
                if (!ReferenceEquals(_stack.Peek(), view)) RefreshLayerBlocking();
            }
            catch
            {
                TryRefreshLayerBlocking();
                throw;
            }
            finally { if (ownsFlag) _isTransitioning = false; }
        }

        public async UniTask ShowAsync<T, TArgs>(TArgs args, CancellationToken ct = default)
            where T : IUIView
            where TArgs : IViewArgs
        {
            if (_isTransitioning && !_stateTransitionActive)
            {
                Debug.LogWarning($"[UINavigator] Transitioning — ShowAsync<{typeof(T).Name}> with args ignored.");
                return;
            }
            if (!_argsCreators.TryGetValue(typeof(T), out var creator))
                throw new InvalidOperationException(
                    $"[UINavigator] No args-creator for {typeof(T).Name}. Call Register<{typeof(T).Name}, TViewModel, {typeof(TArgs).Name}>() in installer.");

            bool ownsFlag = !_isTransitioning;
            if (ownsFlag) _isTransitioning = true;
            try
            {
                var view = await creator(args, ct);
                // Block layers below this view BEFORE the push animation.
                RefreshLayerBlocking(view);
                await _stack.PushAsync(view, ct);

                // See the no-args overload: a declined or failed push must not leave blocking and
                // the backdrop pinned to a view that never made it onto the stack.
                if (!ReferenceEquals(_stack.Peek(), view)) RefreshLayerBlocking();
            }
            catch
            {
                TryRefreshLayerBlocking();
                throw;
            }
            finally { if (ownsFlag) _isTransitioning = false; }
        }

        // Only hides if the top-of-stack view is T. Middle-of-stack removal is not supported.
        public async UniTask HideAsync<T>(CancellationToken ct = default) where T : IUIView
        {
            if (_isTransitioning) return;
            var top = _stack.Peek();
            if (top is not T)
            {
                Debug.LogWarning(
                    $"[UINavigator] HideAsync<{typeof(T).Name}>: top is '{top?.ViewId ?? "null"}'. Only top-of-stack hide is supported.");
                return;
            }
            _isTransitioning = true;
            try { await _stack.PopAsync(ct); }
            finally
            {
                // Re-evaluate after pop — view is removed from stack at this point.
                RefreshLayerBlocking();
                _isTransitioning = false;
            }
        }

        public async UniTask PopAsync(CancellationToken ct = default)
        {
            if (_isTransitioning)
            {
                Debug.LogWarning("[UINavigator] Transitioning — PopAsync ignored.");
                return;
            }
            _isTransitioning = true;
            try { await _stack.PopAsync(ct); }
            finally
            {
                RefreshLayerBlocking();
                _isTransitioning = false;
            }
        }

        public async UniTask CloseAllAsync(CancellationToken ct = default)
        {
            if (_isTransitioning)
            {
                Debug.LogWarning("[UINavigator] Transitioning — CloseAllAsync ignored.");
                return;
            }
            _isTransitioning = true;
            try { await _stack.ClearAsync(ct); }
            finally
            {
                RefreshLayerBlocking();
                _isTransitioning = false;
            }
        }

        // Always clears the stack before entering the new state.
        // For overlay states (Pause, GameOver) use ShowAsync<T>() instead.
        // Internal: GameLifecycleManager is the only sanctioned caller. Game code uses
        // GameLifecycleManager.ChangeStateAsync<T> / RestartCurrentStateAsync so the transition
        // overlay and the lifecycle re-entrancy guard are always applied.
        internal async UniTask ChangeStateAsync<TState>(CancellationToken ct = default) where TState : IViewState
        {
            if (_isTransitioning)
            {
                Debug.LogWarning($"[UINavigator] Transitioning — ChangeStateAsync<{typeof(TState).Name}> ignored.");
                return;
            }
            _isTransitioning = true;
            try
            {
                await _stack.ClearAsync(ct);
                RefreshLayerBlocking();
                // NOTE: ResetState() is deliberately NOT called here. It nulled _currentState, which
                // made UIStateMachine skip previous.OnExitAsync — silently dropping non-view cleanup
                // (timeScale restore, subscription disposal, spawned-object teardown). Some states
                // (e.g. AircraftGameplayState) rely on OnExitAsync running for exactly this reason.
                // Same-state re-entry is GameLifecycleManager.RestartCurrentStateAsync's job, not this
                // method's — ResetState() remains available as a manual escape hatch via IUINavigator.
                // _stateTransitionActive lets state OnEnterAsync call ShowAsync while _isTransitioning
                // remains true — so IsTransitioning stays accurate for the full operation duration.
                _stateTransitionActive = true;
                await _stateMachine.ChangeStateAsync<TState>(ct);
            }
            finally
            {
                _stateTransitionActive = false;
                _isTransitioning = false;
            }
        }

        public void ResetState() => _stateMachine.ResetState();

        // The top-of-stack view is the sole authority for layer blocking.
        // BlockLayersBelow enables that view's layer and every layer above it; everything below
        // is disabled so lower views cannot receive input. Layers above the top view's UILayer
        // remain interactable — overlays and debug layers stay live regardless of what is on top.
        // pending is passed from ShowAsync (before PushAsync) so blocking applies during entrance.
        private void RefreshLayerBlocking(IUIView pending = null)
        {
            IUIView top = pending ?? _stack.Peek();

            if (_layers != null)
            {
                if (top is UIViewBase vb)
                    _layers.BlockLayersBelow(vb.Layer);
                else
                    _layers.SetAllLayersInteractable(true);
            }

            // Backdrop follows the same authority as layer blocking: whatever is on top decides.
            // `pending != null` tells it this view is mid-show — the one case where a still-inactive
            // GameObject legitimately gets a backdrop.
            _backdrop?.Refresh(top, isPending: pending != null);
        }

        // RefreshLayerBlocking on a recovery path must never replace the exception being handled
        // (including an OperationCanceledException) with one of its own — e.g. a
        // MissingReferenceException from a layer transform destroyed by a scene unload.
        private void TryRefreshLayerBlocking()
        {
            try { RefreshLayerBlocking(); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        private async UniTask<IUIView> CreateViewAsync<TView, TViewModel>(CancellationToken ct)
            where TView : IUIView
            where TViewModel : class, IViewModel
        {
            return await _factory.CreateAsync<TView, TViewModel>(ct);
        }

        private async UniTask<IUIView> CreateViewWithArgsAsync<TView, TViewModel, TArgs>(TArgs args, CancellationToken ct)
            where TView : IUIView
            where TViewModel : class, IViewModel<TArgs>
            where TArgs : IViewArgs
        {
            return await _factory.CreateAsync<TView, TViewModel, TArgs>(args, ct);
        }
    }
}
