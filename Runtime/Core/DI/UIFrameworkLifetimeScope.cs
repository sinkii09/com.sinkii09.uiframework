using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Sinkii09.UIFramework
{
    // Root VContainer LifetimeScope for the UIFramework.
    // Add this MonoBehaviour to the UIRoot prefab (DontDestroyOnLoad bootstrap scene).
    // Assign UIFrameworkConfig in the Inspector, or place UIFrameworkConfig.asset in Resources/.
    [AddComponentMenu("UIFramework/UIFrameworkLifetimeScope")]
    public class UIFrameworkLifetimeScope : LifetimeScope
    {
        [SerializeField] private UIFrameworkConfig _config;
        [SerializeField] private UIRootLayerRefs _layers;

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
            builder.Register<IUINavigator, UINavigator>(Lifetime.Singleton);

            // SafeAreaProvider must be a MonoBehaviour on the UIRoot prefab hierarchy.
            if (GetComponentInChildren<SafeAreaProvider>() == null)
                Debug.LogError("[UIFrameworkLifetimeScope] SafeAreaProvider not found in hierarchy — add it to the UIRoot prefab.", this);
            else
                builder.RegisterComponentInHierarchy<SafeAreaProvider>().AsImplementedInterfaces();

            // RegisterEntryPoint covers IInitializable + IDisposable — no separate Register needed.
            builder.RegisterEntryPoint<BackButtonRouter>();

            // Registers all discovered UIView<T> ViewModels as Transient so UIViewFactory can resolve them.
            UIViewRegistry.AutoRegister(builder);

            // --- Game Lifecycle ---
            builder.Register<ISceneLoader, SceneLoader>(Lifetime.Singleton);
            builder.RegisterInstance<ILoadingContext>(new LoadingContext());
            builder.Register<BootState>(Lifetime.Singleton);
            builder.Register<LoadingState>(Lifetime.Singleton);
            // GameplayState and other game-specific states are NOT registered here.
            // In your game bootstrap IInitializable.Initialize(), resolve each state
            // and call _lifecycle.RegisterState(state) before StartAsync runs.
            builder.RegisterEntryPoint<GameLifecycleManager>(Lifetime.Singleton);
        }

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
