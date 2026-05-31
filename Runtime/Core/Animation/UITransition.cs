using DG.Tweening;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Base class for all inspector-assignable UI transitions.
    // Subclass and override CreateShowTween / CreateHideTween to define animation behaviour.
    // SetUpdate(true) is applied by DOTweenUIAnimator — transitions do not call it themselves.
    public abstract class UITransition : ScriptableObject
    {
        public float Duration = 0.25f;
        public Ease EaseType = Ease.OutQuad;

        public abstract Tween CreateShowTween(UIViewBase view);
        public abstract Tween CreateHideTween(UIViewBase view);
    }
}
