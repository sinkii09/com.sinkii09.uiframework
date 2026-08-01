using DG.Tweening;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    public enum SlideDirection { Up, Down, Left, Right }

    [CreateAssetMenu(menuName = "UIFramework/Transitions/Slide", fileName = "SlideTransition")]
    public class SlideTransition : UITransition
    {
        public SlideDirection Direction = SlideDirection.Up;
        public float SlideDistance = 100f;

        public override Tween CreateShowTween(UIViewBase view)
        {
            var rt = view.RectTransform;
            if (rt == null)
            {
                Debug.LogError($"[SlideTransition] {view.ViewId} has no RectTransform — slide requires UGUI.");
                return DOTween.To(() => 0f, _ => { }, 1f, Duration);
            }

            var offset = GetOffset();
            rt.anchoredPosition = offset;

            return rt.DOAnchorPos(Vector2.zero, Duration).SetEase(EaseType);
        }

        public override Tween CreateHideTween(UIViewBase view)
        {
            var rt = view.RectTransform;
            if (rt == null)
            {
                Debug.LogError($"[SlideTransition] {view.ViewId} has no RectTransform — slide requires UGUI.");
                return DOTween.To(() => 0f, _ => { }, 1f, Duration);
            }

            return rt.DOAnchorPos(GetOffset(), Duration).SetEase(EaseType);
        }

        // Resting pose is Vector2.zero — CreateShowTween always tweens to zero (never back to a
        // per-instance originalPos), so zero already is this transition's canonical resting pose.
        public override void RestoreOnCancel(UIViewBase view)
        {
            var rt = view != null ? view.RectTransform : null;
            if (rt != null) rt.anchoredPosition = Vector2.zero;
        }

        private Vector2 GetOffset() => Direction switch
        {
            SlideDirection.Up    => new Vector2(0f,  SlideDistance),
            SlideDirection.Down  => new Vector2(0f, -SlideDistance),
            SlideDirection.Left  => new Vector2(-SlideDistance, 0f),
            SlideDirection.Right => new Vector2( SlideDistance, 0f),
            _                    => Vector2.zero
        };
    }
}
