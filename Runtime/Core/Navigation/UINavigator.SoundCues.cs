namespace Sinkii09.UIFramework
{
    public partial class UINavigator
    {
        // Push plays the show cue, pop the hide cue. Stack clears (CloseAll, state changes) and views
        // pushed by a state's OnEnterAsync stay silent: a screen change is not a popup opening, and a
        // burst of cues at boot would be noise. A view revealed by a pop fires no event, so it is silent too.
        private void PlayNavigationCue(IUIView view, bool show)
        {
            if (_audio == null || view is not UIViewBase viewBase) return;
            if (show && _stateTransitionActive) return;

            SoundId id = viewBase.ResolveNavigationCue(show, _audio.Theme);
            if (id.IsValid) _audio.Play(id);
        }
    }
}
