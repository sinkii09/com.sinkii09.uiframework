using R3;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // The framework's only source of Unity application-lifecycle messages.
    //
    // A component of its own rather than methods on UIFrameworkLifetimeScope, which is the obvious
    // home because it owns the single DontDestroyOnLoad GameObject. Unity dispatches these messages
    // by NAME against the most-derived type, and UIFrameworkLifetimeScope is public and designed to
    // be subclassed: a game subclass declaring its own OnApplicationPause would HIDE the base
    // method, Unity would call only the derived one, and autosave would stop working with nothing
    // logged anywhere. A sealed component cannot be hidden.
    //
    // The scope adds this to its own GameObject. It is deliberately NOT something a game places in a
    // scene: a project that forgot would lose saves silently, which is the exact failure this exists
    // to prevent.
    [DisallowMultipleComponent]
    public sealed class AppLifecycleSignals : MonoBehaviour
    {
        private readonly Subject<bool> _pause = new();
        private readonly Subject<bool> _focus = new();
        private readonly Subject<Unit> _quit = new();
        private bool _disposed;

        /// <summary>Raised with <c>true</c> when the app is backgrounded.</summary>
        /// <remarks>
        /// On mobile this is the only reliable "you may be about to die" signal, so it is the one
        /// autosave hangs off. See <see cref="OnQuitAsObservable"/> for why quit is not.
        /// </remarks>
        public Observable<bool> OnPauseAsObservable => _pause;

        /// <summary>Raised on an explicit application quit.</summary>
        /// <remarks>
        /// Never the sole save trigger. iOS suspends instead of quitting, Android raises this only
        /// for an explicit <c>Application.Quit</c>, and an OS kill under memory pressure raises
        /// neither this nor pause. Nothing can be done about that last case; treat pause as the last
        /// moment guaranteed to run and treat quit as a bonus.
        /// </remarks>
        public Observable<Unit> OnQuitAsObservable => _quit;

        /// <summary>Raised with <c>false</c> when the app loses focus.</summary>
        /// <remarks>
        /// Exposed but deliberately NOT wired to saving. On Android, opening the virtual keyboard
        /// raises focus(false), so a save bound to this would rewrite the whole save file every time
        /// a player taps a text field.
        /// </remarks>
        public Observable<bool> OnFocusAsObservable => _focus;

        private void OnApplicationPause(bool paused) => Publish(_pause, paused);

        private void OnApplicationFocus(bool focused) => Publish(_focus, focused);

        private void OnApplicationQuit()
        {
            if (!_disposed) _quit.OnNext(Unit.Default);
        }

        // Unity can deliver a message to a component between OnDestroy and the object actually going
        // away — on play-mode exit in particular — and R3 subjects throw once disposed. Guarding is
        // cheaper than debugging one ObjectDisposedException in a quit path nobody steps through.
        private void Publish(Subject<bool> subject, bool value)
        {
            if (!_disposed) subject.OnNext(value);
        }

        private void OnDestroy()
        {
            _disposed = true;
            _pause.Dispose();
            _focus.Dispose();
            _quit.Dispose();
        }
    }
}
