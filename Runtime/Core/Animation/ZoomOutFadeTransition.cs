using DG.Tweening;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Hides a view by scaling the root up (zoom-out punch) while fading the CanvasGroup to 0.
    // Reverse on show: fades in + scales from ZoomOutScale back to 1.
    // SetUpdate(true) is applied by DOTweenUIAnimator — not needed here.
    [CreateAssetMenu(menuName = "UIFramework/Transitions/ZoomOutFade", fileName = "ZoomOutFadeTransition")]
    public class ZoomOutFadeTransition : UITransition
    {
        [Tooltip("Scale the root reaches at the end of hide (e.g. 1.5 = zooms out to 150%)")]
        public float ZoomOutScale = 1.5f;

        public override Tween CreateHideTween(UIViewBase view)
        {
            var t = view.transform;
            var cg = view.CanvasGroup;
            var seq = DOTween.Sequence();
            seq.Join(t.DOScale(new Vector3(ZoomOutScale, ZoomOutScale, 1f), Duration).SetEase(EaseType));
            seq.Join(cg.DOFade(0f, Duration).SetEase(EaseType));
            // OnKill: restore to visible/resting state so a cancelled hide leaves no mid-tween scale or alpha.
            seq.OnKill(() =>
            {
                if (t != null) t.localScale = Vector3.one;
                if (cg != null) cg.alpha = 1f;
            });
            return seq;
        }

        public override Tween CreateShowTween(UIViewBase view)
        {
            var t = view.transform;
            var cg = view.CanvasGroup;
            var zoomedOut = new Vector3(ZoomOutScale, ZoomOutScale, 1f);
            var seq = DOTween.Sequence();
            seq.Join(t.DOScale(Vector3.one, Duration).From(zoomedOut).SetEase(EaseType));
            seq.Join(cg.DOFade(1f, Duration).From(0f).SetEase(EaseType));
            // OnKill: restore to pre-show hidden state so a cancelled show leaves no mid-tween scale or alpha.
            seq.OnKill(() =>
            {
                if (t != null) t.localScale = zoomedOut;
                if (cg != null) cg.alpha = 0f;
            });
            return seq;
        }
    }
}
