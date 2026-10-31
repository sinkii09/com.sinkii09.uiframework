using System;

namespace Sinkii09.UIFramework
{
    [Flags]
    internal enum AudioPauseReason
    {
        None = 0,
        Hidden = 1,
        Paused = 2,
        Unfocused = 4,
    }

    // Why audio is paused, as a set of independent reasons. Audio resumes only when EVERY reason has
    // cleared: with a single bool, regaining focus would unpause a tab that is still hidden.
    // Focus counts only on standalone desktop players. On Android the virtual keyboard raises
    // focus(false), and on WebGL clicking outside an embedded canvas does, and neither should mute.
    internal sealed class PauseController
    {
        private readonly Action<bool> _applyPause;
        private readonly Action _resumeContext;
        private readonly bool _trackFocus;
        private AudioPauseReason _reasons;

        public PauseController(Action<bool> applyPause, Action resumeContext, bool trackFocus)
        {
            _applyPause = applyPause;
            _resumeContext = resumeContext;
            _trackFocus = trackFocus;
        }

        public AudioPauseReason Reasons => _reasons;
        public bool IsPaused => _reasons != AudioPauseReason.None;

        // With domain reload off, a pause left set by the previous play session would otherwise persist.
        public void Reset()
        {
            _reasons = AudioPauseReason.None;
            _applyPause(false);
        }

        public void OnHidden() => Set(AudioPauseReason.Hidden, true);

        // iOS Safari leaves the audio context "interrupted" after a tab switch or lock; it needs an
        // explicit resume, and the page becoming visible is the moment to try.
        public void OnVisible()
        {
            Set(AudioPauseReason.Hidden, false);
            _resumeContext();
        }

        public void OnApplicationPause(bool paused) => Set(AudioPauseReason.Paused, paused);

        public void OnFocus(bool focused)
        {
            if (_trackFocus) Set(AudioPauseReason.Unfocused, !focused);
        }

        private void Set(AudioPauseReason reason, bool on)
        {
            bool wasPaused = IsPaused;
            _reasons = on ? _reasons | reason : _reasons & ~reason;
            if (wasPaused != IsPaused) _applyPause(IsPaused);
        }
    }
}
