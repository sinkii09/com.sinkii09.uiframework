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
        // This method OWNS OnComplete/OnKill on every tween it awaits — DOTween's setters assign,
        // they do not chain, so anything else installed on the same tween (e.g. a transition's own
        // restore-on-cancel logic) would be silently overwritten here. Restore-on-cancel logic
        // belongs in the caller (DOTweenUIAnimator's catch blocks via UITransition.RestoreOnCancel),
        // never on the tween itself.
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
