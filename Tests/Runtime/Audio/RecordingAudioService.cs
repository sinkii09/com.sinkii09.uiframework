using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Sinkii09.UIFramework.Tests
{
    // Records Play calls so UI wiring can be asserted without real voices.
    internal sealed class RecordingAudioService : IAudioService
    {
        public readonly List<SoundId> Played = new();
        public UISoundTheme Theme { get; set; }

        public static UISoundTheme NewTheme() => ScriptableObjectWith(theme =>
        {
            theme.ButtonClick = new SoundRef("ui/click");
            theme.Toggle = new SoundRef("ui/toggle");
            theme.PopupShow = new SoundRef("ui/popup-show");
            theme.PopupHide = new SoundRef("ui/popup-hide");
        });

        public PlayHandle Play(SoundId id, in PlayOptions options = default)
        {
            Played.Add(id);
            return default;
        }

        public void PlayMusic(SoundId id, int fadeMs = 500) { }
        public void StopMusic(int fadeMs = 500) { }
        public void StopGroup(SoundGroup group, int fadeMs = -1) { }
        public UniTask<bool> LoadBankAsync(string key, CancellationToken ct = default) => UniTask.FromResult(false);
        public void UnloadBank(string key) { }
        public UniTask ReadyAsync() => UniTask.CompletedTask;
        public AudioCounters Counters => default;
        public AudioPreferences Preferences => null;

        private static UISoundTheme ScriptableObjectWith(System.Action<UISoundTheme> setup)
        {
            var theme = ScriptableObject.CreateInstance<UISoundTheme>();
            setup(theme);
            return theme;
        }
    }
}
