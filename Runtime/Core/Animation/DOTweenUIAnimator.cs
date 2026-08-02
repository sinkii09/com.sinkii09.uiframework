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
                // Locked out here, restored by UIViewBase after OnShowAsync completes — matches the
                // non-null-transition path below. Without this, a freshly-added CanvasGroup defaults
                // to interactable=true (Unity's own default), so a view would be clickable for the
                // view's ENTIRE OnShowAsync, not just briefly — this is the widest form of Finding 2.
                cg.alpha = 1f;
                cg.interactable = false;
                cg.blocksRaycasts = false;
                return;
            }

            cg.interactable = false;
            cg.blocksRaycasts = false;
            try
            {
                var showTween = transition.CreateShowTween(viewBase).SetLink(viewBase.gameObject);
                await showTween.AwaitAsync(ct);
                // Resting alpha, regardless of whether this transition animates alpha itself
                // (Fade/ZoomOutFade already end here; Scale/Slide never touch alpha at all).
                cg.alpha = 1f;
                // interactable/blocksRaycasts restored by UIViewBase after OnShowAsync completes.
            }
            catch (OperationCanceledException)
            {
                // Geometry restore lives here, not in the tween's OnKill — AwaitAsync replaces
                // that callback, and OnKill also fires on normal completion.
                transition.RestoreOnCancel(viewBase);
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
                var hideTween = transition.CreateHideTween(viewBase).SetLink(viewBase.gameObject);
                await hideTween.AwaitAsync(ct);
                // Resting alpha, regardless of whether this transition animates alpha itself.
                cg.alpha = 0f;
            }
            catch (OperationCanceledException)
            {
                transition.RestoreOnCancel(viewBase);
                // Reset to fully visible state — UIViewBase.HideAsync catch handles SetActive(false).
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
                throw;
            }
        }
    }
}
