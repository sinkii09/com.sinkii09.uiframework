using DG.Tweening;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    [CreateAssetMenu(menuName = "UIFramework/Transitions/Fade", fileName = "FadeTransition")]
    public class FadeTransition : UITransition
    {
        public override Tween CreateShowTween(UIViewBase view) =>
            view.CanvasGroup.DOFade(1f, Duration).From(0f).SetEase(EaseType);

        public override Tween CreateHideTween(UIViewBase view) =>
            view.CanvasGroup.DOFade(0f, Duration).From(1f).SetEase(EaseType);
    }
}
