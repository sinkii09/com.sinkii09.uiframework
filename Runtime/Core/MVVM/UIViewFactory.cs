using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using VContainer;
using VContainer.Unity;

namespace Sinkii09.UIFramework
{
    // Creates view + ViewModel pairs. Each view gets an isolated VContainer child scope.
    // Views are cached by type after first creation — subsequent ShowAsync calls reuse the
    // existing GameObject (FactoryReset() resets scope and bindings) instead of re-instantiating.
    // All registered TViews must extend UIViewBase — enforced at runtime via InvalidCastException.
    public class UIViewFactory : IUIViewFactory, IDisposable
    {
        private readonly IUILoader _loader;
        private readonly IObjectResolver _container;
        private readonly UIRootLayerRefs _layers;

        // One cached instance per view type. Values are UIViewBase MonoBehaviours;
        // Unity overrides == so a null check detects destroyed objects after scene unload.
        private readonly Dictionary<Type, UIViewBase> _cache = new();

        [Inject]
        public UIViewFactory(IUILoader loader, IObjectResolver container, UIRootLayerRefs layers)
        {
            _loader = loader;
            _container = container;
            _layers = layers;
        }

        public UniTask<TView> CreateAsync<TView, TViewModel>(CancellationToken ct = default)
            where TView : IUIView
            where TViewModel : class, IViewModel
            => CreateGenericAsync<TView, TViewModel>(ct);

        public UniTask<TView> CreateAsync<TView, TViewModel, TArgs>(TArgs args, CancellationToken ct = default)
            where TView : IUIView
            where TViewModel : class, IViewModel<TArgs>
            where TArgs : IViewArgs
            => CreateGenericAsync<TView, TViewModel>(ct, vm => vm.Initialize(args));

        public async UniTask<IUIView> CreateAsync(Type viewType, Type vmType, string key, CancellationToken ct = default)
        {
            bool isNew;
            UIViewBase instance;

            if (_cache.TryGetValue(viewType, out instance) && instance != null)
            {
                isNew = false;
                instance.FactoryReset();
            }
            else
            {
                isNew = true;
                var prefab = await _loader.LoadAsync<UIViewBase>(key, ct);
                ct.ThrowIfCancellationRequested();
                instance = UnityEngine.Object.Instantiate(prefab);

                if (!viewType.IsInstanceOfType(instance))
                {
                    UnityEngine.Object.Destroy(instance.gameObject);
                    throw new InvalidCastException(
                        $"[UIViewFactory] Prefab '{key}' root is {instance.GetType().Name}, not {viewType.Name}.");
                }
            }

            IObjectResolver scope = null;
            try
            {
                if (isNew) ReparentToLayer(instance);
                scope = _container.CreateScope(builder =>
                {
                    builder.RegisterInstance(instance);
                    builder.Register(vmType, Lifetime.Scoped);
                });
                // W2: inject from child scope so [Inject] members resolve scoped dependencies correctly.
                scope.InjectGameObject(instance.gameObject);
                var viewModel = (IViewModel)scope.Resolve(vmType);
                await instance.InitializeNonGenericAsync(viewModel, scope, ct);
                _cache[viewType] = instance;
                return instance;
            }
            catch
            {
                scope?.Dispose();
                if (isNew)
                {
                    _cache.Remove(viewType);
                    UnityEngine.Object.Destroy(instance.gameObject);
                }
                throw;
            }
        }

        // VContainer calls Dispose() when the owning LifetimeScope is destroyed.
        // FactoryReset() disposes each view's child VContainer scope (releasing the ViewModel)
        // before the GO is destroyed so R3 subscriptions in _disposables are cleaned up.
        public void Dispose()
        {
            foreach (var view in _cache.Values)
            {
                if (view != null)
                {
                    view.FactoryReset();
                    UnityEngine.Object.Destroy(view.gameObject);
                }
            }
            _cache.Clear();
        }

        // Shared creation path for both generic overloads. afterResolve is invoked between
        // Resolve and InitializeAsync — used by the TArgs overload to call viewModel.Initialize(args).
        private async UniTask<TView> CreateGenericAsync<TView, TViewModel>(
            CancellationToken ct,
            Action<TViewModel> afterResolve = null)
            where TView : IUIView
            where TViewModel : class, IViewModel
        {
            // Cache lookup before try — no resources allocated yet, nothing to clean up on miss.
            bool isNew = !(_cache.TryGetValue(typeof(TView), out var viewBase) && viewBase != null);
            TView view = isNew ? default : (TView)(IUIView)viewBase;
            if (!isNew) viewBase.FactoryReset();

            IObjectResolver scope = null;
            try
            {
                if (isNew)
                {
                    // InstantiateViewAsync is inside try so viewBase is assigned before the
                    // catch block — avoids a NullReferenceException masking the real error.
                    view = await InstantiateViewAsync<TView>(ct);
                    if (view is not UIViewBase castBase)
                        throw new InvalidOperationException(
                            $"[UIViewFactory] {typeof(TView).Name} must extend UIViewBase.");
                    viewBase = castBase;
                }

                scope = _container.CreateScope(builder =>
                {
                    builder.RegisterInstance(view);
                    builder.Register<TViewModel>(Lifetime.Scoped);
                });
                // W2: inject from child scope so [Inject] members resolve scoped dependencies correctly.
                // Skip ReparentToLayer on cache-hit — view is already under the correct layer transform.
                if (isNew) ReparentToLayer(viewBase);
                scope.InjectGameObject(viewBase.gameObject);
                var viewModel = scope.Resolve<TViewModel>();
                afterResolve?.Invoke(viewModel);
                await CastAndInitialize<TView, TViewModel>(view, viewModel, scope, ct);
                _cache[typeof(TView)] = viewBase;
                return view;
            }
            catch
            {
                scope?.Dispose();
                // On cache-miss failure: destroy the newly created GO (if instantiation succeeded)
                // and clear the cache entry.
                // On cache-hit failure: leave the GO in its FactoryReset() state so the next
                // ShowAsync attempt can retry with a fresh scope.
                if (isNew && viewBase != null)
                {
                    _cache.Remove(typeof(TView));
                    UnityEngine.Object.Destroy(viewBase.gameObject);
                }
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
