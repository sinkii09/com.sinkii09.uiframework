using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Sinkii09.UIFramework
{
    /// <summary>The built-in IAudioService: the pure engine plus a Unity AudioSource backend.</summary>
    /// <remarks>
    /// Registered as an entry point when UIFrameworkConfig.Audio is assigned. Everything is built in
    /// Initialize, which starts loading the resident banks; GameLifecycleManager awaits ReadyAsync
    /// before the first queued state, so UI sounds are loaded by the time a player can tap.
    /// </remarks>
    public sealed partial class AudioService : IAudioService, IInitializable, ITickable, IDisposable, ISoundBankSink
    {
        private readonly AudioConfig _config;
        private readonly IAssetLoader _loader;
        private readonly AppLifecycleSignals _signals;
        private readonly CancellationTokenSource _lifetime = new();

        private AudioEngine _engine;
        private UnityVoiceBackend _backend;
        private MusicPlayer _music;
        private SoundRegistry _registry;
        private BankManager _banks;
        private AudioContextGate _gate;
        private PauseController _pause;
        private AudioPreferences _preferences;
        private UniTaskCompletionSource _ready;
        private DisposableBag _subscriptions;
        private bool _isWeb;
        private bool _disposed;

        [Inject]
        public AudioService(UIFrameworkConfig config, IAssetLoader loader, AppLifecycleSignals signals)
        {
            _config = config.Audio != null
                ? config.Audio
                : throw new ArgumentException("UIFrameworkConfig.Audio is not assigned.", nameof(config));
            _loader = loader;
            _signals = signals;
        }

        public AudioCounters Counters => _engine?.Counters ?? default;

        public AudioPreferences Preferences => _preferences ??= new AudioPreferences();

        public void Initialize()
        {
            if (_engine != null || _disposed) return;

            var platform = Application.platform;
            _isWeb = platform == RuntimePlatform.WebGLPlayer;
            int pool = _config.PoolSizeFor(platform);
            // Fade headroom: a stolen voice fades on one of these while its replacement starts.
            int headroom = Math.Max(4, pool / 4);

            var parent = _signals.transform;
            _backend = new UnityVoiceBackend(parent, pool + headroom);
            _engine = new AudioEngine(_backend, pool, _config.MaxTickDeltaSeconds, message => Debug.LogError(message));
            _music = new MusicPlayer(parent);
            _registry = new SoundRegistry(_engine);
            _banks = new BankManager(_loader, this);
            _gate = new AudioContextGate(WebGlAudioBridge.QueryContextState);
            _pause = new PauseController(ApplyPause, WebGlAudioBridge.ResumeContext, IsStandaloneDesktop(platform));
            _pause.Reset();

            if (_isWeb) _gate.Refresh();
            _engine.ContextRunning = _gate.IsRunning;

            Preferences.Changed += ApplyPreferences;
            ApplyPreferences();

            _signals.OnHiddenAsObservable.Subscribe(this, (_, self) => self._pause.OnHidden()).AddTo(ref _subscriptions);
            _signals.OnVisibleAsObservable.Subscribe(this, (_, self) => self._pause.OnVisible()).AddTo(ref _subscriptions);
            _signals.OnPauseAsObservable.Subscribe(this, (paused, self) => self._pause.OnApplicationPause(paused)).AddTo(ref _subscriptions);
            _signals.OnFocusAsObservable.Subscribe(this, (focused, self) => self._pause.OnFocus(focused)).AddTo(ref _subscriptions);

            LoadResidentBanksAsync().Forget();
        }

        public void Tick()
        {
            if (_engine == null) return;

            // Unscaled: the game pauses with timeScale = 0, and fades must still finish in a pause menu.
            float dt = Time.unscaledDeltaTime;
            float clamped = Math.Min(dt, _config.MaxTickDeltaSeconds);

            if (_isWeb && _gate.Refresh() && _gate.TryTakeQueuedMusic(out var music, out int fadeMs))
                StartMusic(music, fadeMs);

            _engine.ContextRunning = _gate.IsRunning;
            _engine.Paused = _pause.IsPaused;
            _engine.Tick(dt);
            _backend.Tick(clamped);
            _music.Tick(clamped, _engine);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _subscriptions.Dispose();
            _lifetime.Cancel();
            _lifetime.Dispose();
            if (_preferences != null) _preferences.Changed -= ApplyPreferences;

            // Voices first, so no source still references a clip when the banks release their assets.
            _engine?.StopAll();
            _music?.Dispose();
            _backend?.Dispose();
            _banks?.Dispose();

            AudioListener.pause = false;
            _ready?.TrySetResult();

            // The sources are destroyed: every later call must take the not-initialized path rather
            // than reach a dead AudioSource (e.g. a view's close sound during scope teardown).
            _engine = null;
            _registry = null;
            _banks = null;
        }

        // The engine is told at once, not on the next Tick: a Play in the same frame as the hide
        // must already be refused.
        private void ApplyPause(bool paused)
        {
            AudioListener.pause = paused;
            if (_engine != null) _engine.Paused = paused;
        }

        private void ApplyPreferences()
        {
            if (_engine == null) return;
            _engine.SetBusMuted(AudioBus.Music, !Preferences.MusicEnabled);
            _engine.SetBusMuted(AudioBus.Sfx, !Preferences.SfxEnabled);
            _engine.SetBusMuted(AudioBus.Ui, !Preferences.SfxEnabled);
        }

        private static bool IsStandaloneDesktop(RuntimePlatform platform)
            => platform == RuntimePlatform.WindowsPlayer
               || platform == RuntimePlatform.OSXPlayer
               || platform == RuntimePlatform.LinuxPlayer;
    }
}
