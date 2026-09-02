namespace Sinkii09.UIFramework
{
    /// <summary>
    /// One call to <see cref="INotificationService.Notify"/>: an identity plus what to show.
    /// Requests with equal <see cref="Key"/>s merge; see <see cref="NotificationKey"/>.
    /// </summary>
    public readonly struct NotificationRequest
    {
        public readonly NotificationKey Key;
        public readonly NotificationContent Content;

        public NotificationRequest(in NotificationKey key, in NotificationContent content)
        {
            Key = key;
            Content = content;
        }

        public NotificationRequest(string category, string id, in NotificationContent content)
            : this(new NotificationKey(category, id), content) { }
    }
}
