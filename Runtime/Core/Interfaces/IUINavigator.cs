using Cysharp.Threading.Tasks;
using System.Threading;

namespace Sinkii09.UIFramework
{
    public interface IUINavigator
    {
        IUIView Current { get; }
        bool IsTransitioning { get; }

        // Manual override for a no-args view (e.g. when a custom creation path is needed).
        void Register<TView, TViewModel>()
            where TView : IUIView
            where TViewModel : class, IViewModel;

        // Register a view that requires args. Call from bootstrap before any ShowAsync<T,TArgs> for this view.
        void Register<TView, TViewModel, TArgs>()
            where TView : IUIView
            where TViewModel : class, IViewModel<TArgs>
            where TArgs : IViewArgs;

        UniTask ShowAsync<T>(CancellationToken ct = default) where T : IUIView;
        // C# cannot link TArgs to T's expected ViewModel args type — mismatched calls compile
        // but throw at runtime inside IUIViewFactory. UINavigator asserts T's ViewModel accepts TArgs.
        UniTask ShowAsync<T, TArgs>(TArgs args, CancellationToken ct = default) where T : IUIView where TArgs : IViewArgs;
        UniTask HideAsync<T>(CancellationToken ct = default) where T : IUIView;
        UniTask PopAsync(CancellationToken ct = default);
        UniTask CloseAllAsync(CancellationToken ct = default);
        /// <summary>
        /// Clears the state machine's current state pointer.
        /// As of v1.2.0, the internal ChangeStateAsync no longer calls this automatically — doing so
        /// skipped the previous state's OnExitAsync, silently dropping non-view cleanup. Call this
        /// manually only for same-state re-entry when GameLifecycleManager.RestartCurrentStateAsync
        /// is not applicable (e.g. bypassing the lifecycle manager entirely via direct ShowAsync
        /// navigation) — the caller is responsible for any cleanup ResetState() skips.
        /// </summary>
        void ResetState();
    }
}
