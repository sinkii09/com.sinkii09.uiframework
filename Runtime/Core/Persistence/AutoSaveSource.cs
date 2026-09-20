using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Sinkii09.UIFramework
{
    // One registered key's state inside AutoSaveScheduler. Split out to keep that class readable and
    // under the file-size limit, the same reason SaveEventPublisher was split out of JsonSaveService.
    //
    // The three delegates are built once, in Register&lt;T&gt;, so the generic type argument is captured
    // in a closure instead of being recovered by reflection at write time.
    internal sealed class AutoSaveSource
    {
        private readonly Func<object> _snapshot;
        private readonly Func<ISaveService, object, CancellationToken, UniTask> _saveAsync;
        private readonly Func<ISynchronousSaveService, object, bool> _saveSync;

        internal AutoSaveSource(Func<object> snapshot,
                                Func<ISaveService, object, CancellationToken, UniTask> saveAsync,
                                Func<ISynchronousSaveService, object, bool> saveSync)
        {
            _snapshot = snapshot;
            _saveAsync = saveAsync;
            _saveSync = saveSync;
        }

        internal bool Dirty { get; private set; }
        internal bool Saving { get; set; }

        // Preserved so both Tick's failure logger and FlushAsync can await the same write. A bare
        // UniTask is single-consumption and awaiting it twice is undefined behaviour.
        internal UniTask InFlight { get; set; } = UniTask.CompletedTask;

        private float _sinceLastMark;
        private float _sinceFirstMark;

        internal object Snapshot() => _snapshot();

        internal UniTask SaveAsync(ISaveService saves, object data, CancellationToken ct)
            => _saveAsync(saves, data, ct);

        internal bool SaveSync(ISynchronousSaveService saves, object data) => _saveSync(saves, data);

        internal void Mark()
        {
            // Only the debounce clock restarts. The max-latency clock keeps running from the FIRST
            // mark of this dirty streak — otherwise a value that changes every frame would reset the
            // debounce forever and never be written at all.
            _sinceLastMark = 0f;
            if (!Dirty) _sinceFirstMark = 0f;
            Dirty = true;
        }

        internal void Advance(float dt)
        {
            _sinceLastMark += dt;
            _sinceFirstMark += dt;
        }

        internal bool IsDue(float debounceSeconds, float maxLatencySeconds)
            => _sinceLastMark >= debounceSeconds || _sinceFirstMark >= maxLatencySeconds;

        // Stays dirty, but both clocks restart, so a retry lands one debounce window later instead
        // of on the very next frame. Without this a snapshot that keeps failing calls game code and
        // logs an error at frame rate for the rest of the session.
        internal void Defer()
        {
            _sinceLastMark = 0f;
            _sinceFirstMark = 0f;
        }

        internal void ClearDirty()
        {
            Dirty = false;
            _sinceLastMark = 0f;
            _sinceFirstMark = 0f;
        }
    }
}
