using Cysharp.Threading.Tasks;
using System.Threading;

namespace Sinkii09.UIFramework
{
    // Default ITransitionOverlay when no TransitionOverlayView exists in the scene hierarchy.
    // Keeps GameLifecycleManager free of null-checks; every game behaves exactly as before
    // this feature existed unless it opts in by placing a TransitionOverlayView under the
    // Overlay layer of its UIRoot.
    public sealed class NullTransitionOverlay : ITransitionOverlay
    {
        public bool IsShown => false;

        public UniTask ShowAsync(CancellationToken ct = default) => UniTask.CompletedTask;
        public UniTask HideAsync(CancellationToken ct = default) => UniTask.CompletedTask;
    }
}
