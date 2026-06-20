using DG.Tweening;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    [CreateAssetMenu(menuName = "UIFramework/Transitions/Scale", fileName = "ScaleTransition")]
    public class ScaleTransition : UITransition
    {
        public Vector3 StartScale = new Vector3(0.8f, 0.8f, 1f);

        public override Tween CreateShowTween(UIViewBase view)
        {
            var t = view.transform;
            // OnKill: restore to StartScale (pre-show state) so a cancelled show leaves no mid-tween scale.
            return t.DOScale(Vector3.one, Duration).From(StartScale).SetEase(EaseType)
                .OnKill(() => { if (t != null) t.localScale = StartScale; });
        }

        public override Tween CreateHideTween(UIViewBase view)
        {
            var t = view.transform;
            // OnKill: restore to Vector3.one (visible/resting state) so a cancelled hide leaves no mid-tween scale.
            return t.DOScale(StartScale, Duration).From(Vector3.one).SetEase(EaseType)
                .OnKill(() => { if (t != null) t.localScale = Vector3.one; });
        }
    }
}
