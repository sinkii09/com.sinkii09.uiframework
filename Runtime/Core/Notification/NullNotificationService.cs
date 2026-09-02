namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Default <see cref="INotificationService"/> when no <see cref="NotificationHostView"/> exists
    /// anywhere in the scene. Keeps callers free of null-checks: a project that never places a
    /// notification host behaves exactly as it did before this feature existed.
    /// </summary>
    public sealed class NullNotificationService : INotificationService
    {
        public int ActiveCount => 0;

        public void Notify(in NotificationRequest request) { }
        public void Dismiss(in NotificationKey key) { }
        public void DismissAll() { }
    }
}
