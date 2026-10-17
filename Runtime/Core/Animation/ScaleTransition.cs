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
            return t.DOScale(Vector3.one, Duration).From(StartScale).SetEase(EaseType);
        }

        public override Tween CreateHideTween(UIViewBase view)
        {
            var t = view.transform;
            return t.DOScale(StartScale, Duration).From(Vector3.one).SetEase(EaseType);
        }

        // Resting pose is Vector3.one — show ends there, and a hide cancelled mid-flight is restored
        // to it because the view is deactivated right after and the next show starts from a resting
        // pose (the animator's cancel path leaves alpha at 1, so the pose must be correct, not hidden).
        public override void RestoreOnCancel(UIViewBase view)
        {
            ResetScale(view);
        }
    }
}
