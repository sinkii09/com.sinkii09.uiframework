using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using VContainer;

namespace Sinkii09.UIFramework
{
    public interface IUIViewFactory
    {
        UniTask<TView> CreateAsync<TView, TViewModel>(CancellationToken ct = default)
            where TView : IUIView
            where TViewModel : class, IViewModel;

        UniTask<TView> CreateAsync<TView, TViewModel, TArgs>(TArgs args, CancellationToken ct = default)
            where TView : IUIView
            where TViewModel : class, IViewModel<TArgs>
            where TArgs : IViewArgs;

        // Type-erased overload used by UINavigator's auto-registration path — AOT-safe, no MakeGenericMethod.
        // key: addressable/Resources key for the prefab (from UIViewKeyAttribute or view type name).
        UniTask<IUIView> CreateAsync(Type viewType, Type vmType, string key, CancellationToken ct = default);

        // Override the container used to create per-view child scopes. Call from a game-specific
        // LifetimeScope.Awake() AFTER base.Awake() so game-scope registrations (e.g. IProgressionService)
        // are visible to ViewModels. Call ResetScopeContainer(Container) from OnDestroy() to avoid stale refs.
        // Pass the same resolver that was passed to SetScopeContainer so that a reloaded scene's
        // OnDestroy cannot accidentally null a newer scope's override.
        void SetScopeContainer(IObjectResolver resolver);
        void ResetScopeContainer(IObjectResolver expected = null);
    }
}
