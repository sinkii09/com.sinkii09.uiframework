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
            var seq = DOTween.Sequence();
            seq.Join(view.transform.DOScale(new Vector3(ZoomOutScale, ZoomOutScale, 1f), Duration).SetEase(EaseType));
            seq.Join(view.CanvasGroup.DOFade(0f, Duration).SetEase(EaseType));
            return seq;
        }

        public override Tween CreateShowTween(UIViewBase view)
        {
            var seq = DOTween.Sequence();
            seq.Join(view.transform.DOScale(Vector3.one, Duration)
                .From(new Vector3(ZoomOutScale, ZoomOutScale, 1f))
                .SetEase(EaseType));
            seq.Join(view.CanvasGroup.DOFade(1f, Duration).From(0f).SetEase(EaseType));
            return seq;
        }
    }
}
