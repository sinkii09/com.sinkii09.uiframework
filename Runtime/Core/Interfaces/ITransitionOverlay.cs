using Cysharp.Threading.Tasks;
using System.Threading;

namespace Sinkii09.UIFramework
{
    // Resident full-screen overlay covering the gap between UINavigator.CloseAllAsync() and the
    // new view's factory load + ShowAsync finishing. Owned exclusively by GameLifecycleManager.
    // Games without an overlay in their UIRoot hierarchy get NullTransitionOverlay (no-op) instead.
    public interface ITransitionOverlay
    {
        bool IsShown { get; }

        UniTask ShowAsync(CancellationToken ct = default);
        UniTask HideAsync(CancellationToken ct = default);
    }
}
