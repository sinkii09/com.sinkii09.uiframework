using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Sinkii09.UIFramework
{
    // Root VContainer LifetimeScope for the UIFramework.
    // Persists across scene loads via DontDestroyOnLoad — place the UIRoot prefab in your
    // first scene and it will survive for the application lifetime. A duplicate-instance
    // guard ensures a second UIRoot (e.g. from an additive scene load) is destroyed
    // immediately, so only one container ever exists.
    // Subclasses: override OnAwake() (not Awake) for post-container initialisation.
    //             override Configure() to register additional services.
    // Assign UIFrameworkConfig in the Inspector, or place UIFrameworkConfig.asset in Resources/.
    [AddComponentMenu("UIFramework/UIFrameworkLifetimeScope")]
    public class UIFrameworkLifetimeScope : LifetimeScope
    {
        private static UIFrameworkLifetimeScope _instance;

        // Owned by this scope, not by the container: it is created in Configure (so it can be
        // registered) but must be disposed and unhooked in OnDestroy.
        private UIRenderScheduler _scheduler;

        [SerializeField] private UIFrameworkConfig _config;
        [SerializeField] private UIRootLayerRefs _layers;

        [Tooltip("Optional. Per-view policy (resident / backdrop / preload). Leave empty for framework defaults — every view gets UIViewPolicy.Default.")]
        [SerializeField] private UIViewPolicyConfig _viewPolicies;

        protected override void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            base.Awake();
            OnAwake();
        }

        // Called after the VContainer container is built. Override in subclasses for
        // post-build initialisation without touching the singleton guard or DDOL logic.
        protected virtual void OnAwake() { }

        protected override void OnDestroy()
        {
            // Cleared BEFORE the container is disposed. UIBindingExtensions.Scheduler is a static
            // hook: leaving it set would hand every binding created after teardown a dead
            // scheduler whose pump is no longer running, and they would simply never update.
            // The identity check keeps a second scope from clearing the live one's hook.
            if (_scheduler != null && ReferenceEquals(UIBindingExtensions.Scheduler, _scheduler))
                UIBindingExtensions.Scheduler = null;

            _scheduler?.Dispose();
            _scheduler = null;

            base.OnDestroy();
            if (_instance == this) _instance = null;
        }

        protected override void Configure(IContainerBuilder builder)
        {
            var config = _config != null ? _config : LoadDefaultConfig();

            builder.RegisterInstance(config);

            if (_layers == null)
                Debug.LogError("[UIFrameworkLifetimeScope] UIRootLayerRefs not assigned in Inspector — layer-based view parenting will fail.", this);
            else
                builder.RegisterInstance(_layers);

            RegisterLoader(builder, config);

            // Frame clock for coalesced bindings. Registered unconditionally — VContainer ignores
            // C# optional-parameter defaults, so a conditional registration would throw while
            // constructing any consumer that takes one — AND published to the static hook on
            // UIBindingExtensions, which cannot take a constructor dependency. OnDestroy clears
            // both the hook and the instance.
            // Disposed first on the off-chance Configure runs twice for one scope: otherwise the
            // orphaned pump stays registered on PostLateUpdate for the rest of the session.
            _scheduler?.Dispose();
            _scheduler = new UIRenderScheduler(host: null, maxSuspendedFrames: config.MaxSuspendedFrames);
            builder.RegisterInstance<IUIRenderScheduler>(_scheduler).AsSelf();
            UIBindingExtensions.Scheduler = _scheduler;

            builder.Register<IUIAnimator, DOTweenUIAnimator>(Lifetime.Singleton);
            // .AsSelf() so the concrete type resolves to the SAME singleton — UIViewCacheSweeper
            // and UIViewPreloader both need it for internal members. Same idiom as UINavigator
            // below. Deliberately not a lambda alias resolving the interface: that registers a
            // second entry for one IDisposable instance (double-dispose risk) and hard-casts, so
            // it would throw at resolve time if a game's Configure override swapped the interface.
            builder.Register<IUIViewFactory, UIViewFactory>(Lifetime.Singleton).AsSelf();
            builder.Register<INavigationStack, NavigationStack>(Lifetime.Singleton);
            builder.Register<IUIStateMachine, UIStateMachine>(Lifetime.Singleton);


            // SafeAreaProvider should be a MonoBehaviour on the UIRoot prefab hierarchy; absent ->
            // NullSafeAreaProvider so DI resolution never throws. Still warned (not silent) because,
            // unlike ITransitionOverlay, safe-area support isn't meant to be opt-in per-game.
            if (GetComponentInChildren<SafeAreaProvider>() != null)
                builder.RegisterComponentInHierarchy<SafeAreaProvider>().AsImplementedInterfaces();
            else
            {
                Debug.LogWarning("[UIFrameworkLifetimeScope] SafeAreaProvider not found in hierarchy — add it to the UIRoot prefab. Falling back to a full-screen NullSafeAreaProvider.", this);
                builder.RegisterInstance<ISafeAreaProvider>(new NullSafeAreaProvider());
            }

            builder.Register<IBackButtonSource, NewInputSystemBackButtonSource>(Lifetime.Singleton);
            builder.RegisterEntryPoint<BackButtonRouter>();

            // Resident full-screen overlay (optional per-game). Present anywhere in the scene -> real
            // TransitionOverlayView; absent -> NullTransitionOverlay so GameLifecycleManager
            // never needs to null-check. Scene-wide search (not GetComponentInChildren) to match
            // RegisterComponentInHierarchy's own resolution scope — VContainer's FindComponentProvider
            // searches the whole scene, not just this LifetimeScope's subtree.
            if (FindAnyObjectByType<TransitionOverlayView>(FindObjectsInactive.Include) != null)
                builder.RegisterComponentInHierarchy<TransitionOverlayView>().As<ITransitionOverlay>();
            else
                builder.RegisterInstance<ITransitionOverlay>(new NullTransitionOverlay());

            // Resident tooltip owner — same shape as the overlay above: a TooltipViewBase anywhere
            // in the scene means the real service, absent means the Null-Object, and the
            // registration is unconditional either way because VContainer ignores C# optional
            // parameter defaults (UINavigator takes an ITooltipService).
            //
            // RegisterEntryPoint is required, not stylistic: TooltipService.Initialize() discovers
            // and injects the tooltip views, and Tick() drives the whole timing state machine.
            // A plain Register would leave both undispatched and tooltips silently dead.
            // AsImplementedInterfaces (inside RegisterEntryPoint) already maps ITooltipService;
            // AsSelf is for tests that need the concrete type, mirroring GameLifecycleManager below.
            if (FindAnyObjectByType<TooltipViewBase>(FindObjectsInactive.Include) != null)
                builder.RegisterEntryPoint<TooltipService>(Lifetime.Singleton).AsSelf();
            else
                builder.RegisterInstance<ITooltipService>(new NullTooltipService());

            // Resident toast host — identical shape and identical reasoning to the tooltip block
            // above. RegisterEntryPoint is required for the same reason: Initialize() discovers and
            // injects the host, Tick() advances every notification timer AND every row fade, so a
            // plain Register would leave toasts silently dead.
            if (FindAnyObjectByType<NotificationHostView>(FindObjectsInactive.Include) != null)
                builder.RegisterEntryPoint<NotificationService>(Lifetime.Singleton).AsSelf();
            else
                builder.RegisterInstance<INotificationService>(new NullNotificationService());

            // Scans assemblies for concrete UIView<T> subclasses; populates UIViewRegistry.Registrations.
            UIViewRegistry.AutoRegister();
            builder.RegisterInstance(UIViewRegistry.Registrations);

            // Registered UNCONDITIONALLY even when _viewPolicies is null. VContainer ignores C#
            // optional-parameter defaults, so a conditionally registered resolver would throw
            // VContainerException while constructing every consumer that takes one. The resolver
            // handles a null config itself and hands back UIViewPolicy.Default for every view.
            // (Note this is why we build the resolver here rather than RegisterInstance(_viewPolicies) —
            // RegisterInstance derives the implementation type from instance.GetType() and throws on null.)
            var policies = new UIViewPolicyResolver(_viewPolicies);
            policies.ValidateAgainst(UIViewRegistry.Registrations);
            builder.RegisterInstance(policies);

            // Also unconditional, and for the same reason — UINavigator takes it, and VContainer
            // would throw on construction if it were only registered when some policy happens to
            // set NeedsBackdrop. With no policy asset it simply never shows anything.
            builder.Register<UIBackdrop>(Lifetime.Singleton);

            builder.Register<IUINavigator, UINavigator>(Lifetime.Singleton).AsSelf();

            // Warms views marked PreloadOnBoot. Registered always, but never runs on its own —
            // the game calls PreloadAllAsync() from its own boot sequence. With no policy asset
            // nothing is marked, so the call is a no-op returning 0.
            builder.Register<UIViewPreloader>(Lifetime.Singleton);

            // Opt-in: no sweeper, no behaviour change. Conditional registration is safe here
            // precisely because nothing injects UIViewCacheSweeper (contrast UIViewPolicyResolver
            // above, where conditional registration would break every consumer's construction).
            // It needs the concrete UIViewFactory for the internal SweepAsync, which the .AsSelf()
            // on the factory registration above provides unconditionally.
            if (config.ViewCacheGraceSeconds > 0f)
            {
                // Eviction destroys hidden views. Any reference game code still holds to one
                // becomes a Unity-null and throws MissingReferenceException on next use — the
                // HUD-channel pattern (views created straight from the factory, never pushed on
                // the navigation stack) is the likely victim. With no policy asset, NOTHING is
                // resident, so warn rather than let that surface as a mystery null later.
                if (_viewPolicies == null)
                {
                    Debug.LogWarning("[UIFrameworkLifetimeScope] View cache eviction is enabled " +
                        $"(ViewCacheGraceSeconds = {config.ViewCacheGraceSeconds}) but no UIViewPolicyConfig " +
                        "is assigned, so no view is resident. Any view held by game code will be destroyed " +
                        "once hidden past the grace period. Assign a policy asset and mark those views Resident.", this);
                }

                builder.RegisterEntryPoint<UIViewCacheSweeper>();
            }
            
            // --- Game Lifecycle ---
            builder.Register<ISceneLoader, SceneLoader>(Lifetime.Singleton);
            builder.RegisterInstance<ILoadingContext>(new LoadingContext());
            RegisterBootState(builder);
            builder.Register<LoadingState>(Lifetime.Singleton);
            // Game-specific states (GameplayState, PauseState, etc.) are registered by the game
            // developer's IInitializable bootstrap via GameLifecycleManager.RegisterState<T>().
            builder.RegisterEntryPoint<GameLifecycleManager>(Lifetime.Singleton).AsSelf();

            // --- Persistence ---
            builder.Register<IStorageBackend, LocalFileStorageBackend>(Lifetime.Singleton);
            builder.Register<ISaveService, JsonSaveService>(Lifetime.Singleton);
        }

        // Override in your game's LifetimeScope to substitute a custom BootState subclass.
        protected virtual void RegisterBootState(IContainerBuilder builder)
            => builder.Register<BootState>(Lifetime.Singleton);

        private static void RegisterLoader(IContainerBuilder builder, UIFrameworkConfig config)
        {

            if (config.LoaderMode == LoaderMode.Addressables)
            {
                builder.Register<IUILoader, AddressablesUILoader>(Lifetime.Singleton);
                return;
            }

            builder.Register<IUILoader, ResourcesUILoader>(Lifetime.Singleton);
        }

        private static UIFrameworkConfig LoadDefaultConfig()
        {
            var config = Resources.Load<UIFrameworkConfig>("UIFrameworkConfig");
            if (config == null)
            {
                Debug.LogWarning("[UIFrameworkLifetimeScope] UIFrameworkConfig not found in Resources/. Using defaults.");
                config = ScriptableObject.CreateInstance<UIFrameworkConfig>();
            }
            return config;
        }
    }
}
