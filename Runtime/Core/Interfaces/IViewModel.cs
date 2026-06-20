using System;

namespace Sinkii09.UIFramework
{
    // Initialization is handled by VContainer injection; typed init goes through IViewModel<TArgs>
    public interface IViewModel : IDisposable
    {
        void OnShow();
        // Called by UIView.HideAsync after the hide animation completes — triggers OnHide() and disposes per-show bindings.
        // Named NotifyHide (not Show) to clarify this is the hide-side teardown hook, not a show-side method.
        void NotifyHide();
    }

    public interface IViewModel<TArgs> : IViewModel where TArgs : IViewArgs
    {
        void Initialize(TArgs args);
    }
}
