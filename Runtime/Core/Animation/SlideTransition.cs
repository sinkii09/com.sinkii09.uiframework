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

            var originalPos = rt.anchoredPosition;
            var offset = GetOffset();
            rt.anchoredPosition = offset;

            // OnKill: restore original position if tween is killed mid-animation (e.g. cancellation).
            return rt.DOAnchorPos(Vector2.zero, Duration)
                .SetEase(EaseType)
                .OnKill(() => { if (rt != null) rt.anchoredPosition = originalPos; });
        }

        public override Tween CreateHideTween(UIViewBase view)
        {
            var rt = view.RectTransform;
            if (rt == null)
            {
                Debug.LogError($"[SlideTransition] {view.ViewId} has no RectTransform — slide requires UGUI.");
                return DOTween.To(() => 0f, _ => { }, 1f, Duration);
            }

            var restingPos = rt.anchoredPosition;
            // OnKill: restore resting position if cancelled mid-slide-out.
            return rt.DOAnchorPos(GetOffset(), Duration)
                .SetEase(EaseType)
                .OnKill(() => { if (rt != null) rt.anchoredPosition = restingPos; });
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
