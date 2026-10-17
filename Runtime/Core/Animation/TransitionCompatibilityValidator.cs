using System.Collections.Generic;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Authoring guard for transition/view pairings that compile and run but animate wrongly.
    // Editor and development builds only — the call site is stripped elsewhere, like UIViewValidator.
    //
    // Only one pairing is checked today: SlideTransition on a tooltip. TooltipPositioner writes the
    // tooltip's anchoredPosition, and Slide tweens that same property toward Vector2.zero, so the
    // tooltip slides to the layer's centre instead of its anchor. Juice-on-view-root is caught at
    // runtime by JuiceScale.CaptureBase, which sees the actual target rather than guessing it here.
    internal static class TransitionCompatibilityValidator
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // One report per view type per session, matching UIViewValidator: the fault is in the prefab.
        private static readonly HashSet<System.Type> _reportedTypes = new();

        // Statics survive play-mode entry when domain reload is disabled (Enter Play Mode Options),
        // which would silently suppress every report after the first session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetReports() => _reportedTypes.Clear();
#endif

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void Validate(UIViewBase view, UITransition show, UITransition hide)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (view is not TooltipViewBase) return;
            if (!ContainsSlide(show) && !ContainsSlide(hide)) return;
            if (!_reportedTypes.Add(view.GetType())) return;

            Debug.LogWarning(
                $"[UIFramework] Tooltip '{view.name}' uses a SlideTransition. TooltipPositioner owns a " +
                "tooltip's position and Slide overwrites it — use Fade, Scale or Pop.",
                view);
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static bool ContainsSlide(UITransition transition)
        {
            if (transition is SlideTransition) return true;
            if (transition is not SequenceTransition sequence || sequence.Transitions == null) return false;

            foreach (var child in sequence.Transitions)
                if (ContainsSlide(child)) return true;
            return false;
        }
#endif
    }
}
