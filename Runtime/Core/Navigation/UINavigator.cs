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

        private readonly Dictionary<Type, Func<CancellationToken, UniTask<IUIView>>> _creators = new();
        private readonly Dictionary<Type, Func<object, CancellationToken, UniTask<IUIView>>> _argsCreators = new();

        private bool _isTransitioning;

        public IUIView Current => _stack.Peek();
        public bool IsTransitioning => _isTransitioning;

        [Inject]
        public UINavigator(INavigationStack stack, IUIStateMachine stateMachine,
            IUIViewFactory factory, IReadOnlyList<UIViewRegistration> registrations,
            UIRootLayerRefs layers)
        {
            _stack = stack;
            _stateMachine = stateMachine;
            _factory = factory;
            _layers = layers;
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
            if (_isTransitioning)
            {
                Debug.LogWarning($"[UINavigator] Transitioning — ShowAsync<{typeof(T).Name}> ignored.");
                return;
            }
            if (!_creators.TryGetValue(typeof(T), out var creator))
                throw new InvalidOperationException(
                    $"[UINavigator] No creator for {typeof(T).Name}. Call Register<{typeof(T).Name}, TViewModel>() in installer.");

            _isTransitioning = true;
            try
            {
                var view = await creator(ct);
                // Block layers below this view BEFORE the push animation — prevents click-through
                // on lower layers during the entrance transition.
                RefreshLayerBlocking(view);
                await _stack.PushAsync(view, ct);
            }
            finally { _isTransitioning = false; }
        }

        public async UniTask ShowAsync<T, TArgs>(TArgs args, CancellationToken ct = default)
            where T : IUIView
            where TArgs : IViewArgs
        {
            if (_isTransitioning)
            {
                Debug.LogWarning($"[UINavigator] Transitioning — ShowAsync<{typeof(T).Name}> with args ignored.");
                return;
            }
            if (!_argsCreators.TryGetValue(typeof(T), out var creator))
                throw new InvalidOperationException(
                    $"[UINavigator] No args-creator for {typeof(T).Name}. Call Register<{typeof(T).Name}, TViewModel, {typeof(TArgs).Name}>() in installer.");

            _isTransitioning = true;
            try
            {
                var view = await creator(args, ct);
                // Block layers below this view BEFORE the push animation.
                RefreshLayerBlocking(view);
                await _stack.PushAsync(view, ct);
            }
            finally { _isTransitioning = false; }
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
        public async UniTask ChangeStateAsync<TState>(CancellationToken ct = default) where TState : IViewState
        {
            if (_isTransitioning)
            {
                Debug.LogWarning($"[UINavigator] Transitioning — ChangeStateAsync<{typeof(TState).Name}> ignored.");
                return;
            }
            _isTransitioning = true;
            try   { await _stack.ClearAsync(ct); }
            finally { _isTransitioning = false; }

            // Enable all layers after clear — state's OnEnterAsync will call ShowAsync which
            // re-applies correct blocking. _isTransitioning must be false before this call
            // so the state's ShowAsync calls are not silently dropped.
            RefreshLayerBlocking();
            await _stateMachine.ChangeStateAsync<TState>(ct);
        }

        // The top-of-stack view is the sole authority for layer blocking.
        // BlockLayersBelow enables that view's layer and every layer above it; everything below
        // is disabled so lower views cannot receive input. Layers above the top view's UILayer
        // remain interactable — overlays and debug layers stay live regardless of what is on top.
        // pending is passed from ShowAsync (before PushAsync) so blocking applies during entrance.
        private void RefreshLayerBlocking(IUIView pending = null)
        {
            if (_layers == null) return;

            IUIView top = pending ?? _stack.Peek();

            if (top is UIViewBase vb)
                _layers.BlockLayersBelow(vb.Layer);
            else
                _layers.SetAllLayersInteractable(true);
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
