using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Sinkii09.UIFramework
{
    // Coalescing autosave. Games mark state dirty; this decides when it is actually written.
    //
    // The coalescing is not an optimisation, it is the whole point: ISaveService rewrites the ENTIRE
    // JSON payload for a key on every save, so saving on each field change means rewriting the
    // player's whole progress per changed field. Marking is therefore cheap by construction and a
    // burst of marks costs exactly one write.
    public interface IAutoSaveScheduler
    {
        /// <summary>
        /// Declares where the payload for <paramref name="key"/> comes from.
        /// </summary>
        /// <param name="snapshot">
        /// Called on the MAIN THREAD at the moment a write is decided, never on the writing thread.
        /// It may therefore touch Unity APIs — and it must return a value that is safe to serialize
        /// afterwards, so hand back a copy rather than live mutable game state.
        /// </param>
        void Register<T>(string key, Func<T> snapshot) where T : class;

        /// <summary>Records that <paramref name="key"/> has changed. Cheap; call it freely.</summary>
        void MarkDirty(string key);

        /// <summary>Writes every dirty key now and waits for all of them.</summary>
        /// <remarks>
        /// For deliberate checkpoints — finishing a level, leaving a shop, before a scene load. The
        /// app-pause path does NOT use this: it cannot await. Awaiting this inside
        /// <c>OnApplicationPause</c> deadlocks.
        /// </remarks>
        UniTask FlushAsync(CancellationToken ct = default);
    }
}
