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
            _indicator.DOKill(); // cancel any in-flight tween before starting a new one
            _indicator.DOAnchorPos(target.anchoredPosition, _duration)
                .SetEase(Ease.OutCubic);
        }
    }
}
