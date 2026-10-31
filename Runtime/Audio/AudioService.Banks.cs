using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Bank registration and the resident-bank boot load.
    public sealed partial class AudioService
    {
        void ISoundBankSink.Register(SoundBank bank)
        {
            var events = bank.Events;
            if (events == null) return;
            for (int i = 0; i < events.Length; i++) _registry.Add(events[i]);
        }

        // The registry unregisters an event only when its LAST bank goes, and the engine then cuts
        // its pending and playing instances; music is cut here, before the clips are released.
        void ISoundBankSink.Unregister(SoundBank bank)
        {
            var events = bank.Events;
            if (events == null) return;
            for (int i = 0; i < events.Length; i++)
                if (_registry.Release(events[i])) _music.StopIfPlaying(events[i]);
        }

        private async UniTaskVoid LoadResidentBanksAsync()
        {
            var ready = _ready ??= new UniTaskCompletionSource();
            try
            {
                var keys = _config.ResidentBankKeys ?? Array.Empty<string>();
                var loads = new UniTask<bool>[keys.Length];
                for (int i = 0; i < keys.Length; i++) loads[i] = _banks.LoadAsync(keys[i], _lifetime.Token);

                var timeout = UniTask.Delay(TimeSpan.FromSeconds(_config.ReadyTimeoutSeconds), ignoreTimeScale: true,
                                            cancellationToken: _lifetime.Token);
                var (loaded, results) = await UniTask.WhenAny(UniTask.WhenAll(loads), timeout);

                if (!loaded)
                {
                    Debug.LogError($"[Audio] Resident banks still loading after {_config.ReadyTimeoutSeconds} s; " +
                                   "continuing without them. Sounds in them stay missing until they land.");
                    return;
                }
                for (int i = 0; i < results.Length; i++)
                    if (!results[i]) Debug.LogError($"[Audio] Resident bank '{keys[i]}' did not load; its sounds are missing.");
            }
            catch (OperationCanceledException)
            {
                // Disposed during boot.
            }
            finally
            {
                ready.TrySetResult();
            }
        }
    }
}
