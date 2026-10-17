using Cysharp.Threading.Tasks;
using DG.Tweening;
using System;
using System.Threading;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Implements IUIAnimator with DOTween. Unscaled time comes from AwaitAsync, so UI animations
    // run during Time.timeScale = 0 pauses.
    //
    // Owns CanvasGroup.alpha (the resting value after every transition) and LOCKS input for the
    // duration. It never unlocks input: UIViewBase does that, only after OnShowAsync finishes.
    public class DOTweenUIAnimator : IUIAnimator
    {
        public UniTask ShowAsync(IUIView view, UITransition transition, CancellationToken ct = default)
            => PlayAsync(view, transition, show: true, ct);

        public UniTask HideAsync(IUIView view, UITransition transition, CancellationToken ct = default)
            => PlayAsync(view, transition, show: false, ct);

        private static async UniTask PlayAsync(IUIView view, UITransition transition, bool show, CancellationToken ct)
        {
            if (view is not UIViewBase viewBase)
            {
                Debug.LogWarning($"[DOTweenUIAnimator] {(show ? "ShowAsync" : "HideAsync")}: {view?.ViewId} is not UIViewBase — animation skipped.");
                return;
            }

            var cg = viewBase.CanvasGroup;
            if (cg == null)
            {
                Debug.LogError($"[DOTweenUIAnimator] {view.ViewId} has no CanvasGroup.");
                return;
            }

            // Locked in both directions, null transition included: a freshly-added CanvasGroup
            // defaults to interactable = true, which would leave a view clickable for its entire
            // OnShowAsync.
            cg.interactable = false;
            cg.blocksRaycasts = false;

            float restAlpha = show ? 1f : 0f;
            if (transition == null)
            {
                cg.alpha = restAlpha;
                return;
            }

            // Visible BEFORE a show tween is built. A prior hide leaves alpha at 0, and Scale/Slide
            // never touch alpha, so without this they played their whole show invisible and popped in
            // at the end. Safe for alpha-driving transitions: their .From(0) applies at build time, in
            // this same frame, so nothing renders at 1 first.
            if (show) cg.alpha = 1f;

            try
            {
                // SetTarget(view): a bare DOScale/DOAnchorPos targets the root transform, so a stray
                // DOKill(transform) from other code would kill the transition and read as a cancellation.
                var tween = show ? transition.CreateShowTween(viewBase) : transition.CreateHideTween(viewBase);
                await tween.SetTarget(viewBase).SetLink(viewBase.gameObject).AwaitAsync(ct);
                // Resting alpha regardless of whether the transition animates alpha itself.
                cg.alpha = restAlpha;
            }
            catch (OperationCanceledException)
            {
                // Geometry restore lives here, not in the tween's OnKill — AwaitAsync replaces that
                // callback, and OnKill also fires on normal completion.
                transition.RestoreOnCancel(viewBase);
                // A cancelled show is hidden (its mid-tween pose is invalid). A cancelled hide is put
                // back at its visible pose, but input stays locked ON PURPOSE: every UIViewBase.HideAsync
                // exit deactivates the view, and the next show unlocks input after OnShowAsync.
                cg.alpha = 1f - restAlpha;
                throw;
            }
        }
    }
}
