namespace Sinkii09.UIFramework
{
    // Activation for a show, and the resident-view pre-hide that must not undo it.
    public abstract partial class UIViewBase
    {
        // True only while ActivateForShow's SetActive(true) runs. A view saved inactive runs its Awake
        // inside that call, and a resident view's PreHide must not undo the activation that shows it.
        private bool _activatingForShow;

        // Every framework show path activates through here (ShowAsync, TooltipService,
        // NotificationService), so PreHide can tell a view woken to be shown from one woken by its
        // scene loading. Callers that own alpha write it AFTER this call: CanvasGroup is only cached in
        // Awake, so on a view that has never woken it is still null before. Not covered: a view whose
        // PARENT is inactive here wakes later with the parent, outside this call, and pre-hides.
        internal void ActivateForShow()
        {
            if (gameObject.activeSelf) return;
            _activatingForShow = true;
            try
            {
                gameObject.SetActive(true);
            }
            finally
            {
                _activatingForShow = false;
            }
        }

        // Resident views (scene-placed, shown on demand) call this from Awake so the first frame after a
        // scene load never flashes them. Skipped when Awake runs inside a show's own activation: a view
        // saved inactive wakes there, and hiding it would silently cancel that first show.
        private protected void PreHide()
        {
            if (_activatingForShow) return;
            CanvasGroup.alpha = 0f;
            gameObject.SetActive(false);
        }
    }
}
