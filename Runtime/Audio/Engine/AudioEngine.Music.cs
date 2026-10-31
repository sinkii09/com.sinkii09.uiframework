namespace Sinkii09.UIFramework
{
    // Music plays outside the voice pool (a crossfade needs two dedicated sources), but it still
    // takes its clip choice and its gain from the engine, so mute, ducking and Master apply to it.
    internal sealed partial class AudioEngine
    {
        public bool TryPrepareMusic(SoundId id, out EventDefinition sound, out int clipIndex, out float voiceLinear)
        {
            if (!_events.TryGetValue(id, out sound))
            {
                _counters.Missing++;
                clipIndex = 0;
                voiceLinear = 0f;
                return false;
            }

            clipIndex = SoundVariation.PickClip(sound, ref _random);
            voiceLinear = AudioMath.ToLinear(sound.VolumeDb + sound.Group.VolumeDb);
            return true;
        }
    }
}
