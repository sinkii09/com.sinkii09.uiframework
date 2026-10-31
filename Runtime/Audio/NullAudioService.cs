using System.Threading;
using Cysharp.Threading.Tasks;

namespace Sinkii09.UIFramework
{
    /// <summary>Registered when no AudioConfig is assigned, so IAudioService always resolves.</summary>
    /// <remarks>
    /// VContainer ignores C# default parameter values, so a view or service taking IAudioService would
    /// fail the container build in any game, scene or test without sound. This plays nothing,
    /// completes ReadyAsync at once and reports every bank as not loaded.
    /// </remarks>
    public sealed class NullAudioService : IAudioService
    {
        private AudioPreferences _preferences;

        public PlayHandle Play(SoundId id, in PlayOptions options = default) => default;
        public void PlayMusic(SoundId id, int fadeMs = 500) { }
        public void StopMusic(int fadeMs = 500) { }
        public void StopGroup(SoundGroup group, int fadeMs = -1) { }
        public UniTask<bool> LoadBankAsync(string key, CancellationToken ct = default) => UniTask.FromResult(false);
        public void UnloadBank(string key) { }
        public UniTask ReadyAsync() => UniTask.CompletedTask;
        public AudioCounters Counters => default;
        public UISoundTheme Theme => null;

        // Lazy: reading PlayerPrefs is not allowed during the container build on every platform.
        public AudioPreferences Preferences => _preferences ??= new AudioPreferences();
    }
}
