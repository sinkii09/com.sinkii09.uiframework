using Cysharp.Threading.Tasks;
using System.Threading;

namespace Sinkii09.UIFramework
{
    public interface IUINavigator
    {
        IUIView Current { get; }
        bool IsTransitioning { get; }

        UniTask ShowAsync<T>(CancellationToken ct = default) where T : IUIView;
        // View-to-args type safety is enforced by IUIViewFactory; the navigator routes only
        UniTask ShowAsync<T, TArgs>(TArgs args, CancellationToken ct = default) where T : IUIView where TArgs : IViewArgs;
        UniTask HideAsync<T>(CancellationToken ct = default) where T : IUIView;
        UniTask PopAsync(CancellationToken ct = default);
        UniTask CloseAllAsync(CancellationToken ct = default);
        UniTask ChangeStateAsync<TState>(CancellationToken ct = default) where TState : IViewState;
    }
}
