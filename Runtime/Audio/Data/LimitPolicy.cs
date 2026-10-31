namespace Sinkii09.UIFramework
{
    // What a group does when a play request hits a voice limit (per event, per group or the pool).
    // Ties between equally eligible victims always go to the oldest.
    public enum LimitPolicy
    {
        Reject = 0,
        StealOldest = 1,
        StealLowestPriority = 2,
    }
}
