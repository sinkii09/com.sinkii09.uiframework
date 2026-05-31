using Cysharp.Threading.Tasks;
using System.Threading;

namespace Sinkii09.UIFramework
{
    public interface IUINavigator
    {
        IUIView Current { get; }
        bool IsTransitioning { get; }

        UniTask ShowAsync<T>(CancellationToken ct = default) where T : IUIView;
        // C# cannot link TArgs to T's expected ViewModel args type — mismatched calls compile
        // but throw at runtime inside IUIViewFactory. UINavigator asserts T's ViewModel accepts TArgs.
        UniTask ShowAsync<T, TArgs>(TArgs args, CancellationToken ct = default) where T : IUIView where TArgs : IViewArgs;
        UniTask HideAsync<T>(CancellationToken ct = default) where T : IUIView;
        UniTask PopAsync(CancellationToken ct = default);
        UniTask CloseAllAsync(CancellationToken ct = default);
        UniTask ChangeStateAsync<TState>(CancellationToken ct = default) where TState : IViewState;
    }
}
