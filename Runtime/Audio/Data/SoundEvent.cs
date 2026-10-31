using UnityEngine;

namespace Sinkii09.UIFramework
{
    // One playable sound. Identity is the hash of Path (SoundId), so renaming the asset file is free
    // but changing Path changes the id that code and sound references point at.
    [CreateAssetMenu(menuName = "UIFramework/Audio/Sound Event", fileName = "SoundEvent")]
    public sealed class SoundEvent : ScriptableObject
    {
        [Tooltip("Stable identity, e.g. ui/click. Hashed into the SoundId.")]
        public string Path;
        public AudioClip[] Clips = System.Array.Empty<AudioClip>();
        public SoundContainerMode Container = SoundContainerMode.Random;
        [Tooltip("Random container only: how many recent picks are never repeated.")]
        [Min(0)] public int AvoidRepeat = 1;

        [Header("Variation")]
        public float VolumeDb;
        [Min(0f)] public float VolumeJitterDb;
        public float PitchSemitones;
        [Min(0f)] public float PitchJitterSemitones;
        [Min(0f)] public float StartDelaySeconds;
        public bool Loop;

        [Header("Rules")]
        public SoundGroup Group;
        public bool OverridePriority;
        [Tooltip("Higher is more important (the reverse of AudioSource.priority).")]
        [Range(0, 255)] public int Priority = 128;
        public bool OverrideMaxInstances;
        [Min(1)] public int MaxInstances = 1;
        public bool OverrideCooldown;
        [Min(0f)] public float CooldownSeconds;

        [Tooltip("Written by the loudness pipeline; the validator flags outliers.")]
        public float MeasuredLoudness;

        public SoundId Id => SoundId.FromPath(Path);
    }
}
