using DG.Tweening;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Diagonal tile wipe for a full-screen curtain such as the framework's TransitionOverlayView. Drives the
    /// <see cref="PatternWipeSurface"/> under the view.
    /// <list type="bullet">
    /// <item><b>Cover</b> — assign as the Show transition: tiles grow, sweeping corner to corner, until the
    /// screen is hidden.</item>
    /// <item><b>Reveal</b> — assign as the Hide transition: tiles shrink, sweeping on, until it is clear.</item>
    /// </list>
    /// Stateless, as every UITransition must be: one asset serves every view that references it.
    ///
    /// <para>The framework keeps the curtain's <c>blocksRaycasts</c> off while a show tween runs, so the
    /// screen underneath stays clickable during the cover — the same as with a fade.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "UIFramework/Transitions/Pattern Wipe", fileName = "PatternWipeTransition")]
    public sealed class PatternWipeTransition : UITransition
    {
        public enum Mode
        {
            Cover,
            Reveal,
        }

        [Tooltip("Cover: tiles grow until the screen is hidden (use as Show). Reveal: tiles shrink away (use as Hide).")]
        [SerializeField] private Mode _mode = Mode.Cover;   // serialized name is data: renaming it loads Reveal assets as Cover

        internal Mode WipeMode { get => _mode; set => _mode = value; }

        public override Tween CreateShowTween(UIViewBase view) => CreateTween(view);

        public override Tween CreateHideTween(UIViewBase view) => CreateTween(view);

        /// <summary>
        /// The view's canonical VISIBLE pose — fully covered — in both modes, matching the framework
        /// contract (Scale/Slide restore their shown value; the animator restores alpha). A constant,
        /// never anything captured from a view.
        /// </summary>
        public override void RestoreOnCancel(UIViewBase view)
        {
            PatternWipeSurface surface = view.GetComponentInChildren<PatternWipeSurface>(true);
            if (surface != null) surface.SetPose(1f, 0f);
        }

        private Tween CreateTween(UIViewBase view)
        {
            PatternWipeSurface surface = view.GetComponentInChildren<PatternWipeSurface>(true);
            if (surface == null)
            {
                Debug.LogError($"[PatternWipe] '{view.name}' has no PatternWipeSurface under it; nothing to wipe.", view);
                return DOTween.Sequence();
            }

            // The start pose is written now, not on the tween's first update: the animator has already set
            // the view's alpha to 1, and whatever the previous run left must not show for a frame.
            if (_mode == Mode.Cover)
            {
                surface.SetPose(0f, 0f);
                return DOTween.To(() => 0f, cover => surface.SetPose(cover, 0f), 1f, Duration).SetEase(EaseType);
            }

            surface.SetPose(1f, 0f);
            return DOTween.To(() => 0f, reveal => surface.SetPose(1f, reveal), 1f, Duration).SetEase(EaseType);
        }
    }
}
