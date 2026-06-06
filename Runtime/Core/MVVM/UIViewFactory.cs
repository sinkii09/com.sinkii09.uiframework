using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using System.Threading;
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

        public async UniTask<TView> CreateAsync<TView, TViewModel>(CancellationToken ct = default)
            where TView : IUIView
            where TViewModel : class, IViewModel
        {
            bool isNew;
            UIViewBase viewBase;
            TView view;

            if (_cache.TryGetValue(typeof(TView), out viewBase) && viewBase != null)
            {
                // Reuse cached instance: reset scope + bindings so InitializeAsync runs fresh.
                isNew = false;
                viewBase.FactoryReset();
                view = (TView)(IUIView)viewBase;
            }
            else
            {
                isNew = true;
                view = await InstantiateViewAsync<TView>(ct);
                if (view is not UIViewBase castBase)
                    throw new InvalidOperationException(
                        $"[UIViewFactory] {typeof(TView).Name} must extend UIViewBase.");
                viewBase = castBase;
            }

            IObjectResolver scope = null;
            try
            {
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
                await CastAndInitialize<TView, TViewModel>(view, viewModel, scope, ct);
                _cache[typeof(TView)] = viewBase;
                return view;
            }
            catch
            {
                scope?.Dispose();
                // On cache-miss failure: destroy the newly created GO and clear the cache entry.
                // On cache-hit failure: leave the GO in its FactoryReset() state so the next
                // ShowAsync attempt can retry with a fresh scope.
                if (isNew)
                {
                    _cache.Remove(typeof(TView));
                    UnityEngine.Object.Destroy(viewBase.gameObject);
                }
                throw;
            }
        }

        public async UniTask<TView> CreateAsync<TView, TViewModel, TArgs>(TArgs args, CancellationToken ct = default)
            where TView : IUIView
            where TViewModel : class, IViewModel<TArgs>
            where TArgs : IViewArgs
        {
            bool isNew;
            UIViewBase viewBase;
            TView view;

            if (_cache.TryGetValue(typeof(TView), out viewBase) && viewBase != null)
            {
                isNew = false;
                viewBase.FactoryReset();
                view = (TView)(IUIView)viewBase;
            }
            else
            {
                isNew = true;
                view = await InstantiateViewAsync<TView>(ct);
                if (view is not UIViewBase castBase)
                    throw new InvalidOperationException(
                        $"[UIViewFactory] {typeof(TView).Name} must extend UIViewBase.");
                viewBase = castBase;
            }

            IObjectResolver scope = null;
            try
            {
                scope = _container.CreateScope(builder =>
                {
                    builder.RegisterInstance(view);
                    builder.Register<TViewModel>(Lifetime.Scoped);
                });
                // W2: inject from child scope so [Inject] members resolve scoped dependencies correctly.
                if (isNew) ReparentToLayer(viewBase);
                scope.InjectGameObject(viewBase.gameObject);
                var viewModel = scope.Resolve<TViewModel>();
                // Initialize with args BEFORE BindViewModel so bindings fire against initialized state.
                viewModel.Initialize(args);
                await CastAndInitialize<TView, TViewModel>(view, viewModel, scope, ct);
                _cache[typeof(TView)] = viewBase;
                return view;
            }
            catch
            {
                scope?.Dispose();
                if (isNew)
                {
                    _cache.Remove(typeof(TView));
                    UnityEngine.Object.Destroy(viewBase.gameObject);
                }
                throw;
            }
        }

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
        // Destroy cached GOs so they don't linger in the scene hierarchy after scope teardown.
        public void Dispose()
        {
            foreach (var view in _cache.Values)
            {
                if (view != null)
                    UnityEngine.Object.Destroy(view.gameObject);
            }
            _cache.Clear();
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
