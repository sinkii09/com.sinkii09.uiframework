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
        UniTask ChangeStateAsync<TState>(CancellationToken ct = default) where TState : IViewState;
        /// <summary>
        /// Clears the state machine's current state pointer.
        /// Called internally by ChangeStateAsync before every state transition — callers do not need to call this manually.
        /// Call manually only when bypassing ChangeStateAsync (e.g. navigating via ShowAsync directly).
        /// </summary>
        void ResetState();
    }
}
