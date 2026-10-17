using DG.Tweening;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // The "juicy popup" transition: shows by growing past full size and settling back (OutBack
    // overshoot) while fading in; hides quickly and quietly — a short shrink plus fade.
    // Hide is shorter than show on purpose: entrances deserve attention, exits should get out of the way.
    // Fades alpha itself, like ZoomOutFadeTransition: the animator only snaps alpha AFTER the tween,
    // and after a hide it is 0, so a scale-only pop would play invisible and appear at the end.
    // SetUpdate(true) is applied by DOTweenUIAnimator — not needed here.
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
            if (view != null) view.transform.localScale = Vector3.one;
        }
    }
}
