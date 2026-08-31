using DG.Tweening;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Animates an indicator rect (underline, dot, etc.) to the selected tab's position.
    // Indicator and tab buttons must share the same parent coordinate space.
    public class TabIndicator : MonoBehaviour
    {
        [SerializeField] private RectTransform _indicator;
        [SerializeField] private float _duration = 0.2f;

        public void MoveTo(RectTransform target)
        {
            if (_indicator == null || target == null) return;

            // INVARIANT — read before adding any tween here. DOKill() stops tweens where they
            // stand and restores nothing. That is safe for anchoredPosition only because the very
            // next line writes a fresh target, so an interrupted move self-heals.
            //
            // It is NOT safe for scale, colour, or rotation: nothing else ever writes those, so a
            // kill mid-tween leaves them permanently at their interrupted value. If you add such a
            // tween to _indicator, this bare kill silently becomes a corruption site — capture the
            // baseline in Awake and restore it here, in the same method that kills.
            _indicator.DOKill();
            _indicator.DOAnchorPos(target.anchoredPosition, _duration)
                .SetEase(Ease.OutCubic);
        }
    }
}
