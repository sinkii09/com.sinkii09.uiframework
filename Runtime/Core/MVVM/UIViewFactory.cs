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
        private volatile IObjectResolver _scopeContainer;

        // One cached instance per view type. Values are UIViewBase MonoBehaviours;
        // Unity overrides == so a null check detects destroyed objects after scene unload.
        private readonly Dictionary<Type, UIViewBase> _cache = new();

        // Load key used for each cached view type — needed to release the loader's handle
        // (e.g. Addressables ref-count) on Dispose(). Only set once a load actually succeeded.
        private readonly Dictionary<Type, string> _cacheKeys = new();

        // In-flight creation tasks keyed by view type. If two async callers request the same view
        // type concurrently (main-thread interleaving — not threading), the second awaits the first
        // result instead of instantiating a duplicate GameObject.
        private readonly Dictionary<Type, UniTaskCompletionSource<IUIView>> _pending = new();

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
                // LoadAsync is intentionally outside the try/catch below — per the IUILoader contract,
                // the loader itself releases any partially-acquired handle if this throws/cancels, so
                // no compensating UnloadAsync call is needed here for a load failure.
                var prefab = await _loader.LoadAsync<UIViewBase>(key, ct);
                ct.ThrowIfCancellationRequested();
                instance = UnityEngine.Object.Instantiate(prefab);

                if (!viewType.IsInstanceOfType(instance))
                {
                    UnityEngine.Object.Destroy(instance.gameObject);
                    await _loader.UnloadAsync(key, CancellationToken.None);
                    throw new InvalidCastException(
                        $"[UIViewFactory] Prefab '{key}' root is {instance.GetType().Name}, not {viewType.Name}.");
                }
            }

            IObjectResolver scope = null;
            try
            {
                if (isNew) ReparentToLayer(instance);
                scope = (_scopeContainer ?? _container).CreateScope(builder =>
                {
                    builder.RegisterInstance(instance);
                    builder.Register(vmType, Lifetime.Scoped);
                });
                // W2: inject from child scope so [Inject] members resolve scoped dependencies correctly.
                scope.InjectGameObject(instance.gameObject);
                var viewModel = (IViewModel)scope.Resolve(vmType);
                await instance.InitializeNonGenericAsync(viewModel, scope, ct);
                _cache[viewType] = instance;
                if (isNew) _cacheKeys[viewType] = key;
                return instance;
            }
            catch
            {
                scope?.Dispose();
                if (isNew)
                {
                    _cache.Remove(viewType);
                    UnityEngine.Object.Destroy(instance.gameObject);
                    await _loader.UnloadAsync(key, CancellationToken.None);
                }
                throw;
            }
        }

        public void SetScopeContainer(IObjectResolver resolver)
        {
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            _scopeContainer = resolver;
        }

        public void ResetScopeContainer(IObjectResolver expected = null)
        {
            if (expected == null || _scopeContainer == expected)
                _scopeContainer = null;
        }

        // VContainer calls Dispose() when the owning LifetimeScope is destroyed.
        // FactoryReset() disposes each view's child VContainer scope (releasing the ViewModel)
        // before the GO is destroyed so R3 subscriptions in _disposables are cleaned up.
        public void Dispose()
        {
            foreach (var (type, view) in _cache)
            {
                if (view != null)
                {
                    view.FactoryReset();
                    UnityEngine.Object.Destroy(view.gameObject);

                    if (_cacheKeys.TryGetValue(type, out var key))
                    {
                        // GetAwaiter().GetResult() is safe today: both ResourcesUILoader and
                        // AddressablesUILoader complete UnloadAsync synchronously (no real await
                        // inside), so this never actually blocks. If a future IUILoader implementation
                        // adds genuine async I/O (e.g. a network-backed loader), this would risk a
                        // main-thread deadlock — switch to fire-and-forget with explicit error logging
                        // if that ever happens.
                        _loader.UnloadAsync(key, CancellationToken.None).GetAwaiter().GetResult();
                    }
                }
            }
            _cache.Clear();
            _cacheKeys.Clear();
        }

        // Shared creation path for both generic overloads. afterResolve is invoked between
        // Resolve and InitializeAsync — used by the TArgs overload to call viewModel.Initialize(args).
        private async UniTask<TView> CreateGenericAsync<TView, TViewModel>(
            CancellationToken ct,
            Action<TViewModel> afterResolve = null)
            where TView : IUIView
            where TViewModel : class, IViewModel
        {
            // Bail early if already cancelled — prevents Initialize(args) from running on cached views
            // when the caller's session token was cancelled before ShowAsync was even dispatched.
            ct.ThrowIfCancellationRequested();

            var viewType = typeof(TView);

            // Deduplication: if a creation for this type is already in-flight (async interleave on
            // main thread), await the existing task instead of instantiating a duplicate GO.
            if (_pending.TryGetValue(viewType, out var inFlight))
                return (TView)await inFlight.Task;

            var tcs = new UniTaskCompletionSource<IUIView>();
            _pending[viewType] = tcs;

            // Cache lookup before try — no resources allocated yet, nothing to clean up on miss.
            bool isNew = !(_cache.TryGetValue(viewType, out var viewBase) && viewBase != null);
            TView view = isNew ? default : (TView)(IUIView)viewBase;
            if (!isNew) viewBase.FactoryReset();
            string key = isNew ? GetKey(viewType) : null;

            IObjectResolver scope = null;
            try
            {
                if (isNew)
                {
                    // InstantiateViewAsync is inside try so viewBase is assigned before the
                    // catch block — avoids a NullReferenceException masking the real error.
                    // Its internal LoadAsync call relies on the IUILoader contract (see IUILoader.cs)
                    // to self-release on failure; if it throws before returning, viewBase stays null
                    // below and the catch blocks' unload call is correctly skipped.
                    view = await InstantiateViewAsync<TView>(key, ct);
                    if (view is not UIViewBase castBase)
                        throw new InvalidOperationException(
                            $"[UIViewFactory] {viewType.Name} must extend UIViewBase.");
                    viewBase = castBase;
                }

                scope = (_scopeContainer ?? _container).CreateScope(builder =>
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
                _cache[viewType] = viewBase;
                if (isNew) _cacheKeys[viewType] = key;
                tcs.TrySetResult(view);
                return view;
            }
            catch (OperationCanceledException)
            {
                tcs.TrySetCanceled();
                scope?.Dispose();
                if (isNew && viewBase != null)
                {
                    _cache.Remove(viewType);
                    UnityEngine.Object.Destroy(viewBase.gameObject);
                    await _loader.UnloadAsync(key, CancellationToken.None);
                }
                throw;
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
                scope?.Dispose();
                // On cache-miss failure: destroy the newly created GO (if instantiation succeeded)
                // and clear the cache entry.
                // On cache-hit failure: leave the GO in its FactoryReset() state so the next
                // ShowAsync attempt can retry with a fresh scope.
                if (isNew && viewBase != null)
                {
                    _cache.Remove(viewType);
                    UnityEngine.Object.Destroy(viewBase.gameObject);
                    await _loader.UnloadAsync(key, CancellationToken.None);
                }
                throw;
            }
            finally
            {
                _pending.Remove(viewType);
            }
        }

        // Loads and instantiates the prefab without injection — callers inject via their child scope.
        // LoadAsync is intentionally not wrapped in its own try/catch — per the IUILoader contract,
        // the loader releases any partially-acquired handle itself if this throws/cancels.
        private async UniTask<TView> InstantiateViewAsync<TView>(string key, CancellationToken ct) where TView : IUIView
        {
            var prefab = await _loader.LoadAsync<UIViewBase>(key, ct);
            ct.ThrowIfCancellationRequested();
            var instance = UnityEngine.Object.Instantiate(prefab);

            if (instance is not TView view)
            {
                UnityEngine.Object.Destroy(instance.gameObject);
                await _loader.UnloadAsync(key, CancellationToken.None);
                throw new InvalidCastException(
                    $"[UIViewFactory] Prefab '{key}' root component is {instance.GetType().Name}, " +
                    $"not {typeof(TView).Name}. Ensure the prefab's root MonoBehaviour extends {typeof(TView).Name}.");
            }

            return view;
        }

        // Single source of truth for key derivation — used by both CreateGenericAsync (via
        // InstantiateViewAsync) and needed here for the pre-computed key passed into it.
        private static string GetKey(Type viewType)
        {
            var keyAttr = Attribute.GetCustomAttribute(viewType, typeof(UIViewKeyAttribute)) as UIViewKeyAttribute;
            return keyAttr?.Key ?? viewType.Name;
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
