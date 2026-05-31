using Cysharp.Threading.Tasks;
using System.Threading;

namespace Sinkii09.UIFramework
{
    public interface INavigationStack
    {
        int Count { get; }

        UniTask PushAsync(IUIView view, UITransitionType transition, CancellationToken ct = default);
        UniTask<IUIView> PopAsync(CancellationToken ct = default);
        IUIView Peek();
        UniTask ClearAsync(CancellationToken ct = default);
    }
}
