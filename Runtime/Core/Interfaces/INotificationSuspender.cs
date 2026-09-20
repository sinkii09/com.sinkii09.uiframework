using System;

namespace Sinkii09.UIFramework
{
    // Holds toasts back while something is covering them.
    //
    // The problem is a layer ordering the framework itself sets: Notification sits BELOW Overlay, so
    // a full-screen overlay draws on top of every toast. A reward toast raised during one is shown
    // to nobody — and in a game where the overlay IS the reward moment, that is the worst possible
    // time to lose it. Without this, the rule has to be re-implemented at every call site that
    // grants a reward, and the first one anybody forgets goes unnoticed because nothing errors.
    //
    // NotificationService already does exactly this for the loading curtain (ITransitionOverlay).
    // This is that same rule, opened to callers who know they are covering the screen in a way the
    // framework has no way to detect.
    //
    // A separate interface from INotificationService rather than a member on it: adding one there
    // breaks every implementer and forces a major version — the same reasoning that keeps
    // JsonSaveService.OnSaveRecoveredAsObservable off ISaveService. Both the real service and the
    // null one implement this, so a caller never has to check which it was handed.
    public interface INotificationSuspender
    {
        /// <summary>
        /// Holds back new toasts and freezes the dismiss timers of visible ones until the returned
        /// token is disposed. Reference-counted, so nested suspensions each hold their own token.
        /// </summary>
        /// <param name="reason">
        /// Named in the log if a suspension outlives its cap, so make it identify the caller —
        /// <c>"pull-animation"</c>, not <c>"ui"</c>.
        /// </param>
        /// <remarks>
        /// A suspension EXPIRES on its own after
        /// <see cref="UIFrameworkConfig.NotificationMaxSuspendSeconds"/> and logs an error naming the
        /// reason. Returning a token instead of a Resume() method makes a leak unlikely; the cap is
        /// what makes one survivable, because a queue held back forever fills up and then starts
        /// refusing notifications outright with nothing ever shown.
        /// </remarks>
        IDisposable Suspend(string reason);
    }
}
