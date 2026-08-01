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

        // Restore the view's transform state after a CANCELLED transition. Called by
        // DOTweenUIAnimator's catch blocks — NOT via Tween.OnKill, which TweenExtensions.AwaitAsync
        // overwrites (and which also fires on normal completion, so it was never a safe home for
        // restore logic even before AwaitAsync clobbered it). CanvasGroup.alpha is owned by the
        // animator, not by transitions. Restore to the canonical resting pose: every show tween
        // re-establishes its own start pose via .From()/explicit assignment, so a resting pose is
        // always a valid starting point for the next Show.
        // Transitions are shared ScriptableObject assets across views — this must stay stateless.
        // No transition may cache per-view state in a field to implement this.
        public virtual void RestoreOnCancel(UIViewBase view) { }
    }
}
