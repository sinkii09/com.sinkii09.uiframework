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

        // In-flight creation tasks keyed by view type. If two callers (either overload) request the
        // same view type concurrently (main-thread interleaving — not threading), the second awaits
        // the first result instead of instantiating a duplicate GameObject. VmType is tracked so a
        // mismatched second caller is at least warned instead of silently getting the wrong ViewModel.
        private readonly Dictionary<Type, (Type VmType, UniTaskCompletionSource<IUIView> Source)> _pending = new();

        // Deliberately takes NO UIViewPolicyResolver. VContainer ignores C# default parameter
        // values (ResolveOrParameter never reads ParameterInfo.HasDefaultValue), so adding one
        // — even as `= null` — makes it a HARD dependency and breaks every hand-built container
        // that doesn't register it. Policy is instead supplied per-call by the caller that owns
        // it (see the isResident predicate on SweepAsync), keeping this constructor's contract
        // unchanged for existing consumers.
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
            => (TView)await CreateCoreAsync(typeof(TView), typeof(TViewModel), GetKey(typeof(TView)), null, ct);

        public async UniTask<TView> CreateAsync<TView, TViewModel, TArgs>(TArgs args, CancellationToken ct = default)
            where TView : IUIView
            where TViewModel : class, IViewModel<TArgs>
            where TArgs : IViewArgs
            => (TView)await CreateCoreAsync(typeof(TView), typeof(TViewModel), GetKey(typeof(TView)),
                                            vm => ((IViewModel<TArgs>)vm).Initialize(args), ct);

        public UniTask<IUIView> CreateAsync(Type viewType, Type vmType, string key, CancellationToken ct = default)
            => CreateCoreAsync(viewType, vmType, key, null, ct);

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

        // The single creation implementation — both the generic overloads and the type-erased
        // overload delegate here. Previously this logic existed twice (a generic path and a
        // type-erased path) and only the generic path got the in-flight dedup guard; the type-erased
        // path is what UINavigator actually uses for every auto-registered view, so the guard was
        // effectively dead for the framework's default navigation path. See plans/260801-2148-
        // correctness-cluster/phase-01-factory-consolidation.md (finding C1).
        private async UniTask<IUIView> CreateCoreAsync(
            Type viewType, Type vmType, string key,
            Action<IViewModel> afterResolve, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (_pending.TryGetValue(viewType, out var inFlight))
            {
                if (inFlight.VmType != vmType)
                    UnityEngine.Debug.LogWarning($"[UIViewFactory] {viewType.Name} already in flight with " +
                                     $"{inFlight.VmType.Name}; awaiting that result, {vmType.Name} ignored.");
                return await inFlight.Source.Task;
            }
            var tcs = new UniTaskCompletionSource<IUIView>();
            _pending[viewType] = (vmType, tcs);

            bool isNew = !(_cache.TryGetValue(viewType, out var instance) && instance != null);
            if (!isNew) instance.FactoryReset();

            IObjectResolver scope = null;
            try
            {
                if (isNew)
                {
                    // LoadAsync can throw/cancel before 'instance' is assigned — CleanupAsync's
                    // instance != null guard below then no-ops, so no compensating UnloadAsync happens
                    // here (same net effect as the old type-erased path's separate try).
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
                    ReparentToLayer(instance);
                }

                scope = (_scopeContainer ?? _container).CreateScope(builder =>
                {
                    // Bind under the concrete view type, not UIViewBase — a ViewModel that injects
                    // the concrete view (e.g. [Inject] MyView _view) must resolve regardless of
                    // which overload created it. VContainer's RegisterInstance<T> binds the static
                    // type parameter, so this two-arg overload is required here (was previously
                    // RegisterInstance(instance), which bound UIViewBase on this path only).
                    builder.RegisterInstance(instance, viewType);
                    builder.Register(vmType, Lifetime.Scoped);
                });
                scope.InjectGameObject(instance.gameObject);
                var viewModel = (IViewModel)scope.Resolve(vmType);
                afterResolve?.Invoke(viewModel);
                await instance.InitializeNonGenericAsync(viewModel, scope, ct);

                _cache[viewType] = instance;
                if (isNew) _cacheKeys[viewType] = key;
                tcs.TrySetResult(instance);
                return instance;
            }
            catch (OperationCanceledException) { tcs.TrySetCanceled(); await CleanupAsync(); throw; }
            catch (Exception ex) { tcs.TrySetException(ex); await CleanupAsync(); throw; }
            finally { _pending.Remove(viewType); }

            // Local: identical cleanup for both the cancellation and general-exception catches.
            // On cache-miss failure: destroy the newly created GO and clear the cache entry.
            // On cache-hit failure: leave the GO in its FactoryReset() state so the next attempt
            // can retry with a fresh scope.
            async UniTask CleanupAsync()
            {
                scope?.Dispose();
                if (isNew && instance != null)
                {
                    _cache.Remove(viewType);
                    UnityEngine.Object.Destroy(instance.gameObject);
                    await _loader.UnloadAsync(key, CancellationToken.None);
                }
            }
        }

        // Delegates to UIViewKeys so the factory and UIViewRegistry cannot drift apart —
        // they derived this independently before.
        private static string GetKey(Type viewType) => UIViewKeys.For(viewType);

        private void ReparentToLayer(UIViewBase view)
        {
            if (_layers == null) return;
            var layer = _layers.GetLayer(view.Layer);
            if (layer != null)
                view.transform.SetParent(layer, false);
        }
    }
}
