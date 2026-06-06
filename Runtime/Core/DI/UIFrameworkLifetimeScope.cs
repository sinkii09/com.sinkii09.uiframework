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


            // SafeAreaProvider must be a MonoBehaviour on the UIRoot prefab hierarchy.
            if (GetComponentInChildren<SafeAreaProvider>() == null)
                Debug.LogError("[UIFrameworkLifetimeScope] SafeAreaProvider not found in hierarchy — add it to the UIRoot prefab.", this);
            else
                builder.RegisterComponentInHierarchy<SafeAreaProvider>().AsImplementedInterfaces();

            builder.Register<IBackButtonSource, NewInputSystemBackButtonSource>(Lifetime.Singleton);
            builder.RegisterEntryPoint<BackButtonRouter>();

            // Scans assemblies: registers all ViewModels as Transient + populates UIViewRegistry.Registrations.
            UIViewRegistry.AutoRegister(builder);
            builder.RegisterInstance(UIViewRegistry.Registrations);
            builder.Register<IUINavigator, UINavigator>(Lifetime.Singleton);
            
            // --- Game Lifecycle ---
            builder.Register<ISceneLoader, SceneLoader>(Lifetime.Singleton);
            builder.RegisterInstance<ILoadingContext>(new LoadingContext());
            RegisterBootState(builder);
            builder.Register<LoadingState>(Lifetime.Singleton);
            // Game-specific states (GameplayState, PauseState, etc.) are registered by the game
            // developer's IInitializable bootstrap via GameLifecycleManager.RegisterState<T>().
            builder.RegisterEntryPoint<GameLifecycleManager>(Lifetime.Singleton).AsSelf();
        }

        // Override in your game's LifetimeScope to substitute a custom BootState subclass.
        protected virtual void RegisterBootState(IContainerBuilder builder)
            => builder.Register<BootState>(Lifetime.Singleton);

        private static void RegisterLoader(IContainerBuilder builder, UIFrameworkConfig config)
        {
#if ADDRESSABLES
            if (config.LoaderMode == LoaderMode.Addressables)
            {
                builder.Register<IUILoader, AddressablesUILoader>(Lifetime.Singleton);
                return;
            }
#endif
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
