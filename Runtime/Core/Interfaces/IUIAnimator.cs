using Cysharp.Threading.Tasks;
using System.Threading;

namespace Sinkii09.UIFramework
{
    public interface IUIAnimator
    {
        UniTask ShowAsync(IUIView view, UITransition transition, CancellationToken ct = default);
        UniTask HideAsync(IUIView view, UITransition transition, CancellationToken ct = default);
    }
}
