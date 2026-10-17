using DG.Tweening;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // The "juicy popup" transition: shows by growing past full size and settling back (OutBack
    // overshoot) while fading in; hides quickly and quietly — a short shrink plus fade.
    // Hide is shorter than show on purpose: entrances deserve attention, exits should get out of the way.
    // Fades alpha itself (the fade is part of the look, not a visibility workaround — the animator
    // sets alpha to 1 before any show tween).
    // EaseType drives the FADE only. The scale legs are fixed (OutBack(Overshoot) in, InQuad out)
    // because the overshoot IS the pop; tune Overshoot instead.
    // SetUpdate(true) is applied by AwaitAsync — not needed here.
    [CreateAssetMenu(menuName = "UIFramework/Transitions/Pop", fileName = "PopTransition")]
    public class PopTransition : UITransition
    {
        public Vector3 StartScale = new Vector3(0.85f, 0.85f, 1f);

        [Tooltip("OutBack overshoot. DOTween's default is 1.70158; higher = bouncier.")]
        public float Overshoot = 1.70158f;

        public Vector3 HideScale = new Vector3(0.9f, 0.9f, 1f);
        public float HideDuration = 0.15f;

        public override Tween CreateShowTween(UIViewBase view)
        {
            var t = view.transform;
            var cg = view.CanvasGroup;
            var seq = DOTween.Sequence();
            seq.Join(t.DOScale(Vector3.one, Duration).From(StartScale).SetEase(Ease.OutBack, Overshoot));
            seq.Join(cg.DOFade(1f, Duration).From(0f).SetEase(EaseType));
            return seq;
        }

        public override Tween CreateHideTween(UIViewBase view)
        {
            var t = view.transform;
            var cg = view.CanvasGroup;
            var seq = DOTween.Sequence();
            seq.Join(t.DOScale(HideScale, HideDuration).From(Vector3.one).SetEase(Ease.InQuad));
            seq.Join(cg.DOFade(0f, HideDuration).SetEase(EaseType));
            return seq;
        }

        // Stateless, canonical resting pose — this asset is shared by every view that references it.
        public override void RestoreOnCancel(UIViewBase view)
        {
            ResetScale(view);
        }
    }
}
