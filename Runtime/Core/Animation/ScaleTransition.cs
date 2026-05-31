using DG.Tweening;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    [CreateAssetMenu(menuName = "UIFramework/Transitions/Scale", fileName = "ScaleTransition")]
    public class ScaleTransition : UITransition
    {
        public Vector3 StartScale = new Vector3(0.8f, 0.8f, 1f);

        public override Tween CreateShowTween(UIViewBase view) =>
            view.transform.DOScale(Vector3.one, Duration).From(StartScale).SetEase(EaseType);

        public override Tween CreateHideTween(UIViewBase view) =>
            view.transform.DOScale(StartScale, Duration).From(Vector3.one).SetEase(EaseType);
    }
}
