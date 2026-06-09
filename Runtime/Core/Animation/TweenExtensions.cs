using Cysharp.Threading.Tasks;
using DG.Tweening;
using System.Threading;

namespace Sinkii09.UIFramework
{
    public static class TweenExtensions
    {
        // Bridges DOTween Tween to UniTask without UNITASK_DOTWEEN_SUPPORT.
        // SetUpdate(true) keeps the tween running at Time.timeScale=0.
        // CancellationTokenRegistration disposed on complete/kill prevents stale callbacks on pooled tweens.
        public static UniTask AwaitAsync(this Tween tween, CancellationToken ct = default)
        {
            tween.SetUpdate(true);
            var tcs = new UniTaskCompletionSource();
            CancellationTokenRegistration reg = default;
            tween.OnComplete(() => { reg.Dispose(); tcs.TrySetResult(); })
                 .OnKill(() => { reg.Dispose(); tcs.TrySetCanceled(); });
            if (ct.CanBeCanceled)
                reg = ct.Register(() => tween.Kill());
            return tcs.Task;
        }
    }
}
