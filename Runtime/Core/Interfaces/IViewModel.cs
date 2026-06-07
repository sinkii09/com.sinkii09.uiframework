using System;

namespace Sinkii09.UIFramework
{
    // Initialization is handled by VContainer injection; typed init goes through IViewModel<TArgs>
    public interface IViewModel : IDisposable
    {
        void OnShow();
        void Show();
    }

    public interface IViewModel<TArgs> : IViewModel where TArgs : IViewArgs
    {
        void Initialize(TArgs args);
    }
}
