using UnityEngine;

namespace Sinkii09.UIFramework
{
    // The single resident tooltip owner. Deliberately NOT a navigation view: UINavigator has an
    // _isTransitioning guard that silently drops concurrent calls, plus back-button semantics and
    // MaxNavigationDepth accounting — all wrong for something that fires ten times a second as a
    // pointer sweeps a grid.
    //
    // Registered unconditionally (real service or NullTooltipService) because VContainer ignores
    // C# optional-parameter defaults: an unregistered ITooltipService throws at container build
    // rather than resolving to null. Same shape as ITransitionOverlay and UIBackdrop.
    public interface ITooltipService
    {
        bool IsShown { get; }

        // The rect the currently shown (or pending) tooltip belongs to; null when idle.
        // Triggers compare against this to ignore stale exits.
        RectTransform CurrentAnchor { get; }

        // Long-press policy lives here, not on the trigger: triggers are plain widgets scattered
        // through prefabs with no injection of their own, and this keeps one source of truth for
        // every tooltip timing value.
        float LongPressSeconds { get; }
        float LongPressMoveCancelPixels { get; }

        // Idle/expired -> waits the show delay for Hover and Touch, shows instantly for Click and
        // Focus. Already shown or in grace -> shows instantly regardless of source, which is what
        // makes sweeping across a grid feel responsive rather than sluggish.
        void Show(in TooltipRequest request);

        // Begins the hide grace period. Ignored when `source` is not the current anchor — moving
        // between two triggers fires the new enter before the old exit, and a naive hide would
        // kill the tooltip that was just shown.
        void Hide(RectTransform source);

        // Unconditional teardown with no grace period, from any anchor. Used on navigation and
        // state changes, where a lingering tooltip would sit over the next screen.
        void HideImmediate();

        // Indexes a tooltip view created after boot. Views present in the UIRoot at boot are found
        // and injected automatically; this is the escape hatch for anything instantiated later.
        void Register(TooltipViewBase view);
    }
}
