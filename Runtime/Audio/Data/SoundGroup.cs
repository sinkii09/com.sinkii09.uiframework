using UnityEngine;

namespace Sinkii09.UIFramework
{
    // A rule profile shared by many events: which bus they play on and how they compete for voices.
    // Thousands of events stay manageable because the rules live here, not on each event.
    [CreateAssetMenu(menuName = "UIFramework/Audio/Sound Group", fileName = "SoundGroup")]
    public sealed class SoundGroup : ScriptableObject
    {
        public AudioBus Bus = AudioBus.Sfx;
        public float VolumeDb;
        [Tooltip("Higher is more important (the reverse of AudioSource.priority). StealLowestPriority never takes a higher one.")]
        [Range(0, 255)] public int Priority = 128;

        [Header("Limits")]
        [Tooltip("Voices this group may have playing at once.")]
        [Min(1)] public int MaxVoices = 8;
        [Tooltip("Voices one event of this group may have playing at once, unless the event overrides it.")]
        [Min(1)] public int MaxInstancesPerEvent = 4;
        [Tooltip("Pool voices held back for this group so a flood elsewhere cannot starve it. UI groups usually reserve 2.")]
        [Min(0)] public int ReservedVoices;
        public LimitPolicy OnLimit = LimitPolicy.StealOldest;
        [Tooltip("Minimum seconds between accepted plays of the same event. 0 = none.")]
        [Min(0f)] public float CooldownSeconds;
        [Tooltip("Fade used when a voice is stolen or stopped without an explicit fade, so stops never click.")]
        [Min(0)] public int StopFadeMs = 10;

        public DuckingSettings Ducking;
    }
}
