using DG.Tweening;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    [CreateAssetMenu(menuName = "UIFramework/Transitions/Sequence", fileName = "SequenceTransition")]
    public class SequenceTransition : UITransition
    {
        public UITransition[] Transitions;

        public override Tween CreateShowTween(UIViewBase view)
        {
            var seq = DOTween.Sequence();
            if (Transitions != null)
                foreach (var t in Transitions)
                    if (t != null && t != this) seq.Append(t.CreateShowTween(view));
            return seq;
        }

        // Hide plays in reverse order to visually unwind the show sequence.
        public override Tween CreateHideTween(UIViewBase view)
        {
            var seq = DOTween.Sequence();
            if (Transitions != null)
                for (int i = Transitions.Length - 1; i >= 0; i--)
                    if (Transitions[i] != null && Transitions[i] != this) seq.Append(Transitions[i].CreateHideTween(view));
            return seq;
        }

        // Forward so nested transitions aren't silently left with their own dead restore-on-cancel.
        public override void RestoreOnCancel(UIViewBase view)
        {
            if (Transitions == null) return;
            foreach (var t in Transitions)
                if (t != null && t != this) t.RestoreOnCancel(view);
        }
    }
}
