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

        // Resting pose is Vector3.one — both show and hide tweens ultimately settle there
        // (show ends at one; hide's cancelled-mid-flight state is invisible either way since the
        // animator has already zeroed alpha and the GO is about to be deactivated).
        public override void RestoreOnCancel(UIViewBase view)
        {
            if (view != null) view.transform.localScale = Vector3.one;
        }
    }
}
