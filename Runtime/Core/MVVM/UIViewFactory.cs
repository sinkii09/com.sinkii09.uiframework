using System;
using Cysharp.Threading.Tasks;
using System.Threading;
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
        private readonly UIRootLayerRefs _layers;

        [Inject]
        public UIViewFactory(IUILoader loader, IObjectResolver container, UIRootLayerRefs layers)
        {
            _loader = loader;
            _container = container;
            _layers = layers;
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
                // W2: inject from child scope so [Inject] members resolve scoped dependencies correctly.
                if (view is not UIViewBase viewBase)
                    throw new InvalidOperationException($"[UIViewFactory] {typeof(TView).Name} must extend UIViewBase.");
                ReparentToLayer(viewBase);
                scope.InjectGameObject(viewBase.gameObject);
                var viewModel = scope.Resolve<TViewModel>();
                await CastAndInitialize<TView, TViewModel>(view, viewModel, scope, ct);
                return view;
            }
            catch
            {
                scope?.Dispose();
                if (view is UIViewBase vb) UnityEngine.Object.Destroy(vb.gameObject);
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
                // W2: inject from child scope so [Inject] members resolve scoped dependencies correctly.
                if (view is not UIViewBase viewBase)
                    throw new InvalidOperationException($"[UIViewFactory] {typeof(TView).Name} must extend UIViewBase.");
                ReparentToLayer(viewBase);
                scope.InjectGameObject(viewBase.gameObject);
                var viewModel = scope.Resolve<TViewModel>();
                // Initialize with args BEFORE BindViewModel so bindings fire against initialized state.
                viewModel.Initialize(args);
                await CastAndInitialize<TView, TViewModel>(view, viewModel, scope, ct);
                return view;
            }
            catch
            {
                scope?.Dispose();
                if (view is UIViewBase vb) UnityEngine.Object.Destroy(vb.gameObject);
                throw;
            }
        }

        public async UniTask<IUIView> CreateAsync(Type viewType, Type vmType, string key, CancellationToken ct = default)
        {
            var prefab = await _loader.LoadAsync<UIViewBase>(key, ct);
            ct.ThrowIfCancellationRequested();
            var instance = UnityEngine.Object.Instantiate(prefab);

            if (!viewType.IsInstanceOfType(instance))
            {
                UnityEngine.Object.Destroy(instance.gameObject);
                throw new InvalidCastException(
                    $"[UIViewFactory] Prefab '{key}' root is {instance.GetType().Name}, not {viewType.Name}.");
            }

            IObjectResolver scope = null;
            try
            {
                ReparentToLayer(instance);
                scope = _container.CreateScope(builder =>
                {
                    builder.RegisterInstance(instance);
                    builder.Register(vmType, Lifetime.Scoped);
                });
                // W2: inject from child scope so [Inject] members resolve scoped dependencies correctly.
                scope.InjectGameObject(instance.gameObject);
                var viewModel = (IViewModel)scope.Resolve(vmType);
                await instance.InitializeNonGenericAsync(viewModel, scope, ct);
                return instance;
            }
            catch
            {
                scope?.Dispose();
                UnityEngine.Object.Destroy(instance.gameObject);
                throw;
            }
        }

        // Loads and instantiates the prefab without injection — callers inject via their child scope.
        private async UniTask<TView> InstantiateViewAsync<TView>(CancellationToken ct) where TView : IUIView
        {
            string key = typeof(TView).Name;
            var prefab = await _loader.LoadAsync<UIViewBase>(key, ct);
            ct.ThrowIfCancellationRequested();
            var instance = UnityEngine.Object.Instantiate(prefab);

            if (instance is not TView view)
            {
                UnityEngine.Object.Destroy(instance.gameObject);
                throw new InvalidCastException(
                    $"[UIViewFactory] Prefab '{key}' root component is {instance.GetType().Name}, " +
                    $"not {typeof(TView).Name}. Ensure the prefab's root MonoBehaviour extends {typeof(TView).Name}.");
            }

            return view;
        }

        private void ReparentToLayer(UIViewBase view)
        {
            if (_layers == null) return;
            var layer = _layers.GetLayer(view.Layer);
            if (layer != null)
                view.transform.SetParent(layer, false);
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
