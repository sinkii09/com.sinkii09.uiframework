namespace Sinkii09.UIFramework
{
    internal enum InstanceState : byte
    {
        Free,
        Pending,
        Playing,
        Stopping,
    }

    // One play request. A struct array, so the engine's bookkeeping never allocates.
    internal struct InstanceSlot
    {
        public InstanceState State;
        // Bumped every time the slot is freed; a handle with an older generation is stale.
        public int Generation;
        public EventDefinition Event;
        // Backend voice index, or -1 while pending.
        public int Voice;
        // Start order, for StealOldest and priority ties.
        public long Sequence;
        public double StartAt;
        public int ClipIndex;
        public int Priority;
        // Event, group, options and jitter, before bus gain.
        public float VoiceLinear;
        public float Pitch;
        // Last gain pushed to the backend, so unchanged gains are not re-sent every frame.
        public float AppliedGain;
    }
}
