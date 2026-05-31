using Cysharp.Threading.Tasks;
using DG.Tweening;
using System;
using System.Threading;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Implements IUIAnimator using DOTween Pro tweens.
    // SetUpdate(true) on every tween so UI animations run during Time.timeScale=0 pauses.
    public class DOTweenUIAnimator : IUIAnimator
    {
        public async UniTask ShowAsync(IUIView view, UITransition transition, CancellationToken ct = default)
        {
            if (view is not UIViewBase viewBase)
            {
                Debug.LogWarning($"[DOTweenUIAnimator] ShowAsync: {view?.ViewId} is not UIViewBase — animation skipped.");
                return;
            }

            var cg = viewBase.CanvasGroup;
            if (cg == null)
            {
                Debug.LogError($"[DOTweenUIAnimator] {view.ViewId} has no CanvasGroup.");
                return;
            }

            if (transition == null)
            {
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
                return;
            }

            cg.interactable = false;
            cg.blocksRaycasts = false;
            try
            {
                await AwaitTween(transition.CreateShowTween(viewBase), ct);
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }
            catch (OperationCanceledException)
            {
                // Reset to hidden state — mid-tween alpha/scale is invalid.
                cg.alpha = 0f;
                cg.interactable = false;
                cg.blocksRaycasts = false;
                throw;
            }
        }

        public async UniTask HideAsync(IUIView view, UITransition transition, CancellationToken ct = default)
        {
            if (view is not UIViewBase viewBase)
            {
                Debug.LogWarning($"[DOTweenUIAnimator] HideAsync: {view?.ViewId} is not UIViewBase — animation skipped.");
                return;
            }

            var cg = viewBase.CanvasGroup;
            if (cg == null)
            {
                Debug.LogError($"[DOTweenUIAnimator] {view.ViewId} has no CanvasGroup.");
                return;
            }

            if (transition == null)
            {
                cg.alpha = 0f;
                cg.interactable = false;
                cg.blocksRaycasts = false;
                return;
            }

            cg.interactable = false;
            cg.blocksRaycasts = false;
            try
            {
                await AwaitTween(transition.CreateHideTween(viewBase), ct);
            }
            catch (OperationCanceledException)
            {
                // Reset to fully visible state — UIViewBase.HideAsync catch handles SetActive(false).
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
                throw;
            }
        }

        // Bridges DOTween Tween to UniTask without requiring UNITASK_DOTWEEN_SUPPORT.
        // OnKill fires for both cancellation and post-complete cleanup; TrySet variants are safe if already resolved.
        private static UniTask AwaitTween(Tween tween, CancellationToken ct)
        {
            tween.SetUpdate(true);
            var tcs = new UniTaskCompletionSource();
            tween.OnComplete(() => tcs.TrySetResult())
                 .OnKill(() => tcs.TrySetCanceled());
            if (ct.CanBeCanceled)
                ct.Register(() => tween.Kill());
            return tcs.Task;
        }
    }
}
