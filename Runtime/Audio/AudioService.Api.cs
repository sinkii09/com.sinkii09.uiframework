using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // The IAudioService calls. None of them throws.
    public sealed partial class AudioService
    {
        // Release builds warn once per id rather than flooding the log from a sound fired every frame.
        // Static, so it is reset explicitly when domain reload is off.
        private static readonly HashSet<uint> s_warnedMissing = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetMissingWarnings() => s_warnedMissing.Clear();

        public PlayHandle Play(SoundId id, in PlayOptions options = default)
        {
            if (_engine == null)
            {
                ReportNotInitialized();
                return default;
            }

            var handle = _engine.Play(id, options, out var status);
            if (status == PlayStatus.Missing) ReportMissing(id);
            return handle;
        }

        public void PlayMusic(SoundId id, int fadeMs = 500)
        {
            if (_engine == null)
            {
                ReportNotInitialized();
                return;
            }
            if (_isWeb && !_gate.IsRunning)
            {
                _gate.QueueMusic(id, fadeMs);
                return;
            }
            StartMusic(id, fadeMs);
        }

        public void StopMusic(int fadeMs = 500)
        {
            if (_engine == null) return;
            _gate.ClearQueuedMusic();
            _music.Stop(fadeMs);
        }

        public void StopGroup(SoundGroup group, int fadeMs = -1)
        {
            if (_engine == null) return;
            var definition = _registry.GroupFor(group);
            if (definition != null) _engine.StopGroup(definition, fadeMs);
        }

        public UniTask<bool> LoadBankAsync(string key, CancellationToken ct = default)
            => _banks != null ? _banks.LoadAsync(key, ct) : UniTask.FromResult(false);

        public void UnloadBank(string key) => _banks?.Unload(key);

        // Lazily created so a caller awaiting before Initialize still gets the real completion, and
        // backed by a completion source so it can be awaited any number of times.
        public UniTask ReadyAsync() => (_ready ??= new UniTaskCompletionSource()).Task;

        private void StartMusic(SoundId id, int fadeMs)
        {
            if (!_engine.TryPrepareMusic(id, out var definition, out int clipIndex, out float voiceLinear))
            {
                ReportMissing(id);
                return;
            }
            var asset = (SoundEvent)definition.Payload;
            var clip = clipIndex < asset.Clips.Length ? asset.Clips[clipIndex] : null;
            _music.Play(asset, clip, definition.Group.Bus, voiceLinear, fadeMs);
        }

        private static void ReportMissing(SoundId id)
        {
            // An error in Editor and development builds, so a test hitting it fails through LogAssert.
            if (Debug.isDebugBuild)
            {
                Debug.LogError($"[Audio] No loaded bank registers {id}. Is its bank loaded, and is the id right?");
                return;
            }
            if (s_warnedMissing.Add(id.Value))
                Debug.LogWarning($"[Audio] No loaded bank registers {id}. Further misses of this id are not logged.");
        }

        private void ReportNotInitialized()
        {
            if (Debug.isDebugBuild)
                Debug.LogError("[Audio] AudioService used before Initialize (or after Dispose); the call was ignored.");
        }
    }
}
