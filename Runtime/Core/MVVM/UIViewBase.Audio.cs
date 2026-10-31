using UnityEngine;
using VContainer;

namespace Sinkii09.UIFramework
{
    public abstract partial class UIViewBase
    {
        [SerializeField] private ViewSoundCue _showSound;
        [SerializeField] private ViewSoundCue _hideSound;

        // Null on a scene-placed view that nothing injected. UIButtonSound reports that in development builds.
        internal IAudioService Audio { get; private set; }

        // Separate from Construct so the animator injection keeps its signature. IAudioService is
        // always registered (NullAudioService without an AudioConfig), so this never fails a real scope.
        [Inject]
        private void InjectAudio(IAudioService audio) => Audio = audio;

        internal SoundId ResolveNavigationCue(bool show, UISoundTheme theme)
        {
            var cue = show ? _showSound : _hideSound;
            var themeCue = theme == null ? default : show ? theme.PopupShow : theme.PopupHide;
            return cue.Resolve(Layer, themeCue);
        }
    }
}
