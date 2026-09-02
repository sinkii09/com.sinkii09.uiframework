namespace Sinkii09.UIFramework
{
    // Ordering band for notifications. Higher wins when choosing which waiting notification is
    // promoted into a free slot.
    //
    // Spaced deliberately so a project can slot its own band between two of these without a
    // framework change, and so the numbers never need renumbering.
    public enum NotificationPriority
    {
        Normal = 0,
        Important = 10,
        Error = 20,
    }
}
