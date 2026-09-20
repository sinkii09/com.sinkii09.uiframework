namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Default <see cref="INotificationService"/> when no <see cref="NotificationHostView"/> exists
    /// anywhere in the scene. Keeps callers free of null-checks: a project that never places a
    /// notification host behaves exactly as it did before this feature existed.
    /// </summary>
    public sealed class NullNotificationService : INotificationService, INotificationSuspender
    {
        public int ActiveCount => 0;

        public void Notify(in NotificationRequest request) { }
        public void Dismiss(in NotificationKey key) { }
        public void DismissAll() { }

        // There are no toasts to hold back, but the token still has to exist: a caller wrapping a
        // reward animation in `using (suspender.Suspend(...))` must compile and behave identically
        // in a project that never placed a NotificationHostView.
        public System.IDisposable Suspend(string reason) => NullSuspension.Instance;

        private sealed class NullSuspension : System.IDisposable
        {
            internal static readonly NullSuspension Instance = new();
            public void Dispose() { }
        }
    }
}
