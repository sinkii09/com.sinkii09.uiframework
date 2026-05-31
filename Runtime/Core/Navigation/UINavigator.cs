using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;

namespace Sinkii09.UIFramework
{
    // Facade over NavigationStack + UIStateMachine.
    // Call Register<TView, TViewModel>() during VContainer installer setup to map view types.
    // All navigation ops are main-thread only — _isTransitioning bool is not async-safe across threads.
    public class UINavigator : IUINavigator
    {
        private readonly INavigationStack _stack;
        private readonly IUIStateMachine _stateMachine;
        private readonly IUIViewFactory _factory;

        // Delegate closures capture TView + TViewModel type params at Register() call-site.
        // This bridges ShowAsync<TView>() (one type param) to factory (needs two type params).
        private readonly Dictionary<Type, Func<CancellationToken, UniTask<IUIView>>> _creators = new();
        private readonly Dictionary<Type, Func<object, CancellationToken, UniTask<IUIView>>> _argsCreators = new();

        private bool _isTransitioning;

        public IUIView Current => _stack.Peek();
        public bool IsTransitioning => _isTransitioning;

        [Inject]
        public UINavigator(INavigationStack stack, IUIStateMachine stateMachine, IUIViewFactory factory)
        {
            _stack = stack;
            _stateMachine = stateMachine;
            _factory = factory;
        }

        // Call once per view type in the VContainer installer (UIFrameworkInstaller or similar).
        // Enforces single-instance-per-type; multi-instance UI is managed inside its parent view.
        public void Register<TView, TViewModel>()
            where TView : IUIView
            where TViewModel : IViewModel
        {
            _creators[typeof(TView)] = ct => CreateViewAsync<TView, TViewModel>(ct);
        }

        public void Register<TView, TViewModel, TArgs>()
            where TView : IUIView
            where TViewModel : IViewModel<TArgs>
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
                await _stack.PushAsync(view, view.Transition, ct);
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
                await _stack.PushAsync(view, view.Transition, ct);
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
            finally { _isTransitioning = false; }
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
            finally { _isTransitioning = false; }
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
            finally { _isTransitioning = false; }
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
            try
            {
                await _stack.ClearAsync(ct);
                await _stateMachine.ChangeStateAsync<TState>(ct);
            }
            finally { _isTransitioning = false; }
        }

        private async UniTask<IUIView> CreateViewAsync<TView, TViewModel>(CancellationToken ct)
            where TView : IUIView
            where TViewModel : IViewModel
        {
            return await _factory.CreateAsync<TView, TViewModel>(ct);
        }

        private async UniTask<IUIView> CreateViewWithArgsAsync<TView, TViewModel, TArgs>(TArgs args, CancellationToken ct)
            where TView : IUIView
            where TViewModel : IViewModel<TArgs>
            where TArgs : IViewArgs
        {
            return await _factory.CreateAsync<TView, TViewModel, TArgs>(args, ct);
        }
    }
}
