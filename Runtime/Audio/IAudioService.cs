using System.Threading;
using Cysharp.Threading.Tasks;

namespace Sinkii09.UIFramework
{
    /// <summary>The game's only door to audio. Always registered; see <see cref="NullAudioService"/>.</summary>
    /// <remarks>
    /// Main thread only. Never throws to callers: an unknown id, a full pool or a locked browser audio
    /// context returns an invalid handle, logs in Editor and development builds, and shows up in
    /// <see cref="Counters"/>. All fades are in milliseconds.
    /// </remarks>
    public interface IAudioService
    {
        PlayHandle Play(SoundId id, in PlayOptions options = default);

        /// <summary>Crossfades to the track. A no-op if it is already the current one.</summary>
        /// <remarks>On WebGL before the first user gesture, the request is kept and starts on unlock.</remarks>
        void PlayMusic(SoundId id, int fadeMs = 500);

        void StopMusic(int fadeMs = 500);

        /// <summary>Stops every pending and playing sound of the group. -1 uses the group's StopFadeMs.</summary>
        void StopGroup(SoundGroup group, int fadeMs = -1);

        /// <summary>Loads and registers a bank. Reference-counted per key; never throws.</summary>
        /// <returns>False if it failed or was cancelled; then call no <see cref="UnloadBank"/> for it.</returns>
        UniTask<bool> LoadBankAsync(string key, CancellationToken ct = default);

        /// <summary>Drops one reference. At zero, the bank's sounds are cut and its assets released.</summary>
        void UnloadBank(string key);

        /// <summary>Completes once resident banks are loaded, or the configured timeout passes. Never faults.</summary>
        UniTask ReadyAsync();

        AudioCounters Counters { get; }

        AudioPreferences Preferences { get; }
    }
}
