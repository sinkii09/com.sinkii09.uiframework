namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Transient toast notifications on their own UI layer. Deliberately NOT navigation views:
    /// they never enter <c>NavigationStack</c>, so they carry no back-button semantics, no
    /// depth accounting, and are not subject to the navigator's transition guard.
    ///
    /// <para>Registered unconditionally — the real service when a <see cref="NotificationHostView"/>
    /// exists in the scene, otherwise <see cref="NullNotificationService"/>. VContainer ignores C#
    /// optional-parameter defaults, so an unregistered dependency throws at container build rather
    /// than resolving to null. Same shape as <c>ITooltipService</c> and <c>ITransitionOverlay</c>.</para>
    /// </summary>
    public interface INotificationService
    {
        /// <summary>Notifications currently alive — visible plus waiting for a free slot.</summary>
        int ActiveCount { get; }

        /// <summary>
        /// Show a notification, or merge it into the live one with the same
        /// <see cref="NotificationKey"/>: quantity accumulates, the dismiss timer restarts, and
        /// priority rises to the higher of the two (never falls).
        ///
        /// <para>An already-visible notification is never displaced by a new arrival, whatever its
        /// priority — a toast vanishing mid-read is worse than an error waiting a moment. Priority
        /// decides which WAITING notification is promoted when a slot frees.</para>
        /// </summary>
        void Notify(in NotificationRequest request);

        /// <summary>Dismiss the notification with this key. Unknown keys are a no-op.</summary>
        void Dismiss(in NotificationKey key);

        /// <summary>Dismiss everything, visible and waiting.</summary>
        void DismissAll();
    }
}
