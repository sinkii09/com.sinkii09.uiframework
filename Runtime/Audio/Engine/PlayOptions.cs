namespace Sinkii09.UIFramework
{
    // Per-call variation on top of the event's own. default = play the event as authored.
    public readonly struct PlayOptions
    {
        public PlayOptions(float volumeDb, float pitchSemitones = 0f)
        {
            VolumeDb = volumeDb;
            PitchSemitones = pitchSemitones;
        }

        public float VolumeDb { get; }
        public float PitchSemitones { get; }
    }
}
