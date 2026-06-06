using R3;
using UnityEngine.InputSystem;

namespace Sinkii09.UIFramework
{
    // Polls the new Input System Escape key each frame via R3 EveryUpdate.
    // Register in your LifetimeScope: builder.Register<IBackButtonSource, NewInputSystemBackButtonSource>(Lifetime.Singleton)
    public sealed class NewInputSystemBackButtonSource : IBackButtonSource
    {
        public Observable<Unit> OnBack =>
            Observable.EveryUpdate()
                .Where(_ => Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                .Select(_ => Unit.Default);
    }
}
