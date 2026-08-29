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

        // Time.unscaledTime when each cached type was last created or reused. Drives idle-based
        // eviction (see SweepAsync). Unscaled so a paused game still ages its cache.
        private readonly Dictionary<Type, float> _lastTouched = new();

        // Scratch collections reused by SweepAsync so a periodic sweep doesn't allocate.
        private readonly List<Type> _sweepVictims = new();
        // Instances, not types — see the live-matching comment in SweepAsync.
        private readonly HashSet<UIViewBase> _sweepLive = new();

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
            _lastTouched.Clear();
        }

        // Destroys cached views that have gone unused for `graceSeconds`, releasing their loader
        // handles. Returns how many were evicted. Driven by UIViewCacheSweeper; internal because
        // policy and the live-view list both live outside the factory.
        //
        // `live` = views currently on the navigation stack. They are re-stamped rather than merely
        // skipped, which makes "on stack" and "the idle clock restarts when it leaves the stack"
        // one operation — no hide/pop notification into the factory, and therefore no cycle
        // between factory and navigator.
        //
        // NOTE the grace inversion vs UIFrameworkConfig: there, ViewCacheGraceSeconds == 0 means
        // the feature is DISABLED and no sweeper is even registered. Here, grace == 0 means
        // "evict everything eligible right now" — which is exactly what the tests want, and is
        // why they can be deterministic without touching wall-clock time. Do not wire config
        // straight through to this parameter without preserving that check.
        internal async UniTask<int> SweepAsync(
            IReadOnlyList<IUIView> live, float graceSeconds,
            Func<Type, bool> isResident, CancellationToken ct = default)
        {
            // Bail before touching anything on shutdown. Note phase 3 deliberately does NOT honour
            // this token — once entries are removed, a cancelled unload leaks the handle forever.
            if (ct.IsCancellationRequested) return 0;

            var now = UnityEngine.Time.unscaledTime;

            // Live views are tracked by INSTANCE, not by type. _cache is keyed by the REQUESTED
            // view type, but CreateCoreAsync only requires the prefab's component to be an
            // instance of that type — a subclass prefab root makes instance.GetType() differ from
            // the cache key, and a type-keyed live set would then fail to match and evict a view
            // that is on the stack.
            _sweepLive.Clear();
            if (live != null)
            {
                for (int i = 0; i < live.Count; i++)
                {
                    if (live[i] is UIViewBase vb) _sweepLive.Add(vb);
                }
            }

            // Phase 1 — select. Cannot mutate _cache while enumerating it, and must not await
            // mid-selection either: an interleaved CreateAsync could resurrect a type already
            // chosen for eviction.
            _sweepVictims.Clear();
            foreach (var (type, view) in _cache)
            {
                if (_pending.ContainsKey(type)) continue;

                // A destroyed GameObject (scene unload) still owns a real loader handle, so it
                // must be swept rather than skipped — but it has no state left to check.
                if (view == null) { _sweepVictims.Add(type); continue; }

                // Live views are BOTH re-stamped and skipped, and the SKIP is what protects them.
                // Re-stamping alone is not enough: it sets touched = now, so the grace test
                // becomes `0 < grace` — false whenever grace is 0, which would destroy the whole
                // live stack under the player. Re-stamping still earns its keep as the "idle
                // clock restarts when the view leaves the stack" mechanism, which is what removes
                // the need for a hide/pop notification into the factory.
                if (_sweepLive.Contains(view)) { _lastTouched[type] = now; continue; }

                if (isResident != null && isResident(type)) continue;

                // IsVisible alone is not enough. It is set at the END of ShowAsync, while
                // CreateCoreAsync populates _cache and clears _pending BEFORE the caller ever
                // calls ShowAsync, and NavigationStack only adds to `live` AFTER ShowAsync
                // completes. For the whole entrance animation a view is therefore cached, not
                // pending and not live — evictable on grace alone, which would destroy it
                // mid-animation. activeSelf closes that window: ShowAsync calls SetActive(true)
                // before awaiting the animation.
                if (view.IsVisible || view.gameObject.activeSelf) continue;

                if (_lastTouched.TryGetValue(type, out var touched) && now - touched < graceSeconds) continue;

                _sweepVictims.Add(type);
            }

            if (_sweepVictims.Count == 0) return 0;

            // Phase 2 — tear down synchronously, collecting keys. Still no await here.
            var keys = new List<string>(_sweepVictims.Count);
            try
            {
                for (int i = 0; i < _sweepVictims.Count; i++)
                {
                    var type = _sweepVictims[i];

                    // Collect the key BEFORE teardown. FactoryReset disposes a user-authored
                    // VContainer scope (R3 subscriptions, game code) and can throw; if it did so
                    // after we'd removed the dictionary entries but before recording the key, the
                    // handle would be unreachable by both Dispose() and any later sweep.
                    if (_cacheKeys.TryGetValue(type, out var key)) keys.Add(key);

                    // All three dictionaries, always. A _cacheKeys entry left behind after _cache
                    // loses the type is unreachable — Dispose() iterates _cache — so its handle
                    // would never be released.
                    _cache.TryGetValue(type, out var view);
                    _cache.Remove(type);
                    _cacheKeys.Remove(type);
                    _lastTouched.Remove(type);

                    if (view == null) continue;
                    try
                    {
                        view.FactoryReset();
                    }
                    catch (Exception ex)
                    {
                        // One view's teardown must not strand the rest of this sweep's handles.
                        UnityEngine.Debug.LogException(ex);
                    }
                    UnityEngine.Object.Destroy(view.gameObject);
                }
            }
            finally
            {
                _sweepVictims.Clear();

                // Phase 3 — release handles. In a finally so an unexpected throw above still
                // releases everything already removed from the dictionaries. CancellationToken.None
                // deliberately: the entries are already gone, so a cancelled unload here would
                // leak the handle permanently (same reasoning as Dispose()).
                for (int i = 0; i < keys.Count; i++)
                    await _loader.UnloadAsync(keys[i], CancellationToken.None);
            }

            return keys.Count;
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
                _lastTouched[viewType] = UnityEngine.Time.unscaledTime;
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
                    // All three dictionaries, matching SweepAsync. Removing only from _cache
                    // leaves _cacheKeys/_lastTouched entries that nothing can reach — both
                    // Dispose() and SweepAsync iterate _cache — so a failed re-create over a
                    // Unity-null cached entry would strand the previous load's handle.
                    _cache.Remove(viewType);
                    _cacheKeys.Remove(viewType);
                    _lastTouched.Remove(viewType);
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
