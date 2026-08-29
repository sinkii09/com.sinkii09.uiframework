using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;

namespace Sinkii09.UIFramework
{
    // Warms views into UIViewFactory's cache ahead of time so the first ShowAsync<T> during
    // gameplay doesn't pay the asset load, the Instantiate, or the layer reparent.
    //
    // WHAT IT DOES *NOT* SAVE: the child scope and the ViewModel. CreateCoreAsync's cache-HIT path
    // calls FactoryReset() and then unconditionally rebuilds the scope, re-injects the GameObject,
    // resolves a fresh ViewModel and re-runs InitializeNonGenericAsync. So preloading builds a
    // ViewModel at BOOT that is disposed and replaced on the view's first real show.
    //
    // CONSEQUENCE, and the reason this is spelled out: any ViewModel whose construction has side
    // effects — R3 subscriptions, save-file reads, analytics, timers — runs those effects TWICE,
    // the first time at boot, before the game's own services may exist. Keep ViewModel constructors
    // inert and do that work in OnShow(), which is the framework's convention anyway.
    //
    // Deliberately NOT an entry point, and nothing calls it automatically. BootState is virtual,
    // so auto-preloading there would add a boot stall that a consuming project never asked for
    // and cannot see the cause of. Call PreloadAllAsync() from your own boot sequence, behind a
    // loading screen.
    public sealed class UIViewPreloader
    {
        private readonly UIViewFactory _factory;
        private readonly UIViewPolicyResolver _policies;
        private readonly IReadOnlyList<UIViewRegistration> _registrations;
        private readonly Dictionary<Type, UIViewRegistration> _byType = new();

        // Takes the CONCRETE UIViewFactory for the internal IsCached probe, same precedent as
        // UIViewCacheSweeper. UIFrameworkLifetimeScope registers the concrete type as an alias
        // of the IUIViewFactory singleton, so this is the same instance, not a second factory.
        [Inject]
        public UIViewPreloader(UIViewFactory factory, UIViewPolicyResolver policies,
                               IReadOnlyList<UIViewRegistration> registrations)
        {
            _factory = factory;
            _policies = policies;

            // Snapshot, not the injected reference: PreloadAllAsync reads this list while
            // PreloadAsync(Type) reads the _byType map built from it. Holding the caller's live
            // list would let the two disagree if anything appended to it after construction.
            var source = registrations ?? Array.Empty<UIViewRegistration>();
            var snapshot = new UIViewRegistration[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                snapshot[i] = source[i];
                _byType[source[i].ViewType] = source[i];   // duplicate ViewType: last wins
            }
            _registrations = snapshot;
        }

        // Warms every view whose policy sets PreloadOnBoot. Returns how many were actually
        // created — already-cached and failed views are not counted.
        //
        // Sequential on purpose: preloading in parallel would overlap several Instantiate calls
        // and multiply peak memory at the worst possible moment (boot), for a wall-clock saving
        // that doesn't justify it behind a loading screen.
        public async UniTask<int> PreloadAllAsync(CancellationToken ct = default)
        {
            var set = _policies != null ? _policies.PreloadSet(_registrations) : null;
            if (set == null || set.Count == 0) return 0;

            int warmed = 0;
            for (int i = 0; i < set.Count; i++)
            {
                if (await PreloadRegistrationAsync(set[i], ct)) warmed++;
            }
            return warmed;
        }

        public UniTask<bool> PreloadAsync<TView>(CancellationToken ct = default) where TView : IUIView
            => PreloadAsync(typeof(TView), ct);

        // Warms one view regardless of its PreloadOnBoot flag — for a game that wants to warm a
        // specific screen at a specific moment (entering a hub, opening a menu tree) rather than
        // everything at boot.
        public UniTask<bool> PreloadAsync(Type viewType, CancellationToken ct = default)
        {
            if (viewType == null) throw new ArgumentNullException(nameof(viewType));

            if (!_byType.TryGetValue(viewType, out var registration))
            {
                Debug.LogError($"[UIViewPreloader] {viewType.Name} is not in UIViewRegistry.Registrations, " +
                               "so its ViewModel type and load key are unknown. Views are auto-registered by " +
                               "scanning for UIView<TViewModel> subclasses; a view created through a manual " +
                               "UINavigator.Register<,>() override is not preloadable.");
                return UniTask.FromResult(false);
            }
            return PreloadRegistrationAsync(registration, ct);
        }

        private async UniTask<bool> PreloadRegistrationAsync(UIViewRegistration registration, CancellationToken ct)
        {
            // Already warm, or being warmed by someone else right now — either way there is
            // nothing to do. Skipping is not just an optimisation: CreateAsync would either
            // FactoryReset() a cached view (disposing the scope and ViewModel of a view that may
            // be ON SCREEN) or join an in-flight first show via the dedup path. Both end with the
            // SetActive(false) below tearing down a live screen under the player. Preload must be
            // safe to call at any time, not only at boot.
            if (_factory.IsCachedOrPending(registration.ViewType)) return false;

            try
            {
                var view = await _factory.CreateAsync(registration.ViewType, registration.VmType,
                                                      registration.Key, ct);

                // CreateCoreAsync never touches activeSelf, so the instance keeps the prefab's
                // own state — active, for any normal UI prefab. Park it hidden; UIViewBase.ShowAsync
                // calls SetActive(true) unconditionally, so a hidden cached view still shows
                // correctly on its first real ShowAsync.
                //
                // No boot flash between Instantiate and here, but that rests on an invariant worth
                // stating: everything CreateCoreAsync does AFTER the loader's await is synchronous
                // (UIView<T>.InitializeAsync is non-virtual, returns UniTask.CompletedTask, and
                // BindViewModel is a sync abstract). There is no async init hook a game can
                // override. Introduce one that yields a frame and a preloaded view will render
                // over the boot screen for that frame — deactivate factory-side if that ever
                // changes.
                if (view is UIViewBase instance && instance != null)
                    instance.gameObject.SetActive(false);

                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // OUR cancellation — boot was aborted. That is the caller's business, not a
                // preload failure, so it propagates unlogged. The filter matters: an OCE from
                // somewhere else (a view's destroyCancellationToken, loader internals) is a real
                // failure and must fall through to the logged catch below rather than silently
                // abandoning every remaining view.
                throw;
            }
            catch (Exception ex)
            {
                // One unloadable prefab must not abort the rest of the warm-up, but it is a real
                // authoring error, so it is logged as one rather than swallowed. The eventual
                // ShowAsync will fail the same way at the call site that actually needs the view.
                Debug.LogError($"[UIViewPreloader] Failed to preload {registration.ViewType.Name} " +
                               $"(key \"{registration.Key}\"): {ex}");
                return false;
            }
        }
    }
}
