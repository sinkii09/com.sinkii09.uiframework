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

            builder.Register<IUIAnimator, DOTweenUIAnimator>(Lifetime.Singleton);
            builder.Register<IUIViewFactory, UIViewFactory>(Lifetime.Singleton);
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

            builder.Register<IUINavigator, UINavigator>(Lifetime.Singleton).AsSelf();
            
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
