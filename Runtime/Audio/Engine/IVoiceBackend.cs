namespace Sinkii09.UIFramework
{
    // What the engine needs from whatever actually makes sound. The engine decides everything; the
    // backend only applies it, and never applies bus gain itself (that is already in `gain`).
    // VoiceCount may exceed the engine's playing cap: the surplus is fade headroom, so a stolen voice
    // can fade out while its replacement starts.
    internal interface IVoiceBackend
    {
        int VoiceCount { get; }

        void Start(int voice, EventDefinition sound, int clipIndex, float gain, float pitch);

        // fadeMs <= 0 stops at once and releases the clip reference; otherwise fade to silence, then stop.
        void Stop(int voice, int fadeMs);

        void SetGain(int voice, float gain);

        // Not consulted while the listener is paused: a paused source reports not playing.
        bool IsFinished(int voice);
    }
}
