using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Assigned to UIFrameworkConfig.Audio. Leaving that empty registers NullAudioService, so a game
    // without sound still resolves IAudioService everywhere.
    [CreateAssetMenu(menuName = "UIFramework/Audio/Audio Config", fileName = "AudioConfig")]
    public sealed class AudioConfig : ScriptableObject
    {
        [Tooltip("Voices that may play at once on WebGL.")]
        [Min(1)] public int PoolSizeWeb = 24;
        [Tooltip("Voices that may play at once on every other platform.")]
        [Min(1)] public int PoolSizeDefault = 32;

        [Tooltip("Banks loaded at boot and kept for the session. UI and theme sounds must live in one of these.")]
        public string[] ResidentBankKeys = System.Array.Empty<string>();

        [Tooltip("Largest step the engine advances per frame, so a resume after a long pause never jumps fades or envelopes.")]
        [Min(0.001f)] public float MaxTickDeltaSeconds = 0.1f;

        [Tooltip("ReadyAsync completes after this many seconds even if resident banks are still loading.")]
        [Min(0f)] public float ReadyTimeoutSeconds = 5f;

        public int PoolSizeFor(RuntimePlatform platform)
            => platform == RuntimePlatform.WebGLPlayer ? PoolSizeWeb : PoolSizeDefault;
    }
}
