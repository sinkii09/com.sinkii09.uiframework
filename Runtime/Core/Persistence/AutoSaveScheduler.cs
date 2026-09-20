using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Sinkii09.UIFramework
{
    // Debounced, coalescing autosave, plus the one write that has to happen when the app is going
    // away. See IAutoSaveScheduler for the contract.
    //
    // Split across two files along the seam between DECIDING to write (here) and DOING it
    // (AutoSaveScheduler.Flushing.cs), which also keeps each under the file-size limit.
    //
    // Deliberately does NOT count in-flight saves by subscribing to ISaveService's R3 events:
    // SaveEventPublisher documents that those can be raised from a thread-pool thread, so the
    // counter would tear and any Unity API touched in the handler would throw. This class issues the
    // saves it cares about, so it already knows.
    public sealed partial class AutoSaveScheduler : IAutoSaveScheduler, ITickable, IDisposable
    {
        // Same clamp and same reason as NotificationService: one scene-load hitch must not read as
        // ten seconds of gameplay to every timer at once.
        internal const float MaxTickDelta = 0.1f;

        private readonly Dictionary<string, AutoSaveSource> _sources = new();
        private readonly List<string> _due = new();
        private readonly ISaveService _saves;
        private readonly ISynchronousSaveService _syncSaves;
        private readonly float _debounceSeconds;
        private readonly float _maxLatencySeconds;
        private readonly IDisposable _pauseSubscription;
        private bool _disposed;

        // [Inject] is MANDATORY, not decoration — the same trap documented on JsonSaveService and
        // LocalFileStorageBackend. VContainer picks the greediest constructor including non-public
        // ones, so the internal test seam below would win the election and the container would try
        // to resolve a System.Single that nothing registers.
        [Inject]
        public AutoSaveScheduler(ISaveService saves, AppLifecycleSignals signals, UIFrameworkConfig config)
            : this(saves, config.AutoSaveDebounceSeconds, config.AutoSaveMaxLatencySeconds)
        {
            // Pause only. Quit is not reliable on mobile and focus fires for the virtual keyboard;
            // see AppLifecycleSignals for both.
            _pauseSubscription = signals.OnPauseAsObservable
                .Where(paused => paused)
                .Subscribe(_ => FlushSynchronously());
        }

        // Test seam: no lifecycle component, explicit timings.
        internal AutoSaveScheduler(ISaveService saves, float debounceSeconds, float maxLatencySeconds)
        {
            _saves = saves ?? throw new ArgumentNullException(nameof(saves));
            _syncSaves = saves as ISynchronousSaveService;
            _debounceSeconds = Mathf.Max(0f, debounceSeconds);
            _maxLatencySeconds = Mathf.Max(_debounceSeconds, maxLatencySeconds);
        }

        public void Register<T>(string key, Func<T> snapshot) where T : class
        {
            SaveKeyResolver.ValidateKey(key);
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            _sources[key] = new AutoSaveSource(
                snapshot: () => snapshot(),
                saveAsync: (saves, data, ct) => saves.SaveAsync(key, (T)data, ct),
                saveSync: (saves, data) => saves.TrySaveSync(key, (T)data));
        }

        public void MarkDirty(string key)
        {
            if (!_sources.TryGetValue(key, out var source))
                throw new InvalidOperationException(
                    $"[AutoSave] '{key}' was marked dirty before Register<T> said where its payload " +
                    "comes from. Silently ignoring it would mean losing exactly this key at runtime.");

            source.Mark();
        }

        public void Tick() => Tick(Mathf.Min(Time.unscaledDeltaTime, MaxTickDelta));

        // The real seam. Public Tick() feeds it the clamped frame delta; tests feed an exact one.
        internal void Tick(float dt)
        {
            if (_disposed || _debounceSeconds <= 0f) return;

            foreach (var pair in _sources)
            {
                var source = pair.Value;
                if (!source.Dirty || source.Saving) continue;

                source.Advance(dt);
                if (source.IsDue(_debounceSeconds, _maxLatencySeconds))
                    _due.Add(pair.Key);
            }

            // Collected first, started second: Snapshot() is game code, and a snapshot that called
            // Register would mutate _sources mid-enumeration.
            for (var i = 0; i < _due.Count; i++)
                LogIfFailed(_due[i], BeginSave(_due[i], _sources[_due[i]], default)).Forget();

            _due.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _pauseSubscription?.Dispose();

            // Last chance to write. VContainer disposes entry points when the scope tears down — a
            // scene change, or quit on desktop where OnApplicationPause never fires at all — and
            // anything still inside its debounce window would otherwise vanish in exactly the way
            // this class exists to prevent.
            try
            {
                FlushSynchronously();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AutoSave] Flush during teardown failed: {ex}");
            }

            // Whatever the flush could not land is reported by name. Data loss that nobody can see
            // is the one outcome worth more noise than this costs.
            foreach (var pair in _sources)
            {
                if (pair.Value.Dirty)
                    Debug.LogError($"[AutoSave] '{pair.Key}' was still unsaved at teardown and is lost.");
            }

            _sources.Clear();
        }
    }
}
