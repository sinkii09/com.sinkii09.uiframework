namespace Sinkii09.UIFramework
{
    // The engine's copy of a SoundEvent. Overridable rules start from the group's values; the
    // factory that builds this from the asset applies the event's overrides on top.
    internal sealed class EventDefinition
    {
        public EventDefinition(SoundId id, GroupDefinition group, int clipCount)
        {
            Id = id;
            Group = group;
            ClipCount = clipCount;
            Priority = group.Priority;
            MaxInstances = group.MaxInstancesPerEvent;
            CooldownSeconds = group.CooldownSeconds;
        }

        public readonly SoundId Id;
        public readonly GroupDefinition Group;
        public readonly int ClipCount;

        // Opaque to the engine; the backend uses it to find the clip (the SoundEvent asset).
        public object Payload;
        public string Path;

        public SoundContainerMode Container = SoundContainerMode.Random;
        public int AvoidRepeat = 1;
        public float VolumeDb;
        public float VolumeJitterDb;
        public float PitchSemitones;
        public float PitchJitterSemitones;
        public float StartDelaySeconds;
        public bool Loop;

        public int Priority;
        public int MaxInstances;
        public float CooldownSeconds;

        // Engine-owned runtime state, allocated once at registration, never per play.
        internal int Playing;
        internal double LastAccepted = double.NegativeInfinity;
        internal int[] Recent;
        internal int RecentCount;
        internal int RecentNext;
        internal int[] Bag;
        internal int BagPosition;
        internal int LastPicked = -1;
    }
}
