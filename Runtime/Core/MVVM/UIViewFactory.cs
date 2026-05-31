using System;
using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Sinkii09.UIFramework
{
    // Creates view + ViewModel pairs. Each view gets an isolated VContainer child scope.
    // All registered TViews must extend UIViewBase — this is enforced at runtime via InvalidCastException.
    // Scope disposal on view destroy is deferred to Phase 06 (UIViewFactory.ReturnAsync).
    public class UIViewFactory : IUIViewFactory
    {
        private readonly IUILoader _loader;
        private readonly IObjectResolver _container;

        [Inject]
        public UIViewFactory(IUILoader loader, IObjectResolver container)
        {
            _loader = loader;
            _container = container;
        }

        public async UniTask<TView> CreateAsync<TView, TViewModel>(CancellationToken ct = default)
            where TView : IUIView
            where TViewModel : class, IViewModel
        {
            var view = await InstantiateViewAsync<TView>(ct);
            IObjectResolver scope = null;
            try
            {
                scope = _container.CreateScope(builder =>
                {
                    builder.RegisterInstance(view);
                    builder.Register<TViewModel>(Lifetime.Scoped);
                });
                var viewModel = scope.Resolve<TViewModel>();
                await CastAndInitialize<TView, TViewModel>(view, viewModel, scope, ct);
                return view;
            }
            catch
            {
                // W1: dispose scope to release ViewModel and any R3 subscriptions it holds.
                scope?.Dispose();
                if (view is UIViewBase viewBase) UnityEngine.Object.Destroy(viewBase.gameObject);
                throw;
            }
        }

        public async UniTask<TView> CreateAsync<TView, TViewModel, TArgs>(TArgs args, CancellationToken ct = default)
            where TView : IUIView
            where TViewModel : class, IViewModel<TArgs>
            where TArgs : IViewArgs
        {
            var view = await InstantiateViewAsync<TView>(ct);
            IObjectResolver scope = null;
            try
            {
                scope = _container.CreateScope(builder =>
                {
                    builder.RegisterInstance(view);
                    builder.Register<TViewModel>(Lifetime.Scoped);
                });
                var viewModel = scope.Resolve<TViewModel>();
                // Initialize with args BEFORE BindViewModel so bindings fire against initialized state.
                viewModel.Initialize(args);
                await CastAndInitialize<TView, TViewModel>(view, viewModel, scope, ct);
                return view;
            }
            catch
            {
                scope?.Dispose();
                if (view is UIViewBase viewBase) UnityEngine.Object.Destroy(viewBase.gameObject);
                throw;
            }
        }

        // Loads prefab via UIViewBase (satisfies IUILoader T : Component constraint),
        // instantiates it, injects all MonoBehaviours in the hierarchy, and returns TView.
        private async UniTask<TView> InstantiateViewAsync<TView>(CancellationToken ct) where TView : IUIView
        {
            string key = typeof(TView).Name;
            var prefab = await _loader.LoadAsync<UIViewBase>(key, ct);
            // W2: guard before allocating a scene object — avoids dangling GameObjects on cancellation.
            ct.ThrowIfCancellationRequested();
            var instance = UnityEngine.Object.Instantiate(prefab);

            // Cast before injection — avoids running [Inject] methods on a view that will immediately be destroyed.
            if (instance is not TView view)
            {
                UnityEngine.Object.Destroy(instance.gameObject);
                throw new InvalidCastException(
                    $"[UIViewFactory] Prefab '{key}' root component is {instance.GetType().Name}, " +
                    $"not {typeof(TView).Name}. Ensure the prefab's root MonoBehaviour extends {typeof(TView).Name}.");
            }

            // InjectGameObject wires all [Inject]-marked methods/fields on every MonoBehaviour in hierarchy.
            _container.InjectGameObject(instance.gameObject);
            return view;
        }

        // InitializeAsync is on UIView<TViewModel>, not IUIView — requires cast.
        // Throws if TView does not extend UIView<TViewModel> (programmer error).
        private static async UniTask CastAndInitialize<TView, TViewModel>(
            TView view, TViewModel viewModel, IObjectResolver scope, CancellationToken ct)
            where TView : IUIView
            where TViewModel : class, IViewModel
        {
            if (view is not UIView<TViewModel> typedView)
                throw new InvalidCastException(
                    $"[UIViewFactory] {typeof(TView).Name} does not extend UIView<{typeof(TViewModel).Name}>. " +
                    $"All views must extend UIView<TViewModel> to be initialized by UIViewFactory.");

            await typedView.InitializeAsync(viewModel, scope, ct);
        }
    }
}
