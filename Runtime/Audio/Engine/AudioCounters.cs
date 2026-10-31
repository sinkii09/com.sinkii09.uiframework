namespace Sinkii09.UIFramework
{
    // Running totals since the service started. The service never throws to callers, so these are
    // how a test or a debug overlay sees that requests are being refused.
    public struct AudioCounters
    {
        public int Played;
        // Refused by a voice limit, a full instance table or an exhausted pool.
        public int Rejected;
        // Older voices faded out to make room.
        public int Stolen;
        // Unknown id: the bank holding it is not loaded, or the id is wrong.
        public int Missing;
        // Deliberately not played: bus muted, audio context locked, or cooldown.
        public int Dropped;
    }
}
