namespace Sinkii09.UIFramework
{
    // Why AudioEngine.Play returned what it did. Internal: callers see a handle and AudioCounters.
    internal enum PlayStatus
    {
        Started,
        Pending,
        Missing,
        Muted,
        Paused,
        ContextLocked,
        Cooldown,
        Rejected,
    }
}
